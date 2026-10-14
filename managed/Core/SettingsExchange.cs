using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace FtpSync
{
	/// <summary>Export / import of the plugin settings as XML (everything) or CSV (profiles only).</summary>
	public static class SettingsExchange
	{
		static readonly string[] Cols = { "Group", "Name", "Host", "Port", "Protocol", "User", "Password", "KeyFile", "InitialDir", "Enabled", "AutoUpload", "ShowHidden", "Passive", "Comment", "Maps" };

		static Profile Copy(Profile p, bool withPasswords)
		{
			using (MemoryStream ms = new MemoryStream())
			{
				XmlSerializer xs = new XmlSerializer(typeof(Profile));
				xs.Serialize(ms, p); ms.Position = 0;
				Profile c = (Profile)xs.Deserialize(ms);
				c.PasswordProtected = withPasswords ? Secret.PlainToken(p.Password) : "";
				return c;
			}
		}

		public static void ExportXml(AppSettings s, string file, bool withPasswords)
		{
			AppSettings c = new AppSettings
			{
				BackupRoot = s.BackupRoot, PollSeconds = s.PollSeconds, MaxVersions = s.MaxVersions, KeepDays = s.KeepDays, CheckOnSave = s.CheckOnSave,
				CheckOnActivate = s.CheckOnActivate, CheckOnTimer = s.CheckOnTimer, Language = s.Language, ShowExplorerOnStart = s.ShowExplorerOnStart
			};
			foreach (Profile p in s.Profiles) c.Profiles.Add(Copy(p, withPasswords));
			using (FileStream f = File.Create(file)) new XmlSerializer(typeof(AppSettings)).Serialize(f, c);
		}

		public static AppSettings ReadXml(string file)
		{
			using (FileStream f = File.OpenRead(file)) return (AppSettings)new XmlSerializer(typeof(AppSettings)).Deserialize(f);
		}

		static string Q(string v)
		{
			v = v ?? "";
			return v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
		}

		public static void ExportCsv(AppSettings s, string file, bool withPasswords)
		{
			StringBuilder sb = new StringBuilder();
			sb.Append(string.Join(",", Cols)).Append("\r\n");
			foreach (Profile p in s.Profiles)
			{
				List<string> maps = new List<string>();
				foreach (PathMap m in p.Maps) maps.Add(m.Local + "=>" + m.Remote);
				string[] v = { p.Group, p.Name, p.Host, p.Port.ToString(), p.Protocol.ToString(), p.User, withPasswords ? p.Password : "", p.KeyFile, p.InitialDir,
					p.Enabled ? "1" : "0", p.AutoUpload ? "1" : "0", p.ShowHidden ? "1" : "0", p.Passive ? "1" : "0", p.Comment, string.Join("|", maps.ToArray()) };
				for (int i = 0; i < v.Length; i++) { if (i > 0) sb.Append(','); sb.Append(Q(v[i])); }
				sb.Append("\r\n");
			}
			File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
		}

		public static List<Profile> ReadCsv(string file)
		{
			List<List<string>> rows = FileZillaCsvImporter.ParseCsv(File.ReadAllText(file, Encoding.UTF8));
			List<Profile> res = new List<Profile>();
			if (rows.Count == 0) return res;
			Dictionary<string, int> ix = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < rows[0].Count; i++) ix[rows[0][i].Trim()] = i;
			if (!ix.ContainsKey("Name") || !ix.ContainsKey("Host")) throw new InvalidDataException("CSV: Name / Host");
			for (int r = 1; r < rows.Count; r++)
			{
				List<string> row = rows[r];
				Func<string, string> g = delegate (string c) { int k; return ix.TryGetValue(c, out k) && k < row.Count ? row[k] : ""; };
				if (g("Name").Trim().Length == 0 && g("Host").Trim().Length == 0) continue;
				Profile p = new Profile { Group = g("Group").Trim().Trim('/'), Name = g("Name").Trim(), Host = g("Host").Trim(), User = g("User"), KeyFile = g("KeyFile"), InitialDir = g("InitialDir"), Comment = g("Comment") };
				int port; p.Port = int.TryParse(g("Port"), out port) && port > 0 ? port : 21;
				Protocol pr; p.Protocol = Enum.TryParse(g("Protocol"), true, out pr) ? pr : Protocol.Ftp;
				p.Password = g("Password");
				p.Enabled = g("Enabled") != "0"; p.AutoUpload = g("AutoUpload") != "0"; p.ShowHidden = g("ShowHidden") != "0"; p.Passive = g("Passive") != "0";
				foreach (string m in g("Maps").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
				{
					int k = m.IndexOf("=>");
					if (k > 0) p.Maps.Add(new PathMap(m.Substring(0, k), m.Substring(k + 2)));
				}
				if (p.Maps.Count == 0) p.Maps.Add(new PathMap("", "/"));
				res.Add(p);
			}
			return res;
		}

		/// <summary>Adds the profiles, replacing those with the same name and group. Returns how many were taken.</summary>
		public static int Merge(AppSettings target, IEnumerable<Profile> profiles)
		{
			int n = 0;
			foreach (Profile p in profiles)
			{
				Profile q = p;
				target.Profiles.RemoveAll(x => string.Equals(x.Name, q.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Group, q.Group, StringComparison.OrdinalIgnoreCase));
				target.Profiles.Add(q); n++;
			}
			return n;
		}
	}
}
