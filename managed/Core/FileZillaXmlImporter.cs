using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace FtpSync
{
	/// <summary>
	/// Imports the FileZilla Site Manager from its XML (sitemanager.xml or File - Export): folders (as groups), servers with
	/// protocol, port, login, password (base64), key file, passive mode, comment, local/remote folders and bookmarks.
	/// </summary>
	public static class FileZillaXmlImporter
	{
		public static string DefaultFile()
		{
			string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			string p = Path.Combine(Path.Combine(appdata, "FileZilla"), "sitemanager.xml");
			return File.Exists(p) ? p : "";
		}

		public static NppFtpImporter.Result Import(string xmlFile)
		{
			NppFtpImporter.Result r = new NppFtpImporter.Result();
			XmlDocument doc = new XmlDocument();
			doc.Load(xmlFile);
			XmlNode root = doc.SelectSingleNode("//Servers") ?? doc.DocumentElement;
			Walk(root, "", r);
			if (r.Profiles.Count == 0) r.Warnings.Add(L.F("В {0} не найдено ни одного сервера.", xmlFile));
			return r;
		}

		static void Walk(XmlNode parent, string group, NppFtpImporter.Result r)
		{
			foreach (XmlNode n in parent.ChildNodes)
			{
				XmlElement e = n as XmlElement;
				if (e == null) continue;
				if (e.Name == "Folder")
				{
					string name = FolderName(e);
					Walk(e, string.IsNullOrEmpty(group) ? name : (string.IsNullOrEmpty(name) ? group : group + "/" + name), r);
				}
				else if (e.Name == "Server") ImportServer(e, group, r);
			}
		}

		/// <summary>The folder name is the text that stands before the nested elements.</summary>
		static string FolderName(XmlElement e)
		{
			StringBuilder sb = new StringBuilder();
			foreach (XmlNode c in e.ChildNodes) if (c is XmlText || c is XmlCDataSection) sb.Append(c.Value);
			string s = sb.ToString().Trim();
			if (e.GetAttribute("encoding") == "base64") s = B64(s);
			if (s.Length == 0) { XmlElement ne = e["Name"]; if (ne != null) s = Text(ne); }
			return s.Replace('/', '-');
		}

		static string B64(string s)
		{
			try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); } catch (FormatException) { return s; }
		}

		static string Text(XmlElement e)
		{
			if (e == null) return "";
			string s = e.InnerText.Trim();
			return e.GetAttribute("encoding") == "base64" ? B64(s) : s;
		}

		static string T(XmlElement parent, string name) { return Text(parent[name]); }

		static void ImportServer(XmlElement s, string group, NppFtpImporter.Result r)
		{
			string host = T(s, "Host");
			string name = T(s, "Name");
			if (host.Length == 0) return;
			int proto; int.TryParse(T(s, "Protocol"), out proto);
			string label = name.Length > 0 ? name : host;
			if (proto == 2 || proto == 5) { r.Warnings.Add(L.F("{0}: сайт HTTP/HTTPS пропущен.", label)); return; }

			int idx = host.IndexOf("://", StringComparison.Ordinal);
			if (idx > 0) host = host.Substring(idx + 3);

			Profile p = new Profile { Name = label, Group = group, Host = host, User = T(s, "User"), Comment = T(s, "Comments") };
			int port; p.Port = int.TryParse(T(s, "Port"), out port) && port > 0 ? port : 0;
			switch (proto)
			{
				case 1: p.Protocol = Protocol.Sftp; if (p.Port == 0) p.Port = 22; break;
				case 3: p.Protocol = Protocol.Ftps; if (p.Port == 0) p.Port = 990; break;
				case 4: p.Protocol = Protocol.Ftpes; break;
				default: p.Protocol = Protocol.Ftp; break;
			}
			if (p.Port == 0) p.Port = 21;
			p.Passive = !string.Equals(T(s, "PasvMode"), "MODE_ACTIVE", StringComparison.OrdinalIgnoreCase);
			p.KeyFile = T(s, "Keyfile");

			int logon; int.TryParse(T(s, "Logontype"), out logon);
			if (logon == 0 && p.User.Length == 0) p.User = "anonymous";
			XmlElement pw = s["Pass"];
			if (pw != null && logon != 2 && logon != 3)
			{
				string enc = pw.GetAttribute("encoding");
				if (enc == "crypt") r.Warnings.Add(L.F("{0}: пароль защищён мастер-паролем FileZilla и не может быть импортирован, введите его вручную.", label));
				else if (pw.InnerText.Length > 0) p.Password = Text(pw);
			}

			string remote = ServerPath(T(s, "RemoteDir")), local = T(s, "LocalDir");
			if (remote != "/") p.InitialDir = remote;
			if (local.Length > 0) p.Maps.Add(new PathMap(local, "/"));

			foreach (XmlNode bn in s.ChildNodes)
			{
				XmlElement b = bn as XmlElement;
				if (b == null || b.Name != "Bookmark") continue;
				string bl = T(b, "LocalDir"), br = ServerPath(T(b, "RemoteDir")), bname = T(b, "Name");
				p.Bookmarks.Add(new Bookmark(bname.Length > 0 ? bname : (br != "/" ? br : bl), bl, br));
				if (bl.Length > 0 && br != "/" && !p.Maps.Exists(m => string.Equals(m.Local, bl, StringComparison.OrdinalIgnoreCase)))
					p.Maps.Add(new PathMap(bl, br));
			}
			r.Profiles.Add(p);
		}

		/// <summary>
		/// FileZilla stores server paths as "type prefixLength [prefix] len segment len segment...", e.g. "1 0 4 html 3 www" = /html/www.
		/// A plain path ("/html/www") is accepted too.
		/// </summary>
		public static string ServerPath(string safe)
		{
			safe = (safe ?? "").Trim();
			if (safe.Length == 0) return "/";
			if (safe[0] == '/') return safe;
			try
			{
				int i = 0;
				ReadInt(safe, ref i);                 // server type
				int prefixLen = ReadInt(safe, ref i); // prefix length (drive letter and the like)
				if (prefixLen > 0) i += prefixLen + 1;
				StringBuilder sb = new StringBuilder();
				while (i < safe.Length)
				{
					int len = ReadInt(safe, ref i);
					if (len < 0 || i + len > safe.Length) break;
					sb.Append('/').Append(safe, i, len);
					i += len + 1;
				}
				return sb.Length == 0 ? "/" : sb.ToString();
			}
			catch (Exception) { return "/"; }
		}

		static int ReadInt(string s, ref int i)
		{
			int start = i;
			while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
			if (i == start) return -1;
			int v = int.Parse(s.Substring(start, i - start));
			if (i < s.Length && s[i] == ' ') i++;
			return v;
		}
	}
}
