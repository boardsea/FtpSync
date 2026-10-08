using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace FtpSync
{
	public enum Protocol { Ftp, Ftpes, Ftps, Sftp }

	public class PathMap
	{
		public string Local = "";
		public string Remote = "/";
		public PathMap() { }
		public PathMap(string l, string r) { Local = l; Remote = r; }
	}

	public class Bookmark
	{
		public string Name = "", Local = "", Remote = "/";
		public Bookmark() { }
		public Bookmark(string n, string l, string r) { Name = n; Local = l; Remote = r; }
	}

	public class Profile
	{
		/// <summary>Folder path in the tree, "A/B" (FileZilla Site Manager folders).</summary>
		public string Group = "";
		public string Comment = "";
		public bool Passive = true;
		public List<Bookmark> Bookmarks = new List<Bookmark>();
		public string Name = "";
		public string Host = "";
		public int Port = 21;
		public Protocol Protocol = Protocol.Ftp;
		public string User = "";
		public string PasswordProtected = "";
		public string KeyFile = "";
		public string InitialDir = "";
		public string LastDir = "";
		public bool RememberLastDir = true;
		public bool Enabled = true;
		public bool AutoUpload = true;
		public bool ShowHidden = true;
		public List<PathMap> Maps = new List<PathMap>();

		[XmlIgnore]
		public string Password
		{
			get { return Secret.Unprotect(PasswordProtected); }
			set { PasswordProtected = Secret.Protect(value); }
		}

		public override string ToString() { return string.IsNullOrEmpty(Group) ? Name : Group + " / " + Name; }
	}

	public class AppSettings
	{
		public string BackupRoot = "";
		public int PollSeconds = 60;
		public int MaxVersions = 50;
		public int KeepDays = 180;
		public bool CheckOnSave = true;
		public bool CheckOnActivate = true;
		public string Language = "auto";
		public bool CheckOnTimer = true;
		public bool ShowExplorerOnStart = true;
		public List<Profile> Profiles = new List<Profile>();
	}

	public static class Secret
	{
		public static string Protect(string s)
		{
			if (string.IsNullOrEmpty(s)) return "";
			try
			{
				byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser);
				return "dpapi:" + Convert.ToBase64String(enc);
			}
			catch (Exception)
			{
				return "plain:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
			}
		}

		public static string Unprotect(string s)
		{
			if (string.IsNullOrEmpty(s)) return "";
			try
			{
				if (s.StartsWith("dpapi:"))
					return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s.Substring(6)), null, DataProtectionScope.CurrentUser));
				if (s.StartsWith("plain:"))
					return Encoding.UTF8.GetString(Convert.FromBase64String(s.Substring(6)));
			}
			catch (Exception) { }
			return "";
		}
	}

	public static class SettingsStore
	{
		public static string Dir;

		public static string FilePath { get { return Path.Combine(Dir, "settings.xml"); } }

		public static AppSettings Load()
		{
			AppSettings s = null;
			try
			{
				if (File.Exists(FilePath))
					using (FileStream f = File.OpenRead(FilePath))
						s = (AppSettings)new XmlSerializer(typeof(AppSettings)).Deserialize(f);
			}
			catch (Exception) { }
			if (s == null) s = new AppSettings();
			if (string.IsNullOrEmpty(s.BackupRoot)) s.BackupRoot = Path.Combine(Dir, "Backups");
			return s;
		}

		public static void Save(AppSettings s)
		{
			Directory.CreateDirectory(Dir);
			string tmp = FilePath + ".tmp";
			using (FileStream f = File.Create(tmp))
				new XmlSerializer(typeof(AppSettings)).Serialize(f, s);
			if (File.Exists(FilePath)) File.Delete(FilePath);
			File.Move(tmp, FilePath);
		}
	}
}
