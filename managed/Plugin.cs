using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FtpSync
{
	class NppWindow : IWin32Window { public IntPtr Handle { get { return Npp.Hwnd; } } }

	/// <summary>Entry point called from the native shim; wires Notepad++ events to the guard logic.</summary>
	public static class Plugin
	{
		public const string Version = "0.5.8";
		[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void CmdDel(int i);
		[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void NotifyDel(IntPtr scn);
		static CmdDel cmdDel; static NotifyDel notifyDel;

		static AppSettings settings;
		static Guard guard;
		static Control ui;
		static PanelForm panel;
		static System.Windows.Forms.Timer timer;
		static string dir, pluginDir;
		static bool ready, wasForeground, panelRegistered;
		static int tick;
		static IntPtr hostTable;
		static readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
		static readonly Dictionary<string, CheckResult> lastStatus = new Dictionary<string, CheckResult>();
		static readonly Dictionary<string, DateTime> lastCheck = new Dictionary<string, DateTime>();
		static readonly HashSet<string> busy = new HashSet<string>();
		static readonly Dictionary<string, ConflictForm> notices = new Dictionary<string, ConflictForm>();
		static readonly List<Action> afterSave = new List<Action>();

		public static BackupStore Backups { get { return guard.Backups; } }

		public static AppSettings Settings { get { return settings; } }
		public static Guard GuardRef { get { return guard; } }

		static string[] Names() { return new[] {
			L.T("Показать дерево подключений (FTP)"), L.T("Показать панель статуса и бекапов"), L.T("Проверить текущий файл с сервером"),
			L.T("Показать различия с сервером"), L.T("Бекапы текущего файла"), L.T("Выложить текущий файл на сервер (с проверкой)"),
			L.T("Профили и настройки…"), L.T("Импорт профилей из NppFTP…"), L.T("Импорт профилей из FileZilla (XML/CSV)…"), L.T("О плагине / диагностика") }; }

		public static List<Profile> SortedProfiles()
		{
			List<Profile> l = new List<Profile>(settings.Profiles);
			l.Sort((x, y) => NaturalComparer.Instance.Compare(x.Name, y.Name));
			return l;
		}

		static readonly Dictionary<string, DateTime> lastWrite = new Dictionary<string, DateTime>();

		public static void NoteWrite(string path)
		{
			try { lastWrite[path] = File.GetLastWriteTimeUtc(path); } catch (IOException) { }
		}

		public static void UiInvoke(Action a) { ui.BeginInvoke(a); }
		public static void SaveSettings() { try { SettingsStore.Save(settings); } catch (Exception ex) { Log(ex); } }

		// ---------------------------------------------------------------- startup
		public static int Bootstrap(string arg)
		{
			try
			{
				AppDomain.CurrentDomain.AssemblyResolve += ResolveFromPluginDir;
				IntPtr t = (IntPtr)long.Parse(arg);
				hostTable = t;
				int P = IntPtr.Size;
				Npp.Hwnd = Marshal.ReadIntPtr(t, 0); Npp.Sci1 = Marshal.ReadIntPtr(t, P); Npp.Sci2 = Marshal.ReadIntPtr(t, 2 * P);
				pluginDir = Marshal.PtrToStringUni(IntPtr.Add(t, 6 * P));

				Application.EnableVisualStyles();
				ui = new Control(); IntPtr h = ui.Handle;

				dir = Path.Combine(Npp.ConfigDir(), "FtpSync");
				EventLog.FilePath = Path.Combine(dir, "log.txt");
				SettingsStore.Dir = dir;
				bool first = !File.Exists(SettingsStore.FilePath);
				settings = SettingsStore.Load();
				L.Init(pluginDir, settings.Language);
				guard = new Guard(settings, Path.Combine(dir, "state.xml"));
				RemoteFactory.TimeoutMs = 8000;

				cmdDel = OnCommand; notifyDel = OnNotify;
				string[] names = Names();
				Marshal.WriteInt32(t, 3 * P, names.Length);
				Marshal.WriteIntPtr(t, 4 * P, Marshal.GetFunctionPointerForDelegate(cmdDel));
				Marshal.WriteIntPtr(t, 5 * P, Marshal.GetFunctionPointerForDelegate(notifyDel));
				int namesOff = 6 * P + 260 * 2;
				for (int i = 0; i < names.Length; i++)
				{
					char[] c = (names[i] + "\0").ToCharArray();
					Marshal.Copy(c, 0, IntPtr.Add(t, namesOff + i * 128), Math.Min(c.Length, 63));
				}

				Explorer.Init();
				EventLog.Info(L.T("Плагин"), L.F("FTP Sync {0} запущен, профилей: {1}", Version, settings.Profiles.Count));
				timer = new System.Windows.Forms.Timer { Interval = 1000 };
				timer.Tick += OnTick; timer.Start();
				if (first) ui.BeginInvoke(new Action(FirstRun));
				return 1;
			}
			catch (Exception ex) { Log(ex); return 0; }
		}

		static Assembly ResolveFromPluginDir(object s, ResolveEventArgs e)
		{
			try
			{
				string n = new AssemblyName(e.Name).Name + ".dll";
				string p = Path.Combine(pluginDir ?? Path.GetDirectoryName(typeof(Plugin).Assembly.Location), n);
				return File.Exists(p) ? Assembly.LoadFrom(p) : null;
			}
			catch (Exception) { return null; }
		}

		static void Log(Exception ex) { EventLog.Error(L.T("Плагин"), ex.Message, ex); }

		static void FirstRun()
		{
			try { SettingsStore.Save(settings); }
			catch (Exception ex) { Log(ex); }
		}

		// ---------------------------------------------------------------- commands
		static void OnCommand(int i)
		{
			try
			{
				switch (i)
				{
					case 0: ShowExplorer(); break;
					case 1: ShowPanel(0); break;
					case 2: CheckNow(Npp.CurrentPath(), true); break;
					case 3: ShowDiffFor(Npp.CurrentPath()); break;
					case 4: ShowPanel(1); SelectBackupsForCurrent(panel); break;
					case 5: UploadCurrent(); break;
					case 6: OpenSettings(); break;
					case 7: DoImport(null); break;
					case 8: DoImportFileZilla(); break;
					case 9: About(); break;
				}
			}
			catch (Exception ex) { Log(ex); MessageBox.Show(new NppWindow(), ex.Message, "FTP Sync"); }
		}

		static void ShowPanel(int tab)
		{
			if (panel == null) panel = new PanelForm();
			if (!panelRegistered)
			{
				Npp.RegisterDock(panel.Handle, L.T("FTP Sync - статус и бекапы"), 0, Npp.DockBottom);
				panelRegistered = true;
				foreach (KeyValuePair<string, CheckResult> kv in lastStatus) panel.SetStatus(kv.Key, kv.Value);
			}
			Npp.ShowDock(panel.Handle);
			panel.ShowTab(tab);
			if (tab == 1) panel.RebuildTree();
		}

		static bool explorerRegistered;

		static void ShowExplorer()
		{
			if (Explorer.Panel == null) Explorer.Panel = new ExplorerPanel();
			if (!explorerRegistered)
			{
				Npp.RegisterDock(Explorer.Panel.Handle, L.T("FTP Sync - подключения"), 1, Npp.DockRight);
				explorerRegistered = true;
			}
			Npp.ShowDock(Explorer.Panel.Handle);
		}

		public static void ShowGuardPanel(int tab) { ShowPanel(tab); }
		public static void OpenSettingsCommand() { OpenSettings(); }
		public static void UploadCurrentCommand() { UploadCurrent(); }

		public static void ShowBackupsFor(Profile p, string remote)
		{
			ShowPanel(1);
			if (!panel.SelectRemote(p.Name, remote)) MessageBox.Show(Owner, L.T("Бекапов этого файла пока нет."), "FTP Sync");
		}

		public static void MarkInSync(string localPath, Resolved r)
		{
			CheckResult c = new CheckResult { Status = SyncStatus.InSync, Target = r };
			lastStatus[localPath] = c;
			if (panel != null && panelRegistered) panel.SetStatus(localPath, c);
		}

		static void OpenSettings()
		{
			using (SettingsForm f = new SettingsForm(settings, Npp.ConfigDir()))
				if (f.ShowDialog(new NppWindow()) == DialogResult.OK) ApplySettings(f.Data);
		}

		static void ApplySettings(AppSettings s)
		{
			settings = s;
			SettingsStore.Save(s);
			EventLog.Info(L.T("Настройки"), L.T("Настройки сохранены, профилей: ") + s.Profiles.Count);
			guard.ApplySettings(s);
			if (panel != null) panel.RebuildTree();
			if (Explorer.Panel != null) Explorer.Panel.RebuildRoots();
		}

		static void DoImport(string file)
		{
			if (file == null)
			{
				OpenFileDialog d = new OpenFileDialog { Filter = L.T("NppFTP.xml|*.xml|Все файлы|*.*"), FileName = NppFtpImporter.DefaultFile(Npp.ConfigDir()) };
				if (d.ShowDialog(new NppWindow()) != DialogResult.OK) return;
				file = d.FileName;
			}
			NppFtpImporter.Result r = NppFtpImporter.Import(file);
			foreach (Profile p in r.Profiles)
			{
				settings.Profiles.RemoveAll(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase));
				settings.Profiles.Add(p);
			}
			SettingsStore.Save(settings);
			string w = r.Warnings.Count > 0 ? "\n\n" + string.Join("\n", r.Warnings.ToArray()) : "";
			EventLog.Info(L.T("Импорт"), L.T("NppFTP: импортировано профилей ") + r.Profiles.Count + (r.Warnings.Count > 0 ? L.T(", предупреждений: ") + r.Warnings.Count : ""));
			MessageBox.Show(new NppWindow(), L.T("Импортировано профилей: ") + r.Profiles.Count + w +
				L.T("\n\nОткройте «Профили и настройки», чтобы проверить протокол, порт, пароль и папки."), "FTP Sync");
		}

		static void DoImportFileZilla()
		{
			string def = FileZillaXmlImporter.DefaultFile();
			OpenFileDialog d = new OpenFileDialog { Filter = L.T("FileZilla (XML, CSV)|*.xml;*.csv|Все файлы|*.*"), Title = L.T("Файл со списком сайтов FileZilla (sitemanager.xml или экспорт)") };
			if (def.Length > 0) { d.InitialDirectory = Path.GetDirectoryName(def); d.FileName = def; }
			if (d.ShowDialog(new NppWindow()) != DialogResult.OK) return;
			NppFtpImporter.Result r = FileZillaImporter.Import(d.FileName);
			foreach (Profile p in r.Profiles)
			{
				settings.Profiles.RemoveAll(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Group, p.Group, StringComparison.OrdinalIgnoreCase));
				settings.Profiles.Add(p);
			}
			SettingsStore.Save(settings);
			if (Explorer.Panel != null) Explorer.Panel.RebuildRoots();
			EventLog.Info(L.T("Импорт"), L.F("FileZilla: импортировано профилей {0}, предупреждений: {1}", r.Profiles.Count, r.Warnings.Count));
			string w = r.Warnings.Count > 0 ? "\n\n" + string.Join("\n", r.Warnings.ToArray()) : "";
			MessageBox.Show(new NppWindow(), L.F("Импортировано профилей: {0}", r.Profiles.Count) + w +
				L.T("\n\nПароли сохранены в зашифрованном виде (Windows DPAPI, только для вашей учётной записи). Файл с открытыми паролями теперь лучше удалить."), "FTP Sync");
		}

		static void About()
		{
			StringBuilder sb = new StringBuilder();
			sb.AppendLine("FTP Sync " + Version);
			sb.AppendLine(L.T("Настройки: ") + SettingsStore.FilePath);
			sb.AppendLine(L.T("Бекапы: ") + settings.BackupRoot);
			sb.AppendLine("NppFTP.xml: " + NppFtpImporter.DefaultFile(Npp.ConfigDir()));
			sb.AppendLine(L.T("Профилей: ") + settings.Profiles.Count);
			foreach (Profile p in settings.Profiles)
			{
				sb.AppendLine("  " + p.Name + "  " + p.Protocol + "://" + p.Host + ":" + p.Port + (p.Enabled ? "" : L.T("  (выключен)")));
				foreach (PathMap m in p.Maps) sb.AppendLine("     " + m.Local + "  ->  " + m.Remote);
			}
			string cur = Npp.CurrentPath();
			Resolved r = guard.Resolve(cur);
			sb.AppendLine();
			sb.AppendLine(L.T("Текущий файл: ") + cur);
			sb.AppendLine(r == null ? L.T("  не относится ни к одному профилю (добавьте соответствие папок)") : L.F("  профиль {0}, на сервере {1}", r.Profile.Name, r.Remote));
			MessageBox.Show(new NppWindow(), sb.ToString(), "FTP Sync");
		}

		// ---------------------------------------------------------------- notifications
		static void OnNotify(IntPtr scn)
		{
			try
			{
				uint code = (uint)Marshal.ReadInt32(scn, 2 * IntPtr.Size);
				IntPtr from = Marshal.ReadIntPtr(scn, 0);
				switch (code)
				{
					case Npp.NPPN_READY:
						ready = true;
						if (settings.ShowExplorerOnStart) ui.BeginInvoke(new Action(ShowExplorer));
						break;
					case Npp.NPPN_TBMODIFICATION: AddToolbar(); break;
					case Npp.NPPN_FILEOPENED:
					case Npp.NPPN_BUFFERACTIVATED:
						if (ready && (code == Npp.NPPN_FILEOPENED || settings.CheckOnActivate)) OnActivated(Npp.PathFromBufferId(from));
						break;
					case Npp.NPPN_FILEBEFORESAVE: OnBeforeSave(from); break;
					case Npp.NPPN_FILESAVED: OnSaved(from); break;
					case Npp.NPPN_SHUTDOWN: guard.SaveState(); if (timer != null) timer.Stop(); break;
				}
			}
			catch (Exception ex) { Log(ex); }
		}

		static void AddToolbar()
		{
			try
			{
				IntPtr funcs = Marshal.ReadIntPtr(hostTable, 6 * IntPtr.Size + 520 + 16 * 128);
				int stride = Marshal.ReadInt32(hostTable, 7 * IntPtr.Size + 520 + 16 * 128);
				int off = Marshal.ReadInt32(hostTable, 7 * IntPtr.Size + 520 + 16 * 128 + 4);
				int cmdId = Marshal.ReadInt32(funcs, 0 * stride + off);
				using (System.Drawing.Bitmap b = new System.Drawing.Bitmap(16, 16))
				{
					using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(b))
					{
						g.Clear(System.Drawing.Color.Fuchsia);
						g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
						g.FillRectangle(System.Drawing.Brushes.SteelBlue, 2, 2, 12, 5);
						g.FillRectangle(System.Drawing.Brushes.LightSteelBlue, 2, 8, 12, 5);
						g.FillEllipse(System.Drawing.Brushes.LimeGreen, 10, 3, 4, 4);
					}
					Npp.AddToolbarIcon(cmdId, b);
				}
			}
			catch (Exception ex) { Log(ex); }
		}

		static void OnActivated(string path)
		{
			if (string.IsNullOrEmpty(path) || guard.Resolve(path) == null) return;
			DateTime t;
			if (lastCheck.TryGetValue(path, out t) && (DateTime.Now - t).TotalSeconds < 15) return;
			ui.BeginInvoke(new Action(delegate { if (Npp.CurrentPath() == path) CheckNow(path, false); }));
		}

		static bool ForegroundIsNpp()
		{
			IntPtr fg = GetForegroundWindow();
			uint pid; GetWindowThreadProcessId(fg, out pid);
			return pid == ownPid;
		}
		[DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
		[DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

		static void OnTick(object s, EventArgs e)
		{
			try
			{
				if (!ready) return;
				tick++;
				bool fg = ForegroundIsNpp();
				bool comeback = fg && !wasForeground;
				wasForeground = fg;
				if (!fg) return;
				CatchMissedSave();
				bool pollDue = settings.CheckOnTimer && settings.PollSeconds > 0 && tick % settings.PollSeconds == 0;
				if (!(comeback && settings.CheckOnActivate) && !pollDue) return;
				string path = Npp.CurrentPath();
				if (!string.IsNullOrEmpty(path) && guard.Resolve(path) != null) CheckNow(path, false);
			}
			catch (Exception ex) { Log(ex); }
		}

		/// <summary>Safety net: if the file on disk changed (saved) and Notepad++ did not notify us, upload it anyway.</summary>
		static void CatchMissedSave()
		{
			string path = Npp.CurrentPath();
			if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
			Resolved r = guard.Resolve(path);
			if (r == null || !r.Profile.AutoUpload) return;
			DateTime wt = File.GetLastWriteTimeUtc(path), prev;
			if (!lastWrite.TryGetValue(path, out prev)) { lastWrite[path] = wt; return; }
			if (wt == prev || Npp.IsModified()) return;
			lastWrite[path] = wt;
			EventLog.Upload(L.T("Сохранение"), L.F("Изменение файла обнаружено без уведомления: {0} - выкладываю", r.Remote));
			Explorer.UploadSaved(path);
		}

		// ---------------------------------------------------------------- checking
		static byte[] LocalBytes(string path)
		{
			if (string.Equals(path, Npp.CurrentPath(), StringComparison.OrdinalIgnoreCase)) return Npp.GetText();
			return File.ReadAllBytes(path);
		}

		public static void CheckNow(string path, bool interactive, Action<CheckResult> after = null)
		{
			if (string.IsNullOrEmpty(path)) return;
			if (guard.Resolve(path) == null)
			{
				if (interactive) MessageBox.Show(new NppWindow(), L.T("Файл не относится ни к одному профилю.\nДобавьте соответствие папок в «Профили и настройки» (см. «О плагине / диагностика»)."), "FTP Sync");
				return;
			}
			if (!interactive && notices.ContainsKey(path)) return;
			lock (busy) { if (!busy.Add(path)) return; }
			byte[] local;
			try { local = LocalBytes(path); } catch (Exception ex) { lock (busy) busy.Remove(path); Log(ex); return; }
			lastCheck[path] = DateTime.Now;
			ThreadPool.QueueUserWorkItem(delegate
			{
				CheckResult res;
				try { res = guard.Check(path, local); }
				catch (Exception ex) { res = new CheckResult { Status = SyncStatus.Error, Message = ex.Message, Target = guard.Resolve(path) }; }
				lock (busy) busy.Remove(path);
				ui.BeginInvoke(new Action(delegate { try { HandleResult(path, res, interactive, after); } catch (Exception ex) { Log(ex); } }));
			});
		}

		static void HandleResult(string path, CheckResult res, bool interactive, Action<CheckResult> after)
		{
			lastStatus[path] = res;
			if (panel != null && panelRegistered) panel.SetStatus(path, res);
			if (after != null) { after(res); return; }
			IWin32Window w = new NppWindow();
			switch (res.Status)
			{
				case SyncStatus.Error: EventLog.Warn(L.T("Проверка"), L.F("{0}: не удалось проверить сервер: {1}", res.Target.Remote, res.Message)); if (interactive) MessageBox.Show(w, L.T("Не удалось проверить сервер:\n") + res.Message, "FTP Sync", MessageBoxButtons.OK, MessageBoxIcon.Warning); break;
				case SyncStatus.InSync: if (interactive) MessageBox.Show(w, L.T("Файл совпадает с серверным."), "FTP Sync"); break;
				case SyncStatus.LocalAhead: if (interactive) MessageBox.Show(w, L.T("На сервере файл не менялся. Ваши правки ещё не выложены."), "FTP Sync"); break;
				case SyncStatus.RemoteMissing: if (interactive) MessageBox.Show(w, L.T("Такого файла на сервере нет:\n") + res.Target.Remote, "FTP Sync"); break;
				case SyncStatus.RemoteChanged:
				case SyncStatus.Conflict:
				case SyncStatus.NoBaseline:
					EventLog.Warn(L.T("Проверка"), res.Target.Remote + ": " + (res.Status == SyncStatus.Conflict ? L.T("конфликт (изменён на сервере и у вас)") : res.Status == SyncStatus.RemoteChanged ? L.T("изменён на сервере") : L.T("отличается от сервера, база неизвестна")) + (res.Ignored ? L.T(" (игнорируется)") : ""));
					if (!res.Ignored || interactive) ShowNotice(path, res);
					break;
			}
		}

		static void ShowNotice(string path, CheckResult res)
		{
			if (notices.ContainsKey(path)) return;
			string localText = TextUtil.Decode(LocalBytes(path));
			bool dirty = string.Equals(path, Npp.CurrentPath(), StringComparison.OrdinalIgnoreCase) && Npp.IsModified();
			ConflictForm f = new ConflictForm(res, localText, false, dirty);
			notices[path] = f;
			f.FormClosed += delegate
			{
				notices.Remove(path);
				try { ApplyChoice(path, res, f.Result, localText); } catch (Exception ex) { Log(ex); MessageBox.Show(new NppWindow(), ex.Message, "FTP Sync"); }
			};
			f.Show(new NppWindow());
		}

		static void EnsureCurrent(string path)
		{
			if (!string.Equals(path, Npp.CurrentPath(), StringComparison.OrdinalIgnoreCase)) Npp.Switch(path);
		}

		static void ApplyChoice(string path, CheckResult res, Choice c, string localText)
		{
			Resolved r = res.Target;
			string remoteText = TextUtil.Decode(res.Remote);
			switch (c)
			{
				case Choice.Ignore: guard.Ignore(res); break;
				case Choice.UseServer:
					EnsureCurrent(path);
					if (res.Status == SyncStatus.RemoteChanged && !Npp.IsModified())
					{
						File.WriteAllBytes(path, res.Remote);
						Npp.Reload(path);
					}
					else
					{
						guard.BackupLocal(r, Encoding.UTF8.GetBytes(localText), "discarded");
						Npp.SetText(remoteText);
					}
					guard.AcceptRemote(r, res.Remote);
					break;
				case Choice.Merge:
					if (res.BaseText == null) return;
					Diff.MergeResult m = Diff.Merge3(res.BaseText, localText, remoteText, L.T("ВАШЕ"), L.T("СЕРВЕР"));
					guard.BackupLocal(r, Encoding.UTF8.GetBytes(localText), "pre-merge");
					if (m.Clean)
					{
						EnsureCurrent(path);
						Npp.SetText(m.Text);
						guard.AcceptRemote(r, res.Remote);
					}
					else
					{
						Npp.NewTab(m.Text);
						guard.Ignore(res);
						MessageBox.Show(new NppWindow(), L.F("Есть конфликты ({0}). Результат слияния с маркерами открыт в новой вкладке, исходный файл не тронут.", m.Conflicts), "FTP Sync");
					}
					break;
			}
		}

		// ---------------------------------------------------------------- saving
		static void OnBeforeSave(IntPtr bufferId)
		{
			if (!settings.CheckOnSave) return;
			string path = Npp.PathFromBufferId(bufferId);
			Resolved r = guard.Resolve(path);
			if (r == null || Npp.CurrentBufferId() != bufferId) return;
			byte[] local = Npp.GetText();
			CheckResult res = guard.Check(path, local);
			lastStatus[path] = res;
			if (panel != null && panelRegistered) panel.SetStatus(path, res);
			bool warn = (res.Status == SyncStatus.RemoteChanged || res.Status == SyncStatus.Conflict || res.Status == SyncStatus.NoBaseline) && !res.Ignored;
			if (!warn) { guard.BackupLocal(r, local, "saved"); return; }

			string localText = TextUtil.Decode(local);
			ConflictForm f = new ConflictForm(res, localText, true, true);
			f.ShowDialog(new NppWindow());
			string remoteText = TextUtil.Decode(res.Remote);
			switch (f.Result)
			{
				case Choice.OverwriteServer:
					guard.BackupRemote(r, res.Remote, "overwritten");
					guard.BackupLocal(r, local, "saved");
					guard.Ignore(res);
					break;
				case Choice.Merge:
					{
						Diff.MergeResult m = Diff.Merge3(res.BaseText ?? "", localText, remoteText, L.T("ВАШЕ"), L.T("СЕРВЕР"));
						guard.BackupLocal(r, local, "pre-merge");
						if (m.Clean) { Npp.SetText(m.Text); guard.AcceptRemote(r, res.Remote); }
						else
						{
							Npp.SetText(remoteText); guard.AcceptRemote(r, res.Remote);
							string mt = m.Text;
							afterSave.Add(delegate { Npp.NewTab(mt); });
						}
						break;
					}
				default:
					guard.BackupLocal(r, local, "discarded");
					Npp.SetText(remoteText);
					guard.AcceptRemote(r, res.Remote);
					afterSave.Add(delegate { Npp.NewTab(localText); });
					break;
			}
		}

		static void OnSaved(IntPtr bufferId)
		{
			string path = Npp.PathFromBufferId(bufferId);
			if (afterSave.Count > 0)
			{
				List<Action> a = new List<Action>(afterSave); afterSave.Clear();
				ui.BeginInvoke(new Action(delegate { foreach (Action x in a) x(); Npp.Switch(path); }));
			}
			Resolved sr = guard.Resolve(path);
			if (sr == null) { Explorer.Log(L.F("Сохранён {0} - не относится ни к одному профилю, не выкладываю (проверьте соответствие папок в настройках)", path)); return; }
			if (sr.Profile.AutoUpload) { NoteWrite(path); EventLog.Upload(L.T("Сохранение"), L.F("Сохранён {0} - выкладываю на сервер", sr.Remote)); Explorer.UploadSaved(path); return; }
			Explorer.Log(L.F("Сохранён {0} - автовыкладка в профиле выключена", sr.Remote));
			foreach (int delay in new[] { 4000, 15000 })
			{
				System.Windows.Forms.Timer t = new System.Windows.Forms.Timer { Interval = delay };
				t.Tick += delegate { t.Stop(); t.Dispose(); if (Npp.CurrentPath() == path) CheckNow(path, false); };
				t.Start();
			}
		}

		// ---------------------------------------------------------------- features
		public static void ShowDiffFor(string path)
		{
			CheckNow(path, true, delegate (CheckResult res)
			{
				if (res.Remote == null) { MessageBox.Show(new NppWindow(), res.Message ?? L.T("Нет серверной версии."), "FTP Sync"); return; }
				new DiffForm(L.T("FTP Sync - различия"), L.T("- только на сервере, + только у вас\n") + res.Target.Remote,
					TextUtil.Decode(res.Remote), TextUtil.Decode(LocalBytes(path))).Show(new NppWindow());
			});
		}

		static void UploadCurrent()
		{
			string path = Npp.CurrentPath();
			Resolved r = guard.Resolve(path);
			if (r == null) { MessageBox.Show(new NppWindow(), L.T("Файл не относится ни к одному профилю."), "FTP Sync"); return; }
			if (Npp.IsModified()) { MessageBox.Show(new NppWindow(), L.T("Сначала сохраните файл (Ctrl+S)."), "FTP Sync"); return; }
			CheckNow(path, true, delegate (CheckResult res)
			{
				IWin32Window w = new NppWindow();
				if (res.Status == SyncStatus.Error) { MessageBox.Show(w, res.Message, "FTP Sync"); return; }
				if (res.Status == SyncStatus.InSync) { MessageBox.Show(w, L.T("Файл уже совпадает с серверным."), "FTP Sync"); return; }
				bool warn = res.Status == SyncStatus.RemoteChanged || res.Status == SyncStatus.Conflict || res.Status == SyncStatus.NoBaseline;
				if (warn && MessageBox.Show(w, L.T("На сервере версия отличается от вашей базы. Перезаписать её?\n(серверная версия будет сохранена в бекапы)"), "FTP Sync", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
				Explorer.UploadLocal(r, path);
			});
		}

		static Profile ProfileByFolder(string folderName)
		{
			foreach (Profile p in settings.Profiles) if (string.Equals(BackupStore.SafeSegment(p.Name), folderName, StringComparison.OrdinalIgnoreCase)) return p;
			return null;
		}

		public static string LocalPathFor(Profile p, string remote)
		{
			bool hasRoot = false;
			foreach (PathMap m in p.Maps) if ((m.Remote ?? "/").Trim('/').Length == 0 && !string.IsNullOrEmpty(m.Local)) hasRoot = true;
			if (!hasRoot)
			{
				p.Maps.Add(new PathMap(Path.Combine(Path.Combine(dir, "Cache"), BackupStore.SafeSegment(p.Name)), "/"));
				SettingsStore.Save(settings);
			}
			PathMap best = null;
			foreach (PathMap m in p.Maps)
			{
				string rr = (m.Remote ?? "/").TrimEnd('/');
				if ((remote.StartsWith(rr + "/", StringComparison.Ordinal) || rr.Length == 0) && (best == null || rr.Length > best.Remote.TrimEnd('/').Length)) best = m;
			}
			if (best == null) return null;
			string rel = remote.Substring(best.Remote.TrimEnd('/').Length).TrimStart('/').Replace('/', '\\');
			return Path.Combine(best.Local, rel);
		}

		public static void OpenBackupFolder()
		{
			Directory.CreateDirectory(settings.BackupRoot);
			Process.Start("explorer.exe", "\"" + settings.BackupRoot + "\"");
		}

		public static void OpenFolder(string path) { if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\""); }

		/// <summary>Clears backups under dir after a confirmation. olderThanDays 0 = any age; keepLast = newest versions of each file to keep.</summary>
		public static bool ClearBackups(string dir, string what, int olderThanDays, int keepLast)
		{
			if (MessageBox.Show(new NppWindow(), L.F("Очистить бекапы: {0}?\nПапка: {1}\n\nЭто действие нельзя отменить.", what, dir),
				"FTP Sync", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return false;
			int n; long bytes;
			Backups.Clear(dir, olderThanDays, keepLast, out n, out bytes);
			EventLog.Info(L.T("Бекапы"), L.F("Бекапы очищены: файлов {0}, освобождено {1}", n, TextUtil.FormatSize(bytes)));
			MessageBox.Show(new NppWindow(), L.F("Бекапы очищены: файлов {0}, освобождено {1}", n, TextUtil.FormatSize(bytes)), "FTP Sync");
			return true;
		}

		public static void Reveal(string path) { Process.Start("explorer.exe", "/select,\"" + path + "\""); }

		public static void OpenVersion(string versionPath)
		{
			string origName = Path.GetFileName(Path.GetDirectoryName(versionPath));
			string tmp = Path.Combine(Path.GetTempPath(), "FtpSync");
			Directory.CreateDirectory(tmp);
			string copy = Path.Combine(tmp, Path.GetFileNameWithoutExtension(versionPath) + "_" + origName);
			File.Copy(versionPath, copy, true);
			File.SetAttributes(copy, FileAttributes.ReadOnly);
			Npp.Open(copy);
		}

		static IWin32Window Owner { get { return new NppWindow(); } }

		public static void CompareWithServer(NodeInfo n)
		{
			Profile p = ProfileByFolder(n.Profile);
			if (p == null) { MessageBox.Show(Owner, L.T("Профиль не найден в настройках."), "FTP Sync"); return; }
			Resolved r = new Resolved { Profile = p, Remote = n.Remote };
			ThreadPool.QueueUserWorkItem(delegate
			{
				byte[] data = null; string err = null;
				try { data = guard.FetchRemote(r); } catch (Exception ex) { err = ex.Message; }
				ui.BeginInvoke(new Action(delegate
				{
					if (err != null || data == null) { MessageBox.Show(Owner, err ?? L.T("На сервере файла нет."), "FTP Sync"); return; }
					new DiffForm(L.T("FTP Sync - бекап и сервер"), L.T("- только в бекапе, + только на сервере\n") + Path.GetFileName(n.Path),
						TextUtil.Decode(File.ReadAllBytes(n.Path)), TextUtil.Decode(data)).Show(Owner);
				}));
			});
		}

		public static void CompareWithEditor(NodeInfo n)
		{
			new DiffForm(L.T("FTP Sync - бекап и редактор"), L.T("- только в бекапе, + только в открытом файле\n") + Path.GetFileName(n.Path),
				TextUtil.Decode(File.ReadAllBytes(n.Path)), TextUtil.Decode(Npp.GetText())).Show(Owner);
		}

		public static void RestoreToEditor(NodeInfo n)
		{
			Profile p = ProfileByFolder(n.Profile);
			string target = p == null ? null : LocalPathFor(p, n.Remote);
			if (target == null || !File.Exists(target)) { MessageBox.Show(Owner, L.T("Не нашёл локальный файл для этой версии. Откройте нужный файл в редакторе и используйте «Открыть копию» / «Сравнить»."), "FTP Sync"); return; }
			if (MessageBox.Show(Owner, L.F("Заменить текст в редакторе версией из бекапа?\n{0}\n(текущий текст будет сохранён в бекапы, действие отменяется Ctrl+Z)", target), "FTP Sync", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
			Npp.Open(target); Npp.Switch(target);
			Resolved r = guard.Resolve(target);
			if (r != null) guard.BackupLocal(r, Npp.GetText(), "pre-restore");
			Npp.SetText(TextUtil.Decode(File.ReadAllBytes(n.Path)));
		}

		public static void UploadVersion(NodeInfo n)
		{
			Profile p = ProfileByFolder(n.Profile);
			if (p == null) { MessageBox.Show(Owner, L.T("Профиль не найден в настройках."), "FTP Sync"); return; }
			if (MessageBox.Show(Owner, L.F("Выложить эту версию на сервер?\n{0}  {1}\n\nТекущая серверная версия сначала попадёт в бекапы.", p.Name, n.Remote), "FTP Sync", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
			Resolved r = new Resolved { Profile = p, Remote = n.Remote };
			byte[] data = File.ReadAllBytes(n.Path);
			ThreadPool.QueueUserWorkItem(delegate
			{
				string err = null;
				try { guard.UploadFile(r, data); } catch (Exception ex) { err = ex.Message; }
				ui.BeginInvoke(new Action(delegate
				{
					if (err == null)
					{
						string lp = LocalPathFor(p, n.Remote);
						if (lp != null && File.Exists(lp))
						{
							try { File.WriteAllBytes(lp, data); if (string.Equals(lp, Npp.CurrentPath(), StringComparison.OrdinalIgnoreCase)) Npp.Reload(lp); } catch (IOException) { }
						}
						if (panel != null) panel.RebuildTree();
					}
					MessageBox.Show(Owner, err ?? L.T("Выложено."), "FTP Sync");
				}));
			});
		}

		public static void SelectBackupsForCurrent(PanelForm pf)
		{
			if (pf == null) return;
			Resolved r = guard.Resolve(Npp.CurrentPath());
			if (r == null) { MessageBox.Show(Owner, L.T("Текущий файл не относится ни к одному профилю."), "FTP Sync"); return; }
			if (!pf.SelectRemote(r.Profile.Name, r.Remote)) MessageBox.Show(Owner, L.T("Бекапов этого файла пока нет."), "FTP Sync");
		}
	}
}
