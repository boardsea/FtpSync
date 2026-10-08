using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace FtpSync
{
	/// <summary>Reads NppFTP.xml. The format is parsed leniently (attribute names matched case-insensitively).</summary>
	public static class NppFtpImporter
	{
		public class Result
		{
			public List<Profile> Profiles = new List<Profile>();
			public List<string> Warnings = new List<string>();
		}

		public static string DefaultFile(string pluginsConfigDir)
		{
			string a = Path.Combine(Path.Combine(pluginsConfigDir, "NppFTP"), "NppFTP.xml");
			if (File.Exists(a)) return a;
			string b = Path.Combine(pluginsConfigDir, "NppFTP.xml");
			return File.Exists(b) ? b : a;
		}

		static Dictionary<string, string> Attrs(XmlElement e)
		{
			Dictionary<string, string> d = new Dictionary<string, string>();
			foreach (XmlAttribute a in e.Attributes) d[a.Name.ToLowerInvariant()] = a.Value;
			foreach (XmlNode c in e.ChildNodes)
			{
				XmlElement ce = c as XmlElement;
				if (ce != null && ce.ChildNodes.Count == 1 && ce.FirstChild is XmlText)
					d[ce.Name.ToLowerInvariant()] = ce.InnerText;
			}
			return d;
		}

		static string Get(Dictionary<string, string> d, params string[] names)
		{
			foreach (string n in names) { string v; if (d.TryGetValue(n, out v)) return v; }
			return null;
		}

		public static Result Import(string xmlFile)
		{
			Result r = new Result();
			XmlDocument doc = new XmlDocument();
			doc.Load(xmlFile);
			string cfgDir = Path.GetDirectoryName(xmlFile);
			string defaultCache = doc.DocumentElement != null ? doc.DocumentElement.GetAttribute("defaultCache") : "";
			if (string.IsNullOrEmpty(defaultCache)) defaultCache = "%CONFIGDIR%\\Cache\\%USERNAME%@%HOSTNAME%";

			foreach (XmlElement pe in doc.GetElementsByTagName("Profile"))
			{
				Dictionary<string, string> a = Attrs(pe);
				Profile p = new Profile();
				p.Name = Get(a, "name") ?? ("profile" + (r.Profiles.Count + 1));
				p.Host = Get(a, "hostname", "host", "address") ?? "";
				p.User = Get(a, "username", "user") ?? "";
				int port;
				p.Port = int.TryParse(Get(a, "port"), out port) ? port : 0;
				p.Protocol = ParseProtocol(Get(a, "securitymode", "security", "protocol", "mode"), p.Port);
				if (p.Port == 0) p.Port = p.Protocol == Protocol.Sftp ? 22 : p.Protocol == Protocol.Ftps ? 990 : 21;
				p.InitialDir = Get(a, "initialdir", "initialdirectory", "initialpath") ?? "";
				p.KeyFile = Get(a, "keyfile", "privatekey", "keyfilepath") ?? "";

				string pw = Get(a, "password", "pass");
				if (!string.IsNullOrEmpty(pw))
				{
					string plain = NppFtpCrypto.TryDecrypt(pw);
					if (plain != null) p.Password = plain;
					else r.Warnings.Add(L.F("{0}: пароль не удалось расшифровать, введите его вручную.", p.Name));
				}

				string cache = Get(a, "cache", "cachedir", "localcache", "cachepath");
				if (string.IsNullOrEmpty(cache)) cache = defaultCache;
				cache = Expand(cache, cfgDir, p);

				bool addedRoot = false;
				foreach (XmlNode n in pe.ChildNodes)
				{
					XmlElement ce = n as XmlElement;
					if (ce == null || ce.Name.ToLowerInvariant().IndexOf("cache") < 0) continue;
					Dictionary<string, string> ca = Attrs(ce);
					string loc = null, rem = null;
					foreach (KeyValuePair<string, string> kv in ca)
					{
						if (kv.Key.Contains("local")) loc = kv.Value;
						else if (kv.Key.Contains("ext") || kv.Key.Contains("remote")) rem = kv.Value;
					}
					if (!string.IsNullOrEmpty(loc))
					{
						p.Maps.Add(new PathMap(Expand(loc, cfgDir, p), string.IsNullOrEmpty(rem) ? "/" : rem));
						addedRoot = true;
					}
				}
				if (!addedRoot) p.Maps.Add(new PathMap(cache, "/"));
				r.Profiles.Add(p);
			}
			if (r.Profiles.Count == 0) r.Warnings.Add(L.F("В {0} не найдено ни одного профиля <Profile>.", xmlFile));
			return r;
		}

		static string Expand(string s, string cfgDir, Profile p)
		{
			s = s.Replace("%CONFIGDIR%", cfgDir)
				.Replace("%USERNAME%", p.User).Replace("%HOSTNAME%", p.Host).Replace("%PROFILENAME%", p.Name);
			s = Environment.ExpandEnvironmentVariables(s);
			return s.Replace("\\\\", "\\").Replace('/', '\\');
		}

		static Protocol ParseProtocol(string v, int port)
		{
			if (!string.IsNullOrEmpty(v))
			{
				string l = v.ToLowerInvariant();
				if (l.Contains("sftp") || l.Contains("ssh")) return Protocol.Sftp;
				if (l.Contains("ftpes") || l.Contains("explicit")) return Protocol.Ftpes;
				if (l.Contains("ftps") || l.Contains("implicit")) return Protocol.Ftps;
				if (l == "ftp") return Protocol.Ftp;
				int n;
				if (int.TryParse(v, out n))
				{
					if (port == 22) return Protocol.Sftp;
					switch (n) { case 1: return Protocol.Ftps; case 2: return Protocol.Ftpes; case 3: return Protocol.Sftp; default: return Protocol.Ftp; }
				}
			}
			return port == 22 ? Protocol.Sftp : Protocol.Ftp;
		}
	}

	/// <summary>NppFTP stores passwords with DES and the default key "NppFTP00". The exact text encoding is tried in several variants.</summary>
	public static class NppFtpCrypto
	{
		public const string DefaultKey = "NppFTP00";

		public static string TryDecrypt(string text, string key = DefaultKey)
		{
			if (string.IsNullOrEmpty(text)) return "";
			List<byte[]> candidates = new List<byte[]>();
			try { candidates.Add(Convert.FromBase64String(text)); } catch (Exception) { }
			if (Regex.IsMatch(text, "^([0-9a-fA-F]{2})+$"))
			{
				byte[] h = new byte[text.Length / 2];
				for (int i = 0; i < h.Length; i++) h[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
				candidates.Add(h);
			}
			byte[] k = Encoding.ASCII.GetBytes(key.PadRight(8, '0').Substring(0, 8));
			foreach (byte[] c in candidates)
			{
				if (c.Length == 0 || c.Length % 8 != 0) continue;
				foreach (CipherMode mode in new[] { CipherMode.ECB, CipherMode.CBC })
				{
					string s = Run(c, k, mode);
					if (s != null) return s;
				}
			}
			return null;
		}

		static string Run(byte[] data, byte[] key, CipherMode mode)
		{
			try
			{
				using (DES des = DES.Create())
				{
					des.Mode = mode; des.Padding = PaddingMode.None; des.Key = key; des.IV = new byte[8];
					byte[] plain = des.CreateDecryptor().TransformFinalBlock(data, 0, data.Length);
					int end = Array.IndexOf(plain, (byte)0);
					if (end < 0)
					{
						int pad = plain[plain.Length - 1];
						end = pad >= 1 && pad <= 8 ? plain.Length - pad : plain.Length;
					}
					if (end == 0) return "";
					for (int i = 0; i < end; i++) if (plain[i] < 0x20 || plain[i] > 0x7E) return null;
					return Encoding.ASCII.GetString(plain, 0, end);
				}
			}
			catch (Exception) { return null; }
		}

		public static string EncryptForTest(string plain, string key = DefaultKey)
		{
			byte[] k = Encoding.ASCII.GetBytes(key.PadRight(8, '0').Substring(0, 8));
			byte[] b = Encoding.ASCII.GetBytes(plain);
			byte[] padded = new byte[((b.Length / 8) + 1) * 8];
			Array.Copy(b, padded, b.Length);
			using (DES des = DES.Create())
			{
				des.Mode = CipherMode.ECB; des.Padding = PaddingMode.None; des.Key = k; des.IV = new byte[8];
				return Convert.ToBase64String(des.CreateEncryptor().TransformFinalBlock(padded, 0, padded.Length));
			}
		}
	}
}
