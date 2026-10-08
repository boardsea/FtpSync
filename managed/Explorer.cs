using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FtpSync
{
	/// <summary>Remote file operations behind the connection tree. Everything public here is called on the UI thread.</summary>
	public static class Explorer
	{
		public static readonly TransferQueue Queue = new TransferQueue();
		public static readonly HashSet<string> Connected = new HashSet<string>();
		public static ExplorerPanel Panel;

		static IWin32Window Owner { get { return new NppWindow(); } }

		public static void Init()
		{
			Queue.Changed += delegate (QueueItem i) { Plugin.UiInvoke(delegate { if (Panel != null) Panel.UpdateItem(i); }); };
		}

		public static void Log(string s) { EventLog.Info(L.T("Обозреватель"), s); }

		// ---------------------------------------------------------------- connect
		/// <summary>Connects, finds the start folder (last used, configured, or the server's login directory) and lists it.</summary>
		public static void Connect(Profile p, Action<bool, string, List<RemoteEntry>> done)
		{
			if (RemoteFactory.NeedsPassword(p))
			{
				string pw = PromptForm.Ask(Owner, "FTP Sync", L.T("Пароль для ") + p.User + "@" + p.Host, "", true);
				if (pw == null) { if (done != null) done(false, null, null); return; }
				RemoteFactory.SetPassword(p, pw);
			}
			EventLog.Info(L.T("Подключение"), L.T("Подключение к ") + p.Name + " (" + p.Host + ")…");
			ThreadPool.QueueUserWorkItem(delegate
			{
				string err = null, start = null; List<RemoteEntry> entries = null;
				try
				{
					using (IRemoteFs fs = RemoteFactory.CreateFs(p))
					{
						string home = "/";
						try { home = fs.GetHome(); } catch (Exception ex) { if (ex is NotSupportedException) throw; }
						List<string> cands = new List<string>();
						if (p.RememberLastDir && !string.IsNullOrEmpty(p.LastDir)) cands.Add(p.LastDir);
						if (!string.IsNullOrEmpty(p.InitialDir) && p.InitialDir != "/") cands.Add(InitialDir(p));
						cands.Add(home);
						Exception last = null;
						foreach (string c in cands)
						{
							try { entries = fs.List(c); start = c; break; }
							catch (Exception ex) { last = ex; }
						}
						if (start == null) throw last ?? new Exception(L.T("не удалось открыть стартовую папку"));
					}
				}
				catch (Exception ex) { err = ex.Message; EventLog.Error(L.T("Подключение"), p.Name + " (" + p.Protocol + "://" + p.Host + ":" + p.Port + "): " + ex.Message, ex); }
				Plugin.UiInvoke(delegate
				{
					if (err == null) { Connected.Add(p.Name); EventLog.Ok(L.T("Подключение"), L.T("Подключено: ") + p.Name + "  " + start); }
					else { MessageBox.Show(Owner, L.T("Не удалось подключиться к ") + p.Name + ":\n" + err, "FTP Sync", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
					if (done != null) done(err == null, start, entries);
				});
			});
		}

		public static void Remember(Profile p, string dir)
		{
			if (!p.RememberLastDir || string.IsNullOrEmpty(dir) || p.LastDir == dir) return;
			p.LastDir = dir;
			Plugin.SaveSettings();
		}

		public static void Disconnect(Profile p)
		{
			Connected.Remove(p.Name);
			RemoteFactory.Disconnect(p);
			Log(L.T("Отключено: ") + p.Name);
		}

		public static string InitialDir(Profile p)
		{
			string d = string.IsNullOrEmpty(p.InitialDir) ? "/" : p.InitialDir.Replace('\\', '/');
			return d.StartsWith("/") ? d : "/" + d;
		}

		public static void List(Profile p, string dir, Action<List<RemoteEntry>, string> done)
		{
			ThreadPool.QueueUserWorkItem(delegate
			{
				List<RemoteEntry> r = null; string err = null;
				try { using (IRemoteFs fs = RemoteFactory.CreateFs(p)) r = fs.List(dir); }
				catch (Exception ex) { err = ex.Message; EventLog.Error(L.T("Список папки"), p.Name + "  " + dir + ": " + ex.Message, ex); }
				Plugin.UiInvoke(delegate { done(r, err); });
			});
		}

		// ---------------------------------------------------------------- download / open
		public static void OpenFile(Profile p, string remote)
		{
			string name = RemotePath.Name(remote);
			Queue.Enqueue(L.T("Скачивание"), p.Name, remote, delegate (QueueItem it)
			{
				byte[] data;
				using (IRemoteFs fs = RemoteFactory.CreateFs(p)) data = fs.Download(remote, it.Report);
				if (data == null) throw new FileNotFoundException(L.T("Файл не найден на сервере: ") + remote);
				it.Report(data.Length, data.Length);
				Plugin.UiInvoke(delegate { try { StoreAndOpen(p, remote, data); } catch (Exception ex) { EventLog.Error(L.T("Открытие файла"), remote + ": " + ex.Message, ex); MessageBox.Show(Owner, ex.Message, "FTP Sync"); } });
			});
		}

		static void StoreAndOpen(Profile p, string remote, byte[] data)
		{
			string local = Plugin.LocalPathFor(p, remote);
			Resolved r = new Resolved { Profile = p, Remote = remote };
			Directory.CreateDirectory(Path.GetDirectoryName(local));
			bool write = true;
			if (File.Exists(local))
			{
				byte[] cur = File.ReadAllBytes(local);
				string cs = TextUtil.Sig(cur), rs = TextUtil.Sig(data);
				FileState st = Plugin.GuardRef.PeekState(r);
				if (cs != rs && (st == null || cs != st.BaselineSig))
				{
					DialogResult a = MessageBox.Show(Owner, L.T("Локальная копия отличается от серверной и не была выложена:\n") + local +
						L.T("\n\nДа: заменить серверной версией (локальная уйдёт в бекап)\nНет: оставить локальную и открыть её\nОтмена: ничего не делать"),
						"FTP Sync", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
					if (a == DialogResult.Cancel) return;
					if (a == DialogResult.No) write = false;
					else Plugin.GuardRef.BackupLocal(r, cur, "local-replaced");
				}
			}
			if (write)
			{
				if (File.Exists(local)) File.SetAttributes(local, FileAttributes.Normal);
				File.WriteAllBytes(local, data);
				Plugin.NoteWrite(local);
				Plugin.GuardRef.AcceptRemote(r, data);
			}
			Npp.Open(local);
		}

		public static void DownloadFolder(Profile p, string dir)
		{
			Queue.Enqueue(L.T("Скачивание папки"), p.Name, dir, delegate (QueueItem it)
			{
				List<RemoteEntry> files = new List<RemoteEntry>();
				using (IRemoteFs fs = RemoteFactory.CreateFs(p)) RemoteOps.Walk(fs, dir, files.Add);
				long done = 0;
				using (IRemoteFs fs = RemoteFactory.CreateFs(p))
					foreach (RemoteEntry e in files)
					{
						it.Check();
						byte[] data = fs.Download(e.Path);
						if (data == null) continue;
						string local = Plugin.LocalPathFor(p, e.Path);
						Directory.CreateDirectory(Path.GetDirectoryName(local));
						File.WriteAllBytes(local, data);
						Resolved r = new Resolved { Profile = p, Remote = e.Path };
						Plugin.GuardRef.AcceptRemote(r, data);
						it.Report(++done, files.Count);
					}
				Plugin.UiInvoke(delegate { Log(L.F("Папка скачана в кэш, файлов: {0}", files.Count)); });
			});
		}

		// ---------------------------------------------------------------- upload
		public static void UploadLocal(Resolved r, string localPath)
		{
			Queue.Enqueue(L.T("Выкладка"), r.Profile.Name, r.Remote, delegate (QueueItem it)
			{
				byte[] data = File.ReadAllBytes(localPath);
				Plugin.GuardRef.UploadFile(r, data, it.Report);
				it.Report(data.Length, data.Length);
				Plugin.UiInvoke(delegate { Plugin.MarkInSync(localPath, r); Log(L.T("Выложено: ") + r.Remote); });
			});
		}

		public static void UploadSaved(string localPath)
		{
			Resolved r = Plugin.GuardRef.Resolve(localPath);
			if (r == null || !r.Profile.AutoUpload) return;
			QueueItem it = null;
			it = Queue.Enqueue(L.T("Выкладка"), r.Profile.Name, r.Remote, delegate (QueueItem q)
			{
				byte[] data = File.ReadAllBytes(localPath);
				try { Plugin.GuardRef.UploadFile(r, data, q.Report); }
				catch (OperationCanceledException) { throw; }
				catch (Exception ex)
				{
					Plugin.UiInvoke(delegate { MessageBox.Show(Owner, L.F("Не удалось выложить {0}:\n{1}\n\nФайл сохранён локально, на сервере прежняя версия.", r.Remote, ex.Message), "FTP Sync", MessageBoxButtons.OK, MessageBoxIcon.Error); });
					throw;
				}
				q.Report(data.Length, data.Length);
				Plugin.UiInvoke(delegate { Plugin.MarkInSync(localPath, r); });
			});
		}

		public static void UploadFiles(Profile p, string dir, string[] files)
		{
			foreach (string f in files)
			{
				if (Directory.Exists(f)) { UploadDirectory(p, RemotePath.Combine(dir, Path.GetFileName(f)), f); continue; }
				string local = f; string remote = RemotePath.Combine(dir, Path.GetFileName(f));
				Resolved r = new Resolved { Profile = p, Remote = remote };
				Queue.Enqueue(L.T("Выкладка"), p.Name, remote, delegate (QueueItem it)
				{
					Plugin.GuardRef.UploadFile(r, File.ReadAllBytes(local), it.Report);
					Plugin.UiInvoke(delegate { if (Panel != null) Panel.RefreshDir(p, dir); });
				});
			}
		}

		static void UploadDirectory(Profile p, string remoteDir, string localDir)
		{
			Queue.Enqueue(L.T("Выкладка папки"), p.Name, remoteDir, delegate (QueueItem it)
			{
				List<string> files = new List<string>(Directory.GetFiles(localDir, "*", SearchOption.AllDirectories));
				using (IRemoteFs fs = RemoteFactory.CreateFs(p))
				{
					HashSet<string> made = new HashSet<string>();
					try { fs.MakeDir(remoteDir); } catch (Exception) { }
					int n = 0;
					foreach (string f in files)
					{
						it.Check();
						string rel = f.Substring(localDir.Length).TrimStart('\\', '/').Replace('\\', '/');
						string remote = RemotePath.Combine(remoteDir, rel);
						string d = RemotePath.Parent(remote);
						if (made.Add(d)) MakeDirs(fs, remoteDir, d);
						fs.Upload(remote, File.ReadAllBytes(f));
						it.Report(++n, files.Count);
					}
				}
				Plugin.UiInvoke(delegate { if (Panel != null) Panel.RefreshDir(p, RemotePath.Parent(remoteDir)); });
			});
		}

		static void MakeDirs(IRemoteFs fs, string root, string dir)
		{
			string rel = dir.Length > root.Length ? dir.Substring(root.Length).Trim('/') : "";
			string cur = root;
			foreach (string seg in rel.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
			{
				cur = RemotePath.Combine(cur, seg);
				try { fs.MakeDir(cur); } catch (Exception) { }
			}
		}

		// ---------------------------------------------------------------- structure changes
		public static void Delete(Profile p, RemoteEntry e, Action done)
		{
			string what = e.IsDir ? L.T("папку со всем содержимым") : L.T("файл");
			if (MessageBox.Show(Owner, L.F("Удалить на сервере {0}?\n{1}  {2}\n\nКопии удаляемых файлов сохраняются в бекапы.", what, p.Name, e.Path),
				"FTP Sync", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
			Queue.Enqueue(L.T("Удаление"), p.Name, e.Path, delegate (QueueItem it)
			{
				int saved = 0; long bytes = 0;
				Action<string> backup = delegate (string path)
				{
					if (saved >= 500 || bytes > 100L * 1024 * 1024) return;
					using (IRemoteFs f2 = RemoteFactory.CreateFs(p))
					{
						byte[] data = f2.Download(path);
						if (data != null) { Plugin.GuardRef.BackupRemote(new Resolved { Profile = p, Remote = path }, data, "deleted"); saved++; bytes += data.Length; }
					}
				};
				using (IRemoteFs fs = RemoteFactory.CreateFs(p))
				{
					if (e.IsDir && !e.IsLink) RemoteOps.DeleteRecursive(fs, e.Path, backup);
					else { if (!e.IsDir) backup(e.Path); fs.Delete(e.Path, e.IsDir && !e.IsLink); }
				}
				Plugin.UiInvoke(delegate { Log(L.F("Удалено: {0}", e.Path) + (saved > 0 ? L.F(" (копий в бекапе: {0})", saved) : "")); if (done != null) done(); });
			});
		}

		public static void Rename(Profile p, RemoteEntry e, string newName, Action done)
		{
			string target = RemotePath.Combine(RemotePath.Parent(e.Path), newName);
			Queue.Enqueue(L.T("Переименование"), p.Name, e.Path + " → " + newName, delegate (QueueItem it)
			{
				using (IRemoteFs fs = RemoteFactory.CreateFs(p)) fs.Rename(e.Path, target);
				Plugin.UiInvoke(delegate { if (done != null) done(); });
			});
		}

		public static void MakeDir(Profile p, string parent, string name, Action done)
		{
			string path = RemotePath.Combine(parent, name);
			Queue.Enqueue(L.T("Новая папка"), p.Name, path, delegate (QueueItem it)
			{
				using (IRemoteFs fs = RemoteFactory.CreateFs(p)) fs.MakeDir(path);
				Plugin.UiInvoke(delegate { if (done != null) done(); });
			});
		}

		public static void NewFile(Profile p, string parent, string name, Action done)
		{
			string path = RemotePath.Combine(parent, name);
			Queue.Enqueue(L.T("Новый файл"), p.Name, path, delegate (QueueItem it)
			{
				using (IRemoteFs fs = RemoteFactory.CreateFs(p)) fs.Upload(path, new byte[0]);
				Plugin.UiInvoke(delegate { if (done != null) done(); OpenFile(p, path); });
			});
		}
	}
}
