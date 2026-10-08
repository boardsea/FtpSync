using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace FtpSync
{
	public class RemoteEntry
	{
		public string Name, Path, Perms = "";
		public bool IsDir, IsLink;
		public long Size;
		public DateTime? Modified;
	}

	public interface IRemote : IDisposable
	{
		/// <summary>File contents, or null when the file does not exist.</summary>
		byte[] Download(string remotePath);
		void Upload(string remotePath, byte[] data);
	}

	public delegate void ProgressFn(long done, long total);

	public interface IRemoteFs : IRemote
	{
		List<RemoteEntry> List(string dir);
		void MakeDir(string path);
		void Delete(string path, bool isDir);
		void Rename(string from, string to);
		/// <summary>Directory the server puts us in after login.</summary>
		string GetHome();
		byte[] Download(string remotePath, ProgressFn progress);
		void Upload(string remotePath, byte[] data, ProgressFn progress);
	}

	public static class RemoteFactory
	{
		public static int TimeoutMs = 10000;
		public static Func<Profile, IRemote> Override; // used by tests
		static readonly Dictionary<string, string> passwords = new Dictionary<string, string>();
		static readonly Dictionary<string, SftpRemote> pool = new Dictionary<string, SftpRemote>();

		public static void SetPassword(Profile p, string pw) { lock (passwords) passwords[p.Name] = pw; }
		public static string PasswordFor(Profile p)
		{
			lock (passwords) { string s; if (passwords.TryGetValue(p.Name, out s)) return s; }
			return p.Password;
		}
		public static bool NeedsPassword(Profile p)
		{
			if (p.Protocol == Protocol.Sftp && !string.IsNullOrEmpty(p.KeyFile)) return false;
			return string.IsNullOrEmpty(PasswordFor(p));
		}

		public static IRemote Create(Profile p) { return CreateFs(p); }

		public static IRemoteFs CreateFs(Profile p)
		{
			if (Override != null) { IRemote o = Override(p); return o as IRemoteFs ?? new Adapter(o); }
			if (p.Protocol == Protocol.Sftp) return new SftpLease(Pooled(p));
			return new FtpRemote(p, TimeoutMs);
		}

		static SftpRemote Pooled(Profile p)
		{
			lock (pool)
			{
				string key = p.Name + "|" + p.Host + "|" + p.Port + "|" + p.User;
				SftpRemote r;
				if (!pool.TryGetValue(key, out r)) { r = new SftpRemote(p, TimeoutMs); pool[key] = r; }
				return r;
			}
		}

		public static void Disconnect(Profile p)
		{
			lock (pool)
			{
				string key = p.Name + "|" + p.Host + "|" + p.Port + "|" + p.User;
				SftpRemote r;
				if (pool.TryGetValue(key, out r)) { r.Close(); pool.Remove(key); }
			}
		}

		public static void DisconnectAll()
		{
			lock (pool) { foreach (SftpRemote r in pool.Values) r.Close(); pool.Clear(); }
		}

		/// <summary>Lets plain IRemote test doubles be used where IRemoteFs is needed.</summary>
		class Adapter : IRemoteFs
		{
			readonly IRemote r;
			public Adapter(IRemote r) { this.r = r; }
			public byte[] Download(string p) { return r.Download(p); }
			public void Upload(string p, byte[] d) { r.Upload(p, d); }
			public List<RemoteEntry> List(string d) { throw new NotSupportedException(); }
			public void MakeDir(string p) { throw new NotSupportedException(); }
			public void Delete(string p, bool d) { throw new NotSupportedException(); }
			public void Rename(string a, string b) { throw new NotSupportedException(); }
			public string GetHome() { return "/"; }
			public byte[] Download(string p, ProgressFn f) { return r.Download(p); }
			public void Upload(string p, byte[] d, ProgressFn f) { r.Upload(p, d); }
			public void Dispose() { r.Dispose(); }
		}
	}

	public static class RemotePath
	{
		public static string Combine(string dir, string name)
		{
			return (dir.TrimEnd('/') + "/" + name.TrimStart('/'));
		}
		public static string Parent(string path)
		{
			string p = path.TrimEnd('/');
			int i = p.LastIndexOf('/');
			return i <= 0 ? "/" : p.Substring(0, i);
		}
		public static string Name(string path)
		{
			string p = path.TrimEnd('/');
			return p.Substring(p.LastIndexOf('/') + 1);
		}
	}

	public static class RemoteOps
	{
		public static void DeleteRecursive(IRemoteFs fs, string path, Action<string> beforeDeleteFile)
		{
			foreach (RemoteEntry e in fs.List(path))
			{
				if (e.IsDir && !e.IsLink) DeleteRecursive(fs, e.Path, beforeDeleteFile);
				else { if (beforeDeleteFile != null) beforeDeleteFile(e.Path); fs.Delete(e.Path, false); }
			}
			fs.Delete(path, true);
		}

		/// <summary>All files below dir (depth-first).</summary>
		public static void Walk(IRemoteFs fs, string dir, Action<RemoteEntry> onFile)
		{
			foreach (RemoteEntry e in fs.List(dir))
			{
				if (e.IsDir && !e.IsLink) Walk(fs, e.Path, onFile);
				else if (!e.IsDir) onFile(e);
			}
		}
	}

	public class FtpRemote : IRemoteFs
	{
		readonly Profile p; readonly int timeout;
		public FtpRemote(Profile p, int timeoutMs) { this.p = p; timeout = timeoutMs; }

		Uri MakeUri(string path)
		{
			string[] parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < parts.Length; i++) parts[i] = Uri.EscapeDataString(parts[i]);
			return new Uri("ftp://" + p.Host + ":" + p.Port + "/%2F" + string.Join("/", parts));
		}

		FtpWebRequest Req(string path, string method)
		{
			if (p.Protocol == Protocol.Ftps) throw new NotSupportedException(L.T("Неявный FTPS (порт 990) не поддерживается: используйте явный FTPS (FTPES) или SFTP."));
			FtpWebRequest r = (FtpWebRequest)WebRequest.Create(MakeUri(path));
			r.Method = method;
			r.Credentials = new NetworkCredential(p.User, RemoteFactory.PasswordFor(p));
			r.UsePassive = p.Passive; r.UseBinary = true; r.KeepAlive = false;
			r.EnableSsl = p.Protocol == Protocol.Ftpes;
			r.Timeout = timeout; r.ReadWriteTimeout = timeout;
			return r;
		}

		public byte[] Download(string remotePath) { return Download(remotePath, null); }

		public byte[] Download(string remotePath, ProgressFn progress)
		{
			try
			{
				using (FtpWebResponse resp = (FtpWebResponse)Req(remotePath, WebRequestMethods.Ftp.DownloadFile).GetResponse())
				using (Stream s = resp.GetResponseStream())
				using (MemoryStream ms = new MemoryStream())
				{
					long total = resp.ContentLength;
					byte[] buf = new byte[32768]; int n;
					while ((n = s.Read(buf, 0, buf.Length)) > 0)
					{
						ms.Write(buf, 0, n);
						if (progress != null) progress(ms.Length, total);
					}
					return ms.ToArray();
				}
			}
			catch (WebException ex)
			{
				FtpWebResponse fr = ex.Response as FtpWebResponse;
				if (fr != null && fr.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable) return null;
				throw;
			}
		}

		public void Upload(string remotePath, byte[] data) { Upload(remotePath, data, null); }

		public void Upload(string remotePath, byte[] data, ProgressFn progress)
		{
			FtpWebRequest r = Req(remotePath, WebRequestMethods.Ftp.UploadFile);
			r.ContentLength = data.Length;
			using (Stream s = r.GetRequestStream())
			{
				int off = 0;
				while (off < data.Length)
				{
					int n = Math.Min(32768, data.Length - off);
					s.Write(data, off, n); off += n;
					if (progress != null) progress(off, data.Length);
				}
			}
			using (r.GetResponse()) { }
		}

		public List<RemoteEntry> List(string dir)
		{
			string text;
			using (FtpWebResponse resp = (FtpWebResponse)Req(dir.TrimEnd('/') + "/", WebRequestMethods.Ftp.ListDirectoryDetails).GetResponse())
			using (StreamReader sr = new StreamReader(resp.GetResponseStream(), System.Text.Encoding.UTF8))
				text = sr.ReadToEnd();
			return FtpListParser.Parse(text, dir);
		}

		public string GetHome()
		{
			if (p.Protocol == Protocol.Ftps) throw new NotSupportedException(L.T("Неявный FTPS (порт 990) не поддерживается: используйте явный FTPS (FTPES) или SFTP."));
			FtpWebRequest r = (FtpWebRequest)WebRequest.Create(new Uri("ftp://" + p.Host + ":" + p.Port + "/"));
			r.Method = WebRequestMethods.Ftp.PrintWorkingDirectory;
			r.Credentials = new NetworkCredential(p.User, RemoteFactory.PasswordFor(p));
			r.UsePassive = p.Passive; r.KeepAlive = false; r.EnableSsl = p.Protocol == Protocol.Ftpes;
			r.Timeout = timeout; r.ReadWriteTimeout = timeout;
			using (FtpWebResponse resp = (FtpWebResponse)r.GetResponse())
				return ParsePwd(resp.StatusDescription);
		}

		public static string ParsePwd(string status)
		{
			Match m = Regex.Match(status ?? "", "\"((?:[^\"]|\"\")*)\"");
			return m.Success ? m.Groups[1].Value.Replace("\"\"", "\"") : "/";
		}

		public void MakeDir(string path) { using (Req(path, WebRequestMethods.Ftp.MakeDirectory).GetResponse()) { } }

		public void Delete(string path, bool isDir)
		{
			using (Req(path, isDir ? WebRequestMethods.Ftp.RemoveDirectory : WebRequestMethods.Ftp.DeleteFile).GetResponse()) { }
		}

		public void Rename(string from, string to)
		{
			FtpWebRequest r = Req(from, WebRequestMethods.Ftp.Rename);
			r.RenameTo = to;
			using (r.GetResponse()) { }
		}

		public void Dispose() { }
	}

	/// <summary>Parses Unix "ls -l" and DOS style FTP listings.</summary>
	public static class FtpListParser
	{
		static readonly Regex Unix = new Regex(@"^([\-dlbcps][rwxsStT\-]{9})[+@.]?\s+\d+\s+\S+\s+\S+\s+(\d+)\s+(\w{3}\s+\d{1,2}\s+(?:\d{4}|\d{1,2}:\d{2}))\s(.+)$", RegexOptions.Compiled);
		static readonly Regex UnixNoGroup = new Regex(@"^([\-dlbcps][rwxsStT\-]{9})[+@.]?\s+\d+\s+\S+\s+(\d+)\s+(\w{3}\s+\d{1,2}\s+(?:\d{4}|\d{1,2}:\d{2}))\s(.+)$", RegexOptions.Compiled);
		static readonly Regex Dos = new Regex(@"^(\d{2}-\d{2}-\d{2,4})\s+(\d{1,2}:\d{2}\s*[AP]?M?)\s+(<DIR>|\d+)\s+(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		public static List<RemoteEntry> Parse(string text, string dir)
		{
			List<RemoteEntry> r = new List<RemoteEntry>();
			foreach (string raw in text.Split('\n'))
			{
				string line = raw.TrimEnd('\r');
				if (line.Length == 0 || line.StartsWith("total ")) continue;
				RemoteEntry e = null;
				Match m = Unix.Match(line);
				if (!m.Success) m = UnixNoGroup.Match(line);
				if (m.Success)
				{
					string name = m.Groups[4].Value;
					e = new RemoteEntry { Perms = m.Groups[1].Value.Substring(1), Size = long.Parse(m.Groups[2].Value) };
					char t = m.Groups[1].Value[0];
					e.IsDir = t == 'd'; e.IsLink = t == 'l';
					if (e.IsLink) { int i = name.IndexOf(" -> "); if (i > 0) name = name.Substring(0, i); }
					e.Name = name;
					e.Modified = ParseUnixDate(m.Groups[3].Value);
				}
				else
				{
					m = Dos.Match(line);
					if (m.Success)
					{
						e = new RemoteEntry { Name = m.Groups[4].Value, IsDir = m.Groups[3].Value == "<DIR>" };
						if (!e.IsDir) e.Size = long.Parse(m.Groups[3].Value);
						DateTime d;
						if (DateTime.TryParse(m.Groups[1].Value + " " + m.Groups[2].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) e.Modified = d;
					}
				}
				if (e == null || e.Name == "." || e.Name == "..") continue;
				e.Path = RemotePath.Combine(dir, e.Name);
				r.Add(e);
			}
			return r;
		}

		static DateTime? ParseUnixDate(string s)
		{
			string[] f = Regex.Split(s.Trim(), @"\s+");
			if (f.Length != 3) return null;
			string[] months = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
			int mo = Array.IndexOf(months, f[0]) + 1;
			int day;
			if (mo == 0 || !int.TryParse(f[1], out day)) return null;
			try
			{
				if (f[2].Contains(":"))
				{
					string[] hm = f[2].Split(':');
					DateTime d = new DateTime(DateTime.Now.Year, mo, day, int.Parse(hm[0]), int.Parse(hm[1]), 0);
					return d > DateTime.Now.AddDays(1) ? d.AddYears(-1) : d;
				}
				return new DateTime(int.Parse(f[2]), mo, day);
			}
			catch (ArgumentException) { return null; }
		}
	}

	public class SftpRemote
	{
		readonly Profile p; readonly int timeout;
		SftpClient c;
		public readonly object Gate = new object();

		public SftpRemote(Profile p, int timeoutMs) { this.p = p; timeout = timeoutMs; }

		public SftpClient Client()
		{
			if (c != null && c.IsConnected) return c;
			Close();
			AuthenticationMethod auth;
			string pw = RemoteFactory.PasswordFor(p);
			if (!string.IsNullOrEmpty(p.KeyFile) && File.Exists(p.KeyFile))
			{
				PrivateKeyFile key = string.IsNullOrEmpty(pw) ? new PrivateKeyFile(p.KeyFile) : new PrivateKeyFile(p.KeyFile, pw);
				auth = new PrivateKeyAuthenticationMethod(p.User, key);
			}
			else auth = new PasswordAuthenticationMethod(p.User, pw);
			ConnectionInfo ci = new ConnectionInfo(p.Host, p.Port, p.User, auth);
			ci.Timeout = TimeSpan.FromMilliseconds(timeout);
			c = new SftpClient(ci);
			c.KeepAliveInterval = TimeSpan.FromSeconds(30);
			c.Connect();
			return c;
		}

		public void Close()
		{
			if (c == null) return;
			try { if (c.IsConnected) c.Disconnect(); } catch (Exception) { }
			try { c.Dispose(); } catch (Exception) { }
			c = null;
		}
	}

	/// <summary>Short-lived handle over a pooled SFTP connection; Dispose keeps the connection open.</summary>
	public class SftpLease : IRemoteFs
	{
		readonly SftpRemote s;
		public SftpLease(SftpRemote s) { this.s = s; }

		T Do<T>(Func<SftpClient, T> f)
		{
			lock (s.Gate)
			{
				try { return f(s.Client()); }
				catch (Renci.SshNet.Common.SshConnectionException) { s.Close(); return f(s.Client()); }
				catch (System.Net.Sockets.SocketException) { s.Close(); return f(s.Client()); }
			}
		}

		public byte[] Download(string p) { return Download(p, null); }

		public byte[] Download(string p, ProgressFn progress)
		{
			return Do<byte[]>(c =>
			{
				if (!c.Exists(p)) return null;
				using (MemoryStream ms = new MemoryStream())
				{
					c.DownloadFile(p, ms, n => { if (progress != null) progress((long)n, -1); });
					return ms.ToArray();
				}
			});
		}

		public void Upload(string p, byte[] d) { Upload(p, d, null); }

		public void Upload(string p, byte[] d, ProgressFn progress)
		{
			Do<bool>(c =>
			{
				using (MemoryStream ms = new MemoryStream(d)) c.UploadFile(ms, p, true, n => { if (progress != null) progress((long)n, d.Length); });
				return true;
			});
		}

		public List<RemoteEntry> List(string dir)
		{
			return Do<List<RemoteEntry>>(c =>
			{
				List<RemoteEntry> r = new List<RemoteEntry>();
				foreach (SftpFile f in c.ListDirectory(dir))
				{
					if (f.Name == "." || f.Name == "..") continue;
					RemoteEntry e = new RemoteEntry { Name = f.Name, Path = RemotePath.Combine(dir, f.Name), IsDir = f.IsDirectory, IsLink = f.IsSymbolicLink, Size = f.Length, Modified = f.LastWriteTime };
					if (f.IsSymbolicLink)
					{
						try { e.IsDir = c.GetAttributes(e.Path).IsDirectory; } catch (Exception) { }
					}
					e.Perms = Perm(f);
					r.Add(e);
				}
				return r;
			});
		}

		static string Perm(SftpFile f)
		{
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			sb.Append(f.OwnerCanRead ? 'r' : '-').Append(f.OwnerCanWrite ? 'w' : '-').Append(f.OwnerCanExecute ? 'x' : '-');
			sb.Append(f.GroupCanRead ? 'r' : '-').Append(f.GroupCanWrite ? 'w' : '-').Append(f.GroupCanExecute ? 'x' : '-');
			sb.Append(f.OthersCanRead ? 'r' : '-').Append(f.OthersCanWrite ? 'w' : '-').Append(f.OthersCanExecute ? 'x' : '-');
			return sb.ToString();
		}

		public string GetHome() { return Do<string>(c => c.WorkingDirectory); }
		public void MakeDir(string p) { Do<bool>(c => { c.CreateDirectory(p); return true; }); }
		public void Delete(string p, bool isDir) { Do<bool>(c => { if (isDir) c.DeleteDirectory(p); else c.DeleteFile(p); return true; }); }
		public void Rename(string a, string b) { Do<bool>(c => { c.RenameFile(a, b); return true; }); }
		public void Dispose() { }
	}
}
