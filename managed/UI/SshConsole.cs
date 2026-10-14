using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FtpSync
{
	/// <summary>SSH console tab: terminal on the left, command cheat sheet (search, favourites, recent) on the right.</summary>
	public class SshConsole : UserControl
	{
		readonly Profile profile;
		readonly Action<SshConsole> closeMe;
		readonly TerminalControl term = new TerminalControl();
		readonly SplitContainer split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
		readonly ToolStrip bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System };
		readonly ToolStripLabel state = new ToolStripLabel { Alignment = ToolStripItemAlignment.Right };
		readonly ToolStripButton bHelp, bHl;
		readonly TextBox search = new TextBox { Dock = DockStyle.Top };
		readonly ListView list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, ShowGroups = true, HeaderStyle = ColumnHeaderStyle.None };
		readonly Label detail = new Label { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(4, 4, 4, 0) };
		readonly ToolStripButton mAll = new ToolStripButton { CheckOnClick = false }, mFav = new ToolStripButton { CheckOnClick = false }, mRec = new ToolStripButton { CheckOnClick = false };
		readonly List<HelpCommand> all = CommandHelp.All();
		SshSession session;
		int mode; // 0 all, 1 favourites, 2 recent
		bool dead;

		public Profile Profile { get { return profile; } }
		public string TabTitle { get { return "SSH: " + profile.Name; } }

		public SshConsole(Profile p, Action<SshConsole> close)
		{
			profile = p; closeMe = close;
			Dock = DockStyle.Fill;
			bar.Font = new Font("Segoe UI", 9f);
			bHelp = new ToolStripButton(L.T("Команды")) { Checked = true, CheckOnClick = true, ToolTipText = L.T("Справочник команд: поиск, избранное, недавние") };
			bHelp.CheckedChanged += delegate { split.Panel2Collapsed = !bHelp.Checked; };
			bHl = new ToolStripButton(L.T("Подсветка")) { Checked = true, CheckOnClick = true, ToolTipText = L.T("Подсветка синтаксиса команд, ошибок, адресов") };
			bHl.CheckedChanged += delegate { term.Highlight = bHl.Checked; term.Invalidate(); };
			ToolStripButton bClear = new ToolStripButton(L.T("Очистить")) { ToolTipText = L.T("Очистить историю прокрутки") };
			bClear.Click += delegate { term.Screen.ClearScrollback(); term.Invalidate(); term.Focus(); };
			ToolStripButton bRe = new ToolStripButton(L.T("Переподключить"));
			bRe.Click += delegate { Reconnect(); };
			ToolStripButton bClose = new ToolStripButton(L.T("Закрыть"));
			bClose.Click += delegate { closeMe(this); };
			bar.Items.AddRange(new ToolStripItem[] { bHelp, bHl, bClear, bRe, bClose, state });

			split.Panel1.Controls.Add(term); term.Dock = DockStyle.Fill;
			BuildHelp();
			split.Panel2.Controls.Add(list); split.Panel2.Controls.Add(detail); split.Panel2.Controls.Add(search);
			Controls.Add(split); Controls.Add(bar);

			term.UserInput += delegate (byte[] b) { SshSession s = session; if (s != null) s.Write(b); };
			term.GridResized += delegate (int c, int r) { SshSession s = session; if (s != null) s.Resize(c, r); };
			term.EnterPressed += OnEnter;
			term.AddFavorite += delegate (string t) { AddFav(t); };
			term.Screen.Reply += delegate (string r) { SshSession s = session; if (s != null) s.Write(r); };
			HandleCreated += delegate { split.SplitterDistance = Math.Max(200, split.Width - 380); };
			StartOnHandle();
		}

		void StartOnHandle()
		{
			HandleCreated += delegate { BeginInvoke(new Action(Connect)); };
		}

		// ------------------------------------------------------------------ session
		void SetState(string text, Color c) { state.Text = text; state.ForeColor = c; }

		void Connect()
		{
			if (RemoteFactory.NeedsPassword(profile))
			{
				string pw = PromptForm.Ask(new NppWindow(), "FTP Sync", L.T("Пароль для ") + profile.User + "@" + profile.Host, "", true);
				if (pw == null) { SetState(L.T("не подключено"), Color.Gray); WriteNote(L.T("Подключение отменено.")); return; }
				RemoteFactory.SetPassword(profile, pw);
			}
			dead = false;
			SetState(L.T("подключение…"), Color.DarkOrange);
			WriteNote(L.T("Консоль — вспомогательный инструмент. Для полноценной работы по SSH используйте PuTTY, Termius и подобные программы."));
			WriteNote(L.T("Подключение к ") + profile.User + "@" + profile.Host + ":" + profile.Port + " …");
			int cols = term.Cols, rows = term.Rows;
			SshSession s = new SshSession(profile);
			session = s;
			s.Received += delegate (byte[] b, int n) { term.Screen.Feed(b, n); };
			s.Ended += delegate (string why) { Ui(delegate { if (session != s) return; dead = true; SetState(L.T("отключено"), Color.Firebrick); if (why != null) WriteNote("\r\n" + L.T("Соединение закрыто") + (why.Length > 0 ? ": " + why : "") + L.T(". Нажмите «Переподключить».")); }); };
			ThreadPool.QueueUserWorkItem(delegate
			{
				try
				{
					s.Open(cols, rows);
					Ui(delegate { SetState(profile.User + "@" + profile.Host, Color.SeaGreen); term.Focus(); });
					EventLog.Info(L.T("Консоль"), L.T("Консоль SSH открыта: ") + profile.Name);
				}
				catch (Exception ex)
				{
					EventLog.Error(L.T("Консоль"), profile.Name + ": " + ex.Message, ex);
					Ui(delegate { dead = true; SetState(L.T("ошибка"), Color.Firebrick); WriteNote("\r\n" + L.T("Не удалось подключиться: ") + ex.Message); });
				}
			});
		}

		void Reconnect()
		{
			SshSession old = session; session = null;
			if (old != null) old.Close();
			term.Screen.Feed(Encoding.UTF8.GetBytes("\r\n"), 2);
			Connect();
		}

		void WriteNote(string text)
		{
			byte[] b = Encoding.UTF8.GetBytes("\x1b[90m" + text + "\x1b[0m\r\n");
			term.Screen.Feed(b, b.Length);
		}

		void Ui(Action a) { if (IsHandleCreated && !IsDisposed) { try { BeginInvoke(a); } catch (InvalidOperationException) { } } }

		public void CloseSession()
		{
			SshSession s = session; session = null;
			if (s != null) s.Close();
		}

		protected override void Dispose(bool disposing) { if (disposing) CloseSession(); base.Dispose(disposing); }

		// ------------------------------------------------------------------ recent / favourites
		AppSettings Cfg { get { return Plugin.Settings; } }

		void OnEnter()
		{
			string line;
			lock (term.Screen.Sync) { if (term.Screen.Alt) return; line = term.Screen.LogicalLineAtScreenRow(term.Screen.CY); }
			string cmd = ShellHighlight.CommandAfterPrompt(line);
			if (cmd == null || cmd.Length > 400) return;
			CommandHelp.Touch(Cfg.RecentCommands, cmd, 60);
			Plugin.SaveSettings();
			if (mode == 2) Fill();
		}

		void AddFav(string cmd)
		{
			cmd = (cmd ?? "").Trim();
			if (cmd.Length == 0 || Cfg.FavoriteCommands.Contains(cmd)) return;
			Cfg.FavoriteCommands.Add(cmd); Plugin.SaveSettings();
			if (mode == 1) Fill();
		}

		void RemoveFav(string cmd) { Cfg.FavoriteCommands.Remove(cmd); Plugin.SaveSettings(); if (mode == 1) Fill(); }

		// ------------------------------------------------------------------ help pane
		void BuildHelp()
		{
			ToolStrip modes = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System, Dock = DockStyle.Top, Font = new Font("Segoe UI", 9f) };
			mAll.Text = L.T("Все"); mFav.Text = L.T("Избранное"); mRec.Text = L.T("Недавние");
			mAll.Click += delegate { SetMode(0); }; mFav.Click += delegate { SetMode(1); }; mRec.Click += delegate { SetMode(2); };
			modes.Items.AddRange(new ToolStripItem[] { mAll, mFav, mRec });
			split.Panel2.Controls.Add(modes);
			search.Font = new Font("Segoe UI", 9f);
			search.TextChanged += delegate { Fill(); };
			search.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Down && list.Items.Count > 0) { list.Focus(); list.Items[0].Selected = true; e.Handled = true; } };
			Label hint = new Label { Text = L.T("Поиск по командам и описаниям:"), Dock = DockStyle.Top, Height = 20, Padding = new Padding(4, 4, 0, 0), Font = new Font("Segoe UI", 8.5f) };
			split.Panel2.Controls.Add(hint);
			list.Font = new Font("Segoe UI", 9f);
			list.Columns.Add("cmd", 170); list.Columns.Add("desc", 200);
			list.SelectedIndexChanged += delegate { ShowDetail(); };
			list.DoubleClick += delegate { Insert(false); };
			list.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { Insert(e.Control); e.Handled = true; e.SuppressKeyPress = true; } };
			list.Resize += delegate { if (list.Columns.Count == 2) list.Columns[1].Width = Math.Max(100, list.ClientSize.Width - list.Columns[0].Width - 4); };
			detail.Font = new Font("Segoe UI", 8.5f);
			ContextMenuStrip m = new ContextMenuStrip();
			ToolStripItem iIns = m.Items.Add(L.T("Вставить в консоль"), null, delegate { Insert(false); });
			ToolStripItem iRun = m.Items.Add(L.T("Вставить и выполнить"), null, delegate { Insert(true); });
			ToolStripItem iFav = m.Items.Add(L.T("В избранное"), null, delegate { string c = Current(); if (c != null) AddFav(c); });
			ToolStripItem iUnfav = m.Items.Add(L.T("Убрать из избранного"), null, delegate { string c = Current(); if (c != null) RemoveFav(c); });
			ToolStripItem iCopy = m.Items.Add(L.T("Копировать"), null, delegate { string c = Current(); if (c != null) { try { Clipboard.SetText(c); } catch (Exception) { } } });
			m.Items.Add(new ToolStripSeparator());
			m.Items.Add(L.T("Добавить свою команду…"), null, delegate { string c = PromptForm.Ask(new NppWindow(), L.T("Избранная команда"), L.T("Команда:"), "", false); if (!string.IsNullOrEmpty(c)) AddFav(c); });
			ToolStripItem iClr = m.Items.Add(L.T("Очистить недавние"), null, delegate { Cfg.RecentCommands.Clear(); Plugin.SaveSettings(); Fill(); });
			m.Opening += delegate
			{
				string c = Current(); bool has = c != null;
				bool fav = has && Cfg.FavoriteCommands.Contains(c);
				iIns.Enabled = iRun.Enabled = iCopy.Enabled = has; iFav.Visible = has && !fav; iUnfav.Visible = fav; iClr.Visible = mode == 2;
			};
			list.ContextMenuStrip = m;
			SetMode(0);
		}

		void SetMode(int m)
		{
			mode = m; mAll.Checked = m == 0; mFav.Checked = m == 1; mRec.Checked = m == 2;
			list.ShowGroups = m == 0;
			Fill();
		}

		string Current() { return list.SelectedItems.Count == 0 ? null : (string)list.SelectedItems[0].Tag; }

		void Fill()
		{
			string q = search.Text.Trim();
			list.BeginUpdate();
			list.Items.Clear(); list.Groups.Clear();
			Dictionary<string, ListViewGroup> groups = new Dictionary<string, ListViewGroup>();
			if (mode == 0)
			{
				foreach (HelpCommand c in all)
				{
					if (!CommandHelp.Matches(c, q)) continue;
					ListViewGroup g;
					if (!groups.TryGetValue(c.Category, out g)) { g = new ListViewGroup(c.Category); groups[c.Category] = g; list.Groups.Add(g); }
					ListViewItem it = new ListViewItem(new[] { c.Command, c.Description }) { Tag = c.Command, Group = g };
					list.Items.Add(it);
				}
			}
			else
			{
				List<string> src = mode == 1 ? Cfg.FavoriteCommands : Cfg.RecentCommands;
				foreach (string cmd in src)
				{
					HelpCommand hc = CommandHelp.Find(all, cmd);
					if (q.Length > 0 && !(hc != null ? CommandHelp.Matches(hc, q) : cmd.ToLowerInvariant().Contains(q.ToLowerInvariant()))) continue;
					list.Items.Add(new ListViewItem(new[] { cmd, hc != null ? hc.Description : "" }) { Tag = cmd });
				}
			}
			list.EndUpdate();
			ShowDetail();
		}

		void ShowDetail()
		{
			string c = Current();
			if (c == null) { detail.Text = list.Items.Count == 0 ? L.T("Ничего не найдено.") : L.T("Двойной щелчок — вставить в консоль. Enter — вставить, Ctrl+Enter — выполнить."); return; }
			HelpCommand hc = CommandHelp.Find(all, c);
			detail.Text = c + (hc != null ? "\n" + hc.Description : "");
		}

		void Insert(bool run)
		{
			string c = Current(); if (c == null) return;
			term.Type(c + (run ? "\r" : ""));
			if (run) { CommandHelp.Touch(Cfg.RecentCommands, c, 60); Plugin.SaveSettings(); if (mode == 2) Fill(); }
		}
	}
}
