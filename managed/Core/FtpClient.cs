using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace FtpSync
{
	public class FtpException : IOException
	{
		public readonly int Code;
		public readonly string Reply;
		public FtpException(int code, string reply, string message) : base(message) { Code = code; Reply = reply; }
	}

	/// <summary>Small FTP / FTPES client on plain sockets: lenient reply parsing, EPSV / PASV / PORT, UTF-8, no system proxy.</summary>
	public class FtpClient : IDisposable
	{
		public struct Reply { public int Code; public string Text; }

		readonly Profile p;
		readonly int timeout;
		TcpClient tcp;
		Stream ctl;
		bool tls;
		readonly byte[] buf = new byte[4096];
		int bufPos, bufLen;
		static readonly Encoding Utf8 = new UTF8Encoding(false);

		public FtpClient(Profile profile, int timeoutMs) { p = profile; timeout = timeoutMs; }

		public bool Connected { get { return tcp != null && tcp.Connected; } }

		static string AsciiHost(string host)
		{
			foreach (char c in host) if (c > 127) { try { return new IdnMapping().GetAscii(host); } catch (ArgumentException) { } break; }
			return host;
		}

		// ------------------------------------------------------------------ control connection
		string ReadLine()
		{
			MemoryStream line = new MemoryStream();
			while (true)
			{
				if (bufPos >= bufLen)
				{
					bufLen = ctl.Read(buf, 0, buf.Length); bufPos = 0;
					if (bufLen <= 0) throw new IOException("connection closed by the server");
				}
				byte b = buf[bufPos++];
				if (b == (byte)'\n') break;
				if (b != (byte)'\r') line.WriteByte(b);
			}
			return Utf8.GetString(line.ToArray());
		}

		static bool StartsWithCode(string s, out int code)
		{
			code = 0;
			return s.Length >= 3 && int.TryParse(s.Substring(0, 3), NumberStyles.None, CultureInfo.InvariantCulture, out code);
		}

		public Reply ReadReply()
		{
			string first = ReadLine();
			int code;
			while (!StartsWithCode(first, out code)) first = ReadLine(); // stray text before a reply
			StringBuilder text = new StringBuilder(first);
			if (first.Length > 3 && first[3] == '-')
			{
				while (true)
				{
					string l = ReadLine();
					text.Append('\n').Append(l);
					int c2;
					if (StartsWithCode(l, out c2) && c2 == code && (l.Length == 3 || l[3] == ' ')) break;
				}
			}
			return new Reply { Code = code, Text = text.ToString() };
		}

		void Send(string cmd)
		{
			byte[] b = Utf8.GetBytes(cmd + "\r\n");
			ctl.Write(b, 0, b.Length); ctl.Flush();
		}

		public Reply Cmd(string cmd) { Send(cmd); return ReadReply(); }

		static string Verb(string cmd) { int i = cmd.IndexOf(' '); return i < 0 ? cmd : cmd.Substring(0, i); }

		Reply Expect(string cmd, params int[] classes)
		{
			Reply r = Cmd(cmd);
			Check(r, cmd, classes);
			return r;
		}

		static void Check(Reply r, string cmd, params int[] classes)
		{
			foreach (int c in classes) if (r.Code / 100 == c) return;
			throw new FtpException(r.Code, r.Text, Verb(cmd) + ": " + r.Text.Replace("\n", " "));
		}

		static bool Validate(object s, System.Security.Cryptography.X509Certificates.X509Certificate c, System.Security.Cryptography.X509Certificates.X509Chain ch, SslPolicyErrors e) { return e == SslPolicyErrors.None; }

		public void Connect()
		{
			Close();
			string host = AsciiHost(p.Host);
			tcp = new TcpClient();
			IAsyncResult ar = tcp.BeginConnect(host, p.Port, null, null);
			if (!ar.AsyncWaitHandle.WaitOne(Math.Max(5000, timeout))) { try { tcp.Close(); } catch (Exception) { } throw new TimeoutException("timeout connecting to " + p.Host + ":" + p.Port); }
			tcp.EndConnect(ar);
			tcp.ReceiveTimeout = timeout; tcp.SendTimeout = timeout; tcp.NoDelay = true;
			ctl = tcp.GetStream(); bufPos = bufLen = 0; tls = false;
			Reply hello = ReadReply();
			Check(hello, "connect", 2);
			if (p.Protocol == Protocol.Ftpes)
			{
				Reply a = Cmd("AUTH TLS");
				if (a.Code / 100 != 2) a = Cmd("AUTH SSL");
				Check(a, "AUTH", 2);
				SslStream ssl = new SslStream(ctl, false, Validate);
				ssl.AuthenticateAsClient(host);
				ctl = ssl; bufPos = bufLen = 0; tls = true;
				Cmd("PBSZ 0");
				Cmd("PROT P");
			}
			Reply u = Cmd("USER " + p.User);
			if (u.Code == 331 || u.Code == 332) u = Cmd("PASS " + RemoteFactory.PasswordFor(p));
			if (u.Code != 230 && u.Code != 202) throw new FtpException(u.Code, u.Text, "LOGIN: " + u.Text.Replace("\n", " "));
			Cmd("OPTS UTF8 ON");
			Reply t = Cmd("TYPE I");
			Check(t, "TYPE", 2);
		}

		public void Close()
		{
			try { if (ctl != null && tcp != null && tcp.Connected) { Send("QUIT"); } } catch (Exception) { }
			try { if (ctl != null) ctl.Close(); } catch (Exception) { }
			try { if (tcp != null) tcp.Close(); } catch (Exception) { }
			ctl = null; tcp = null;
		}

		public void Dispose() { Close(); }

		// ------------------------------------------------------------------ data connection
		static bool IsPrivate(IPAddress a)
		{
			byte[] b = a.GetAddressBytes();
			if (b.Length != 4) return false;
			return b[0] == 10 || b[0] == 127 || b[0] == 0 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
		}

		IPAddress ControlRemote { get { return ((IPEndPoint)tcp.Client.RemoteEndPoint).Address; } }

		TcpClient ConnectData(IPAddress ip, int port)
		{
			TcpClient d = new TcpClient(ip.AddressFamily);
			IAsyncResult ar = d.BeginConnect(ip, port, null, null);
			if (!ar.AsyncWaitHandle.WaitOne(Math.Max(5000, timeout))) { try { d.Close(); } catch (Exception) { } throw new TimeoutException("timeout opening the data connection to " + ip + ":" + port); }
			d.EndConnect(ar);
			d.ReceiveTimeout = timeout; d.SendTimeout = timeout;
			return d;
		}

		/// <summary>Opens a data channel, sends the command, and returns the stream once the server agreed (1xx). Null with the reply when it refused.</summary>
		Stream OpenData(string command, out Reply refusal, out TcpClient data)
		{
			refusal = default(Reply); data = null;
			Stream result;
			if (p.Passive)
			{
				int port = 0; IPAddress ip = ControlRemote;
				Reply e = Cmd("EPSV");
				Match m = Regex.Match(e.Text, @"\(\W\W\W(\d+)\W\)");
				if (e.Code == 229 && m.Success) port = int.Parse(m.Groups[1].Value);
				else
				{
					Reply r = Cmd("PASV");
					Check(r, "PASV", 2);
					Match n = Regex.Match(r.Text, @"(\d+),(\d+),(\d+),(\d+),(\d+),(\d+)");
					if (!n.Success) throw new FtpException(r.Code, r.Text, "PASV: " + r.Text.Replace("\n", " "));
					IPAddress sip = IPAddress.Parse(n.Groups[1].Value + "." + n.Groups[2].Value + "." + n.Groups[3].Value + "." + n.Groups[4].Value);
					if (!IsPrivate(sip)) ip = sip;
					port = int.Parse(n.Groups[5].Value) * 256 + int.Parse(n.Groups[6].Value);
				}
				data = ConnectData(ip, port);
				Reply c = Cmd(command);
				if (c.Code / 100 != 1) { refusal = c; data.Close(); data = null; return null; }
				result = data.GetStream();
			}
			else
			{
				IPAddress local = ((IPEndPoint)tcp.Client.LocalEndPoint).Address;
				TcpListener l = new TcpListener(local, 0);
				l.Start();
				try
				{
					int port = ((IPEndPoint)l.LocalEndpoint).Port;
					byte[] ab = local.GetAddressBytes();
					Reply pr = Cmd("PORT " + ab[0] + "," + ab[1] + "," + ab[2] + "," + ab[3] + "," + (port / 256) + "," + (port % 256));
					Check(pr, "PORT", 2);
					Reply c = Cmd(command);
					if (c.Code / 100 != 1) { refusal = c; return null; }
					IAsyncResult ar = l.BeginAcceptTcpClient(null, null);
					if (!ar.AsyncWaitHandle.WaitOne(Math.Max(5000, timeout))) throw new TimeoutException("the server did not open the data connection (active mode)");
					data = l.EndAcceptTcpClient(ar);
					data.ReceiveTimeout = timeout; data.SendTimeout = timeout;
					result = data.GetStream();
				}
				finally { l.Stop(); }
			}
			if (tls)
			{
				SslStream ssl = new SslStream(result, false, Validate);
				ssl.AuthenticateAsClient(AsciiHost(p.Host));
				result = ssl;
			}
			return result;
		}

		void Finish(Stream s, TcpClient d)
		{
			try { if (s != null) s.Close(); } catch (Exception) { }
			try { if (d != null) d.Close(); } catch (Exception) { }
			Reply r = ReadReply();
			Check(r, "transfer", 2);
		}

		// ------------------------------------------------------------------ operations
		public string Pwd()
		{
			Reply r = Expect("PWD", 2);
			return FtpRemote.ParsePwd(r.Text);
		}

		public string List(string dir)
		{
			string[] cmds = { "LIST -a " + dir, "LIST " + dir };
			Reply bad = default(Reply);
			foreach (string c in cmds)
			{
				Reply refusal; TcpClient d;
				Stream s = OpenData(c, out refusal, out d);
				if (s == null) { bad = refusal; continue; }
				string text;
				using (StreamReader sr = new StreamReader(s, Utf8)) text = sr.ReadToEnd();
				Finish(s, d);
				return text;
			}
			throw new FtpException(bad.Code, bad.Text, "LIST: " + bad.Text.Replace("\n", " "));
		}

		public byte[] Download(string path, ProgressFn progress)
		{
			long total = -1;
			Reply sz = Cmd("SIZE " + path);
			if (sz.Code == 213) { long v; if (long.TryParse(sz.Text.Substring(4).Trim(), out v)) total = v; }
			Reply refusal; TcpClient d;
			Stream s = OpenData("RETR " + path, out refusal, out d);
			if (s == null)
			{
				if (refusal.Code == 550 || refusal.Code == 450) return null;
				throw new FtpException(refusal.Code, refusal.Text, "RETR: " + refusal.Text.Replace("\n", " "));
			}
			MemoryStream ms = new MemoryStream();
			byte[] b = new byte[32768]; int n;
			while ((n = s.Read(b, 0, b.Length)) > 0) { ms.Write(b, 0, n); if (progress != null) progress(ms.Length, total); }
			Finish(s, d);
			return ms.ToArray();
		}

		public void Upload(string path, byte[] data, ProgressFn progress)
		{
			Reply refusal; TcpClient d;
			Stream s = OpenData("STOR " + path, out refusal, out d);
			if (s == null) throw new FtpException(refusal.Code, refusal.Text, "STOR: " + refusal.Text.Replace("\n", " "));
			int off = 0;
			while (off < data.Length)
			{
				int n = Math.Min(32768, data.Length - off);
				s.Write(data, off, n); off += n;
				if (progress != null) progress(off, data.Length);
			}
			s.Flush();
			Finish(s, d);
		}

		public void MakeDir(string path) { Expect("MKD " + path, 2); }
		public void RemoveDir(string path) { Expect("RMD " + path, 2); }
		public void DeleteFile(string path) { Expect("DELE " + path, 2); }
		public void Rename(string from, string to) { Expect("RNFR " + from, 3); Expect("RNTO " + to, 2); }
	}
}
