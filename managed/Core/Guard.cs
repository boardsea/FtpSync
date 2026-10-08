using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace FtpSync
{
	public enum SyncStatus { Untracked, InSync, LocalAhead, RemoteChanged, Conflict, NoBaseline, RemoteMissing, Error }

	public class Resolved
	{
		public Profile Profile; public string Remote; public string Key { get { return Profile.Name + "|" + Remote; } }
	}

	public class CheckResult
	{
		public SyncStatus Status;
		public Resolved Target;
		public byte[] Remote;
		public string RemoteSig, LocalSig;
		public string BaseText;       // null when no baseline
		public bool Ignored;          // user already dismissed this remote version
		public string Message;
		public DateTime Time = DateTime.Now;
	}

	public class FileState
	{
		public string Key = "";
		public string BaselineSig = "";
		public string BaselineFile = "";
		public string IgnoredSig = "";
		public DateTime LastCheck;
	}

	public class StateFile { public List<FileState> Files = new List<FileState>(); }

	/// <summary>Decides whether the server copy changed since the editor copy was loaded.</summary>
	public class Guard
	{
		public AppSettings Settings;
		public BackupStore Backups;
		readonly Dictionary<string, FileState> states = new Dictionary<string, FileState>();
		readonly string stateFile;
		readonly object sync = new object();

		public Guard(AppSettings s, string stateFile)
		{
			Settings = s; this.stateFile = stateFile;
			Backups = new BackupStore(s.BackupRoot) { MaxVersions = s.MaxVersions, KeepDays = s.KeepDays };
			LoadState();
		}

		public void ApplySettings(AppSettings s)
		{
			Settings = s;
			Backups.Root = s.BackupRoot; Backups.MaxVersions = s.MaxVersions; Backups.KeepDays = s.KeepDays;
		}

		// ---- path mapping ----
		public Resolved Resolve(string localPath)
		{
			if (string.IsNullOrEmpty(localPath)) return null;
			string lp = Norm(localPath);
			Resolved best = null; int bestLen = -1;
			foreach (Profile p in Settings.Profiles)
			{
				if (!p.Enabled) continue;
				foreach (PathMap m in p.Maps)
				{
					if (string.IsNullOrEmpty(m.Local)) continue;
					string root = Norm(m.Local).TrimEnd('\\');
					if (lp.Length > root.Length && lp.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) && root.Length > bestLen)
					{
						string rel = lp.Substring(root.Length).Replace('\\', '/');
						string rr = (m.Remote ?? "/").TrimEnd('/');
						best = new Resolved { Profile = p, Remote = rr + rel };
						bestLen = root.Length;
					}
				}
			}
			return best;
		}

		static string Norm(string p) { return p.Replace('/', '\\'); }

		// ---- state ----
		FileState State(string key)
		{
			FileState s;
			if (!states.TryGetValue(key, out s)) { s = new FileState { Key = key }; states[key] = s; }
			return s;
		}

		public FileState PeekState(Resolved r) { lock (sync) { FileState s; states.TryGetValue(r.Key, out s); return s; } }

		void LoadState()
		{
			try
			{
				if (!File.Exists(stateFile)) return;
				using (FileStream f = File.OpenRead(stateFile))
				{
					StateFile sf = (StateFile)new XmlSerializer(typeof(StateFile)).Deserialize(f);
					foreach (FileState s in sf.Files) states[s.Key] = s;
				}
			}
			catch (Exception) { }
		}

		public void SaveState()
		{
			lock (sync) try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(stateFile));
				StateFile sf = new StateFile();
				sf.Files.AddRange(states.Values);
				using (FileStream f = File.Create(stateFile)) new XmlSerializer(typeof(StateFile)).Serialize(f, sf);
			}
			catch (Exception) { }
		}

		// ---- operations ----
		public byte[] FetchRemote(Resolved r)
		{
			using (IRemote c = RemoteFactory.Create(r.Profile)) return c.Download(r.Remote);
		}

		void SetBaseline(Resolved r, byte[] remote, string reason)
		{
			bool created;
			string file = Backups.Save(r.Profile.Name, r.Remote, remote, reason, out created);
			FileState s = State(r.Key);
			s.BaselineSig = TextUtil.Sig(remote);
			s.BaselineFile = file;
			s.IgnoredSig = "";
			SaveState();
		}

		/// <summary>The caller decided that the editor now matches the given server version (reloaded, merged, or accepted).</summary>
		public void AcceptRemote(Resolved r, byte[] remote) { lock (sync) SetBaseline(r, remote, "remote"); }

		public void Ignore(CheckResult c)
		{
			lock (sync)
			{
				FileState s = State(c.Target.Key);
				s.IgnoredSig = c.RemoteSig ?? "";
				SaveState();
			}
		}

		public void BackupLocal(Resolved r, byte[] local, string reason)
		{
			bool created;
			lock (sync) Backups.Save(r.Profile.Name, r.Remote, local, reason, out created);
		}

		public void BackupRemote(Resolved r, byte[] remote, string reason)
		{
			bool created;
			lock (sync) Backups.Save(r.Profile.Name, r.Remote, remote, reason, out created);
		}

		public string BaselineText(Resolved r)
		{
			FileState s = PeekState(r);
			lock (sync) if (s == null || string.IsNullOrEmpty(s.BaselineFile) || !File.Exists(s.BaselineFile)) return null;
			try { return TextUtil.Decode(File.ReadAllBytes(s.BaselineFile)); } catch (IOException) { return null; }
		}

		/// <summary>Compares the editor content with the server copy.</summary>
		public CheckResult Check(string localPath, byte[] local)
		{
			Resolved r = Resolve(localPath);
			CheckResult res = new CheckResult { Target = r };
			if (r == null) { res.Status = SyncStatus.Untracked; return res; }
			byte[] remote;
			try { remote = FetchRemote(r); }
			catch (Exception ex)
			{
				res.Status = SyncStatus.Error; res.Message = ex.Message; return res;
			}
			lock (sync) return Evaluate(r, res, remote, local);
		}

		CheckResult Evaluate(Resolved r, CheckResult res, byte[] remote, byte[] local)
		{
			FileState st = State(r.Key);
			st.LastCheck = DateTime.Now;
			res.LocalSig = TextUtil.Sig(local);
			if (remote == null)
			{
				res.Status = SyncStatus.RemoteMissing;
				res.Message = L.T("Файла нет на сервере.");
				return res;
			}
			res.Remote = remote;
			res.RemoteSig = TextUtil.Sig(remote);
			res.BaseText = BaselineText(r);

			if (res.RemoteSig == res.LocalSig)
			{
				if (st.BaselineSig != res.RemoteSig) SetBaseline(r, remote, "synced");
				res.Status = SyncStatus.InSync; return res;
			}
			if (string.IsNullOrEmpty(st.BaselineSig)) { res.Status = SyncStatus.NoBaseline; res.Ignored = st.IgnoredSig == res.RemoteSig; BackupRemote(r, remote, "remote"); return res; }
			if (res.RemoteSig == st.BaselineSig) { res.Status = SyncStatus.LocalAhead; return res; }

			BackupRemote(r, remote, "remote");
			res.Status = res.LocalSig == st.BaselineSig ? SyncStatus.RemoteChanged : SyncStatus.Conflict;
			res.Ignored = st.IgnoredSig == res.RemoteSig;
			return res;
		}

		public void UploadFile(Resolved r, byte[] data, ProgressFn progress = null)
		{
			byte[] cur;
			using (IRemoteFs c = RemoteFactory.CreateFs(r.Profile))
			{
				cur = c.Download(r.Remote);
				if (cur != null)
				{
					BackupRemote(r, cur, "pre-upload");
					if (TextUtil.Sig(cur) == TextUtil.Sig(data) && cur.Length == data.Length)
					{
						lock (sync) SetBaseline(r, data, "remote");
						return; // identical, nothing to send
					}
				}
				c.Upload(r.Remote, data, progress);
			}
			lock (sync) SetBaseline(r, data, "remote");
		}
	}
}
