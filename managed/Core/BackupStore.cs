using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FtpSync
{
	public class BackupVersion
	{
		public string Path;
		public DateTime Time;
		public string Reason;
		public long Size;
		public DateTime Written;
	}

	/// <summary>
	/// Backups mirror the remote tree: Root\profile\dir\sub\file.php\20261007-153012_remote.php
	/// </summary>
	public class BackupStore
	{
		static readonly Regex VersionRx = new Regex(@"^(\d{8}-\d{6})_([a-z0-9\-]+)(\..*)?$", RegexOptions.Compiled);
		public string Root;
		public int MaxVersions = 50;
		public int KeepDays = 180;

		public BackupStore(string root) { Root = root; }

		public static string SafeSegment(string s)
		{
			StringBuilder sb = new StringBuilder();
			foreach (char ch in s) sb.Append(Array.IndexOf(System.IO.Path.GetInvalidFileNameChars(), ch) >= 0 || ch == ':' ? '_' : ch);
			string r = sb.ToString().Trim().TrimEnd('.');
			if (r.Length == 0 || r == "." || r == "..") r = "_";
			return r;
		}

		public string FileDir(string profile, string remotePath)
		{
			string d = System.IO.Path.Combine(Root, SafeSegment(profile));
			foreach (string seg in remotePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
				d = System.IO.Path.Combine(d, SafeSegment(seg));
			return d;
		}

		public static bool IsVersionFile(string name) { return VersionRx.IsMatch(name); }

		public static BackupVersion Describe(string path)
		{
			Match m = VersionRx.Match(System.IO.Path.GetFileName(path));
			if (!m.Success) return null;
			DateTime t;
			if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd-HHmmss", null, System.Globalization.DateTimeStyles.None, out t)) t = File.GetLastWriteTime(path);
			return new BackupVersion { Path = path, Time = t, Reason = m.Groups[2].Value, Size = new FileInfo(path).Length, Written = File.GetLastWriteTimeUtc(path) };
		}

		public List<BackupVersion> Versions(string profile, string remotePath)
		{
			return VersionsIn(FileDir(profile, remotePath));
		}

		public static List<BackupVersion> VersionsIn(string dir)
		{
			List<BackupVersion> r = new List<BackupVersion>();
			if (!Directory.Exists(dir)) return r;
			foreach (string f in Directory.GetFiles(dir))
			{
				BackupVersion v = Describe(f);
				if (v != null) r.Add(v);
			}
			r.Sort((a, b) => { int c = b.Time.CompareTo(a.Time); if (c == 0) c = b.Written.CompareTo(a.Written); if (c == 0) c = string.CompareOrdinal(b.Path, a.Path); return c; });
			return r;
		}

		/// <summary>Stores a version. Returns the path of the new version, or of the identical latest one.</summary>
		public string Save(string profile, string remotePath, byte[] data, string reason, out bool created)
		{
			created = false;
			string dir = FileDir(profile, remotePath);
			Directory.CreateDirectory(dir);
			List<BackupVersion> vs = VersionsIn(dir);
			if (vs.Count > 0)
			{
				try
				{
					byte[] last = File.ReadAllBytes(vs[0].Path);
					if (TextUtil.Sig(last) == TextUtil.Sig(data)) return vs[0].Path;
				}
				catch (IOException) { }
			}
			string name = System.IO.Path.GetFileName(remotePath.TrimEnd('/', '\\'));
			string ext = System.IO.Path.GetExtension(name);
			string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
			string path = System.IO.Path.Combine(dir, stamp + "_" + reason + ext);
			int n = 1;
			while (File.Exists(path)) path = System.IO.Path.Combine(dir, stamp + "_" + reason + "-" + (n++) + ext);
			File.WriteAllBytes(path, data);
			created = true;
			Prune(dir);
			return path;
		}

		/// <summary>
		/// Deletes versions under dir (a profile, a folder or one file's folder): everything but the newest keepLast of each file,
		/// and, when olderThanDays > 0, only those older than that. Empty folders are removed.
		/// </summary>
		public void Clear(string dir, int olderThanDays, int keepLast, out int files, out long bytes)
		{
			files = 0; bytes = 0;
			if (!Directory.Exists(dir)) return;
			DateTime limit = DateTime.Now.AddDays(-olderThanDays);
			ClearDir(dir, olderThanDays, limit, keepLast, ref files, ref bytes);
		}

		void ClearDir(string dir, int olderThanDays, DateTime limit, int keepLast, ref int files, ref long bytes)
		{
			string[] subs;
			try { subs = Directory.GetDirectories(dir); } catch (IOException) { return; }
			foreach (string sd in subs)
			{
				ClearDir(sd, olderThanDays, limit, keepLast, ref files, ref bytes);
				try { if (Directory.GetFileSystemEntries(sd).Length == 0) Directory.Delete(sd); } catch (IOException) { }
			}
			List<BackupVersion> vs = VersionsIn(dir);
			for (int i = keepLast; i < vs.Count; i++)
			{
				if (olderThanDays > 0 && vs[i].Time >= limit) continue;
				try { File.Delete(vs[i].Path); files++; bytes += vs[i].Size; } catch (IOException) { }
			}
		}

		public void Prune(string dir)
		{
			List<BackupVersion> vs = VersionsIn(dir);
			DateTime limit = DateTime.Now.AddDays(-KeepDays);
			for (int i = 0; i < vs.Count; i++)
			{
				if (i < 3) continue;
				if (i >= MaxVersions || (KeepDays > 0 && vs[i].Time < limit))
					try { File.Delete(vs[i].Path); } catch (IOException) { }
			}
		}
	}
}
