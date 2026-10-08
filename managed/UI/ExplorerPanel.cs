using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FtpSync
{
	/// <summary>TreeView that counts user-driven scrolling (scrollbar, wheel, keys) so automatic scrolling can step back.</summary>
	public class ScrollTree : TreeView
	{
		public int Stamp;
		int quiet;

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

		protected override void WndProc(ref Message m)
		{
			// WM_HSCROLL, WM_VSCROLL, WM_MOUSEWHEEL, WM_KEYDOWN = the user is scrolling
			if (quiet == 0 && (m.Msg == 0x114 || m.Msg == 0x115 || m.Msg == 0x20A || m.Msg == 0x100)) Stamp++;
			base.WndProc(ref m);
		}

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		static extern int GetScrollPos(IntPtr h, int bar);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		static extern bool GetScrollRange(IntPtr h, int bar, out int min, out int max);

		/// <summary>Programmatic horizontal scroll that puts the node's label in the middle of the window.</summary>
		public void CenterHorizontally(TreeNode n)
		{
			if (!IsHandleCreated || n == null) return;
			Rectangle b = n.Bounds;
			int cur = GetScrollPos(Handle, 0), min, max;
			GetScrollRange(Handle, 0, out min, out max);
			int target = cur + b.Left + b.Width / 2 - ClientSize.Width / 2;
			if (b.Width > ClientSize.Width - 8) target = cur + b.Left - 4; // wider than the window: show its start
			target = Math.Max(min, Math.Min(max, target));
			if (target == cur) return;
			quiet++;
			try
			{
				SendMessage(Handle, 0x114, (IntPtr)(((target & 0xFFFF) << 16) | 4), IntPtr.Zero); // WM_HSCROLL, SB_THUMBPOSITION
				SendMessage(Handle, 0x114, (IntPtr)8, IntPtr.Zero);                                // SB_ENDSCROLL
			}
			finally { quiet--; }
			Rectangle nb = n.Bounds;
			if (nb.Left < 0 || nb.Right > ClientSize.Width) n.EnsureVisible();
		}
	}

	public class ExNode
	{
		public Profile Profile;
		public RemoteEntry Entry;   // null for a profile root
		public bool Chain;          // path-only node on the way to the start folder (not listed yet)
		public string Remote;
		public bool IsRoot { get { return Entry == null; } }
		public bool IsDir { get { return Entry == null || Entry.IsDir; } }
	}

	/// <summary>Docked connection tree (NppFTP style): profiles, remote folders and a one-line transfer status.</summary>
	public class ExplorerPanel : Form
	{
		readonly ToolStrip bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System };
		readonly ToolStripSplitButton bConnect = new ToolStripSplitButton();
		readonly TextBox path = new TextBox { Dock = DockStyle.Top };
		readonly ScrollTree tree = new ScrollTree { Dock = DockStyle.Fill, HideSelection = false, ShowNodeToolTips = false, AllowDrop = true, ItemHeight = 22 };
		readonly Label lastAction = new Label { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(3, 3, 3, 0), Cursor = Cursors.Hand, Font = new Font("Segoe UI Symbol", 9f) };
		readonly ProgressBar bar2 = new ProgressBar { Dock = DockStyle.Bottom, Height = 5, Minimum = 0, Maximum = 100 };
		readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 20, Padding = new Padding(3, 3, 0, 0), AutoEllipsis = true };
		ImageList icons;

		public ExplorerPanel()
		{
			Text = L.T("FTP Sync - подключения"); FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
			BuildIcons(); tree.ImageList = icons;

			bConnect.Text = "⚡"; bConnect.ToolTipText = L.T("Подключиться (стрелка: выбрать профиль)"); bConnect.ButtonClick += delegate { ConnectSelected(); };
			ToolStripButton bDisc = Btn("⏏", L.T("Отключиться"), delegate { DisconnectSelected(); });
			ToolStripButton bUp = Btn("⬆", L.T("Выложить текущий файл"), delegate { Plugin.UploadCurrentCommand(); });
			ToolStripButton bRef = Btn("⟳", L.T("Обновить"), delegate { RefreshSelected(); });
			ToolStripButton bStop = Btn("■", L.T("Остановить очередь"), delegate { Explorer.Queue.Abort(); });
			ToolStripButton bSet = Btn("⚙", L.T("Профили и настройки"), delegate { Plugin.OpenSettingsCommand(); });
			ToolStripButton bGuard = Btn("◈", L.T("Панель статуса и бекапов"), delegate { Plugin.ShowGuardPanel(0); });
			ToolStripButton bLog = Btn("≡", L.T("Полный журнал (вкладка «Журнал» внизу)"), delegate { Plugin.ShowGuardPanel(2); });
			bar.Items.AddRange(new ToolStripItem[] { bConnect, bDisc, new ToolStripSeparator(), bUp, bRef, bStop, new ToolStripSeparator(), bSet, bGuard, bLog });
			bar.Font = new Font("Segoe UI Symbol", 11f);

			path.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; GoToPath(path.Text); } };

			lastAction.Click += delegate { Plugin.ShowGuardPanel(2); };
			EventLog.Added += delegate (LogEntry en) { if (en.Source == L.T("Подключение")) Plugin.UiInvoke(delegate { ShowConnection(en); }); };
			Controls.Add(tree); Controls.Add(lastAction); Controls.Add(bar2); Controls.Add(status); Controls.Add(path); Controls.Add(bar);

			tree.BeforeExpand += OnBeforeExpand;
			tree.AfterExpand += OnAfterExpand;
			tree.AfterSelect += delegate { ShowSelection(); };
			tree.NodeMouseDoubleClick += OnDoubleClick;
			tree.NodeMouseClick += delegate (object s, TreeNodeMouseClickEventArgs e) { if (e.Button == MouseButtons.Right) tree.SelectedNode = e.Node; };
			tree.KeyDown += OnKey;
			tree.DragEnter += delegate (object s, DragEventArgs e) { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
			tree.DragOver += delegate (object s, DragEventArgs e) { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) && DropTarget(e) != null ? DragDropEffects.Copy : DragDropEffects.None; };
			tree.DragDrop += OnDrop;
			tree.ContextMenuStrip = BuildMenu();
			RebuildRoots();
		}

		ToolStripButton Btn(string text, string tip, Action a)
		{
			ToolStripButton b = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
			b.Click += delegate { try { a(); } catch (Exception ex) { EventLog.Error(L.T("Дерево"), ex.Message, ex); } };
			return b;
		}

		void BuildIcons() { icons = FileIcons.Build(); }

		// ---------------------------------------------------------------- tree
		static string Short(string s, int n) { s = (s ?? "").Replace("\r", " ").Replace("\n", " "); return s.Length <= n ? s : s.Substring(0, n - 1) + "…"; }

		void SetStatus(string text, Color c, int percent)
		{
			lastAction.Text = text; lastAction.ForeColor = c;
			if (percent >= 0) { bar2.Style = ProgressBarStyle.Continuous; bar2.Value = Math.Min(100, percent); } else bar2.Value = 0;
		}

		/// <summary>One short line: the transfer in progress (percent), done, or the error.</summary>
		void ShowTransfer(QueueItem q)
		{
			string name = RemotePath.Name(q.Remote.Contains(" → ") ? q.Remote.Substring(0, q.Remote.IndexOf(" → ")) : q.Remote);
			bool up = q.IsUpload, down = q.IsDownload;
			string icon = up ? "⬆" : down ? "⬇" : "•";
			int pct = q.Total > 0 ? (int)(q.Done * 100 / q.Total) : -1;
			switch (q.State)
			{
				case QState.Running: SetStatus(icon + " " + name + "  " + q.ProgressText, Color.FromArgb(230, 126, 0), pct); break;
				case QState.Done: SetStatus("✔ " + name + L.T(" - готово"), Color.FromArgb(27, 127, 59), 100); break;
				case QState.Cancelled: SetStatus(L.T("■ остановлено"), Color.Gray, -1); break;
				case QState.Error: SetStatus(L.F("✖ Ошибка ({0}): {1} - {2}", q.Action, name, Short(q.Error, 50)), Color.Firebrick, -1); break;
			}
		}

		void ShowConnection(LogEntry e)
		{
			if (e.Level == LogLevel.Error) SetStatus(L.T("✖ Ошибка подключения: ") + Short(e.Message, 70), Color.Firebrick, -1);
			else if (e.Level == LogLevel.Ok) SetStatus(L.T("✔ Подключено"), Color.FromArgb(27, 127, 59), -1);
			else SetStatus("… " + Short(e.Message, 50), Color.Gray, -1);
		}

		void Log(string s) { EventLog.Info(L.T("Дерево"), s); }

		public void RebuildRoots()
		{
			tree.BeginUpdate();
			tree.Nodes.Clear();
			bConnect.DropDownItems.Clear();
			Dictionary<string, TreeNode> groups = new Dictionary<string, TreeNode>();
			foreach (Profile p in Plugin.SortedProfiles())
			{
				if (!p.Enabled) continue;
				TreeNode n = new TreeNode(p.Name);
				n.Tag = new ExNode { Profile = p, Remote = "/" };
				n.Nodes.Add(new TreeNode("…"));
				bool on = Explorer.Connected.Contains(p.Name);
				n.ImageKey = n.SelectedImageKey = on ? FileIcons.ServerOn : FileIcons.Server;
				n.ToolTipText = p.Protocol + "://" + p.User + "@" + p.Host + ":" + p.Port;
				GroupNodes(groups, p.Group).Add(n);
				Profile pp = p;
				bConnect.DropDownItems.Add(p.Name, null, delegate { ConnectProfile(pp); });
			}
			tree.EndUpdate();
		}

		/// <summary>Collection of nodes for a group path "A/B" (folders are created on demand).</summary>
		TreeNodeCollection GroupNodes(Dictionary<string, TreeNode> groups, string path)
		{
			if (string.IsNullOrEmpty(path)) return tree.Nodes;
			TreeNode node = null; string cur = "";
			foreach (string seg in path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
			{
				cur = cur.Length == 0 ? seg : cur + "/" + seg;
				TreeNode g;
				if (!groups.TryGetValue(cur, out g))
				{
					g = new TreeNode(seg) { Tag = cur };
					g.ImageKey = g.SelectedImageKey = FileIcons.Folder;
					(node == null ? tree.Nodes : node.Nodes).Add(g);
					groups[cur] = g;
				}
				node = g;
			}
			return node == null ? tree.Nodes : node.Nodes;
		}

		TreeNode RootNode(Profile p)
		{
			return FindRoot(tree.Nodes, p);
		}

		static TreeNode FindRoot(TreeNodeCollection nodes, Profile p)
		{
			foreach (TreeNode n in nodes)
			{
				ExNode en = n.Tag as ExNode;
				if (en != null && en.Profile == p) return n;
				if (en == null) { TreeNode r = FindRoot(n.Nodes, p); if (r != null) return r; } // group folder
			}
			return null;
		}

		Profile FirstProfile(TreeNodeCollection nodes)
		{
			foreach (TreeNode n in nodes)
			{
				ExNode en = n.Tag as ExNode;
				if (en != null) return en.Profile;
				Profile p = FirstProfile(n.Nodes);
				if (p != null) return p;
			}
			return null;
		}

		ExNode Sel { get { return tree.SelectedNode == null ? null : tree.SelectedNode.Tag as ExNode; } }

		void ShowSelection()
		{
			ExNode n = Sel;
			if (n == null) { path.Text = ""; status.Text = ""; return; }
			path.Text = n.Remote;
			if (!n.IsRoot) Explorer.Remember(n.Profile, n.IsDir ? n.Remote : RemotePath.Parent(n.Remote));
			if (n.Entry != null) status.Text = n.Entry.Name + (n.Entry.IsDir ? "" : "  " + TextUtil.FormatSize(n.Entry.Size)) + (n.Entry.Modified.HasValue ? "  " + n.Entry.Modified.Value.ToString("yyyy-MM-dd HH:mm") : "") + "  " + n.Entry.Perms;
			else status.Text = Explorer.Connected.Contains(n.Profile.Name) ? L.T("подключено") : L.T("не подключено (двойной щелчок)");
		}

		void ConnectSelected()
		{
			ExNode n = Sel;
			if (n != null) ConnectProfile(n.Profile);
			else { Profile fp = FirstProfile(tree.Nodes); if (fp != null) ConnectProfile(fp); else Plugin.OpenSettingsCommand(); }
		}

		void DisconnectSelected()
		{
			ExNode n = Sel; if (n == null) return;
			Explorer.Disconnect(n.Profile);
			TreeNode r = RootNode(n.Profile);
			if (r != null) { r.Nodes.Clear(); r.Nodes.Add(new TreeNode("…")); r.ImageKey = r.SelectedImageKey = FileIcons.Server; r.Collapse(); }
		}

		void ConnectProfile(Profile p, Action<bool> after = null)
		{
			TreeNode root = RootNode(p);
			if (root == null) return;
			if (Explorer.Connected.Contains(p.Name)) { root.Expand(); if (after != null) after(true); return; }
			Explorer.Connect(p, delegate (bool ok, string start, List<RemoteEntry> entries)
			{
				if (!ok) { if (after != null) after(false); return; }
				root.ImageKey = root.SelectedImageKey = FileIcons.ServerOn;
				BuildChain(root, p, start, entries);
				if (after != null) after(true);
			});
		}

		TreeNode MakeDirNode(Profile p, string name, string path, bool chain)
		{
			TreeNode n = new TreeNode(name);
			n.Tag = new ExNode { Profile = p, Entry = new RemoteEntry { Name = name, Path = path, IsDir = true }, Remote = path, Chain = chain };
			n.ImageKey = n.SelectedImageKey = FileIcons.Folder;
			return n;
		}

		/// <summary>Shows only the path "/" > home > user > start folder; siblings along the way are not listed (they may be unreadable).</summary>
		void BuildChain(TreeNode root, Profile p, string start, List<RemoteEntry> entries)
		{
			tree.BeginUpdate();
			root.Nodes.Clear();
			string[] segs = start.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
			TreeNode cur = MakeDirNode(p, "/", "/", segs.Length > 0);
			root.Nodes.Add(cur);
			string path = "";
			for (int i = 0; i < segs.Length; i++)
			{
				path += "/" + segs[i];
				TreeNode c = MakeDirNode(p, segs[i], path, i < segs.Length - 1);
				cur.Nodes.Add(c); cur = c;
			}
			Populate(cur, entries, null);
			tree.EndUpdate();
			int stamp = tree.Stamp;
			suppressScroll = true;
			root.Expand();
			for (TreeNode t = cur; t != null && t != root; t = t.Parent) t.Expand();
			suppressScroll = false;
			tree.SelectedNode = cur; cur.EnsureVisible();
			if (stamp == tree.Stamp) CenterNode(cur);
		}

		readonly Dictionary<TreeNode, int> pendingScroll = new Dictionary<TreeNode, int>();
		bool suppressScroll;

		/// <summary>Puts the node in the middle of the tree window (vertically and horizontally).</summary>
		void CenterNode(TreeNode n)
		{
			if (n == null) return;
			n.EnsureVisible();
			int vis = Math.Max(1, tree.VisibleCount);
			TreeNode top = n;
			for (int i = 0; i < vis / 2 && top.PrevVisibleNode != null; i++) top = top.PrevVisibleNode;
			tree.TopNode = top;
			tree.CenterHorizontally(n);
		}

		void OnAfterExpand(object s, TreeViewEventArgs e)
		{
			int stamp;
			if (!pendingScroll.TryGetValue(e.Node, out stamp)) return;
			pendingScroll.Remove(e.Node);
			if (!suppressScroll && stamp == tree.Stamp) CenterNode(e.Node);
		}

		void OnBeforeExpand(object s, TreeViewCancelEventArgs e)
		{
			if (!pendingScroll.ContainsKey(e.Node)) pendingScroll[e.Node] = tree.Stamp;
			ExNode n = e.Node.Tag as ExNode;
			if (n == null || !n.IsDir) return;
			bool placeholder = e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Tag == null && e.Node.Nodes[0].Text == "…";
			if (!placeholder) return;
			e.Cancel = true;
			if (n.IsRoot) ConnectProfile(n.Profile);
			else LoadDir(e.Node, delegate { e.Node.Expand(); });
		}

		void LoadDir(TreeNode node, Action done)
		{
			ExNode n = (ExNode)node.Tag;
			bool hadReal = node.Nodes.Count > 0 && node.Nodes[0].Tag != null;
			if (!hadReal) { node.Nodes.Clear(); node.Nodes.Add(new TreeNode(L.T("загрузка…")) { ForeColor = Color.Gray }); }
			Explorer.List(n.Profile, n.Remote, delegate (List<RemoteEntry> list, string err)
			{
				if (err != null)
				{
					if (!hadReal) { node.Nodes.Clear(); node.Nodes.Add(new TreeNode(L.T("ошибка: ") + err) { ForeColor = Color.Firebrick }); }
				}
				else Populate(node, list, null);
				if (done != null) done();
			});
		}

		void Populate(TreeNode node, List<RemoteEntry> list, Action done)
		{
			ExNode n = (ExNode)node.Tag;
			Dictionary<string, TreeNode> keep = new Dictionary<string, TreeNode>();
			foreach (TreeNode old in node.Nodes) { ExNode on = old.Tag as ExNode; if (on != null && on.Chain) keep[on.Remote] = old; }
			node.Nodes.Clear();
			n.Chain = false;
			list.Sort(delegate (RemoteEntry a, RemoteEntry b)
			{
				if (a.IsDir != b.IsDir) return a.IsDir ? -1 : 1;
				return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
			});
			foreach (RemoteEntry e in list)
			{
				if (!n.Profile.ShowHidden && e.Name.StartsWith(".")) continue;
				TreeNode reuse;
				if (keep.TryGetValue(e.Path, out reuse)) { node.Nodes.Add(reuse); keep.Remove(e.Path); continue; }
				TreeNode c = new TreeNode(e.Name);
				c.Tag = new ExNode { Profile = n.Profile, Entry = e, Remote = e.Path };
				c.ImageKey = c.SelectedImageKey = FileIcons.KeyFor(e.Name, e.IsDir, e.IsLink);
				c.ToolTipText = e.Path + (e.IsDir ? "" : "\n" + TextUtil.FormatSize(e.Size)) + (e.Modified.HasValue ? "\n" + e.Modified.Value.ToString("yyyy-MM-dd HH:mm") : "") + "\n" + e.Perms;
				if (e.IsDir) c.Nodes.Add(new TreeNode("…"));
				node.Nodes.Add(c);
			}
			foreach (TreeNode orphan in keep.Values) node.Nodes.Add(orphan); // chain child that the listing did not show
			Explorer.Remember(n.Profile, n.Remote);
			if (done != null) done();
		}

		public void RefreshDir(Profile p, string dir)
		{
			TreeNode root = RootNode(p);
			if (root == null || !Explorer.Connected.Contains(p.Name)) return;
			TreeNode t = Find(root, dir);
			if (t != null && t.IsExpanded) LoadDir(t, delegate { t.Expand(); });
		}

		static TreeNode Find(TreeNode node, string remote)
		{
			ExNode n = (ExNode)node.Tag;
			if (string.Equals(n.Remote.TrimEnd('/'), remote.TrimEnd('/'), StringComparison.Ordinal)) return node;
			foreach (TreeNode c in node.Nodes)
			{
				ExNode cn = c.Tag as ExNode;
				if (cn == null) continue;
				string cr = cn.Remote.TrimEnd('/');
				if (remote.TrimEnd('/') == cr || remote.StartsWith(cr + "/")) { TreeNode r = Find(c, remote); if (r != null) return r; }
			}
			return null;
		}

		void RefreshSelected()
		{
			TreeNode t = tree.SelectedNode;
			if (t == null) return;
			ExNode n = t.Tag as ExNode;
			if (n == null) return;
			if (!n.IsDir) { t = t.Parent; if (t == null) return; }
			ExNode dn = t.Tag as ExNode;
			if (dn == null) return;
			if (dn.IsRoot) { if (!Explorer.Connected.Contains(dn.Profile.Name)) ConnectProfile(dn.Profile); return; }
			LoadDir(t, delegate { t.Expand(); });
		}

		void GoToPath(string target)
		{
			ExNode n = Sel;
			Profile p = n != null ? n.Profile : FirstProfile(tree.Nodes);
			if (p == null || string.IsNullOrEmpty(target)) return;
			target = target.Replace('\\', '/');
			ConnectProfile(p, delegate (bool ok)
			{
				if (!ok) return;
				TreeNode root = RootNode(p);
				Descend(root, target);
			});
		}

		void Descend(TreeNode node, string target)
		{
			ExNode n = (ExNode)node.Tag;
			string cur = n.Remote.TrimEnd('/');
			if (cur == target.TrimEnd('/')) { tree.SelectedNode = node; node.EnsureVisible(); return; }
			if (!(target.StartsWith(cur + "/") || n.IsRoot)) return;
			Action<bool> find = null;
			find = delegate (bool retry)
			{
				foreach (TreeNode c in node.Nodes)
				{
					ExNode cn = c.Tag as ExNode; if (cn == null) continue;
					string cr = cn.Remote.TrimEnd('/');
					if (target.TrimEnd('/') == cr || target.StartsWith(cr + "/"))
					{
						if (cn.IsDir && target.TrimEnd('/') != cr && c.Nodes.Count == 1 && c.Nodes[0].Tag == null) LoadDir(c, delegate { c.Expand(); Descend(c, target); });
						else { c.Expand(); Descend(c, target); }
						return;
					}
				}
				// the path leaves the chain: list this folder fully, then look again
				if (retry && n.Chain) LoadDir(node, delegate { node.Expand(); find(false); });
			};
			if (node.Nodes.Count == 1 && node.Nodes[0].Tag == null) LoadDir(node, delegate { node.Expand(); find(false); });
			else find(true);
		}

		// ---------------------------------------------------------------- actions
		void OnDoubleClick(object s, TreeNodeMouseClickEventArgs e)
		{
			ExNode n = e.Node.Tag as ExNode;
			if (n == null || e.Button != MouseButtons.Left) return;
			if (n.IsRoot) { if (!Explorer.Connected.Contains(n.Profile.Name)) ConnectProfile(n.Profile); return; }
			if (!n.IsDir) { CenterNode(e.Node); Explorer.OpenFile(n.Profile, n.Remote); }
		}

		void OnKey(object s, KeyEventArgs e)
		{
			ExNode n = Sel; if (n == null) return;
			if (e.KeyCode == Keys.F5) { RefreshSelected(); e.Handled = true; }
			else if (e.KeyCode == Keys.Enter && !n.IsDir) { Explorer.OpenFile(n.Profile, n.Remote); e.Handled = true; }
			else if (e.KeyCode == Keys.Delete && n.Entry != null && !n.Chain) { DoDelete(n); e.Handled = true; }
			else if (e.KeyCode == Keys.F2 && n.Entry != null && !n.Chain) { DoRename(n); e.Handled = true; }
		}

		ExNode DropTarget(DragEventArgs e)
		{
			TreeNode t = tree.GetNodeAt(tree.PointToClient(new Point(e.X, e.Y)));
			if (t == null) return null;
			ExNode n = t.Tag as ExNode;
			if (n == null) return null;
			if (n.IsRoot && !Explorer.Connected.Contains(n.Profile.Name)) return null;
			return n;
		}

		void OnDrop(object s, DragEventArgs e)
		{
			ExNode n = DropTarget(e);
			string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
			if (n == null || files == null) return;
			string dir = n.IsDir ? n.Remote : RemotePath.Parent(n.Remote);
			if (MessageBox.Show(this, L.F("Выложить на сервер ({0} {1}): {2} шт.?", n.Profile.Name, dir, files.Length), "FTP Sync", MessageBoxButtons.YesNo) == DialogResult.Yes)
				Explorer.UploadFiles(n.Profile, dir, files);
		}

		void DoDelete(ExNode n)
		{
			Explorer.Delete(n.Profile, n.Entry, delegate { RefreshDir(n.Profile, RemotePath.Parent(n.Remote)); });
		}

		void DoRename(ExNode n)
		{
			string name = PromptForm.Ask(this, L.T("Переименовать"), L.T("Новое имя для ") + n.Entry.Name, n.Entry.Name, false);
			if (string.IsNullOrEmpty(name) || name == n.Entry.Name) return;
			Explorer.Rename(n.Profile, n.Entry, name, delegate { RefreshDir(n.Profile, RemotePath.Parent(n.Remote)); });
		}

		ContextMenuStrip BuildMenu()
		{
			ContextMenuStrip m = new ContextMenuStrip();
			Func<string, Action<ExNode>, ToolStripMenuItem> add = delegate (string text, Action<ExNode> act)
			{
				ToolStripMenuItem it = new ToolStripMenuItem(text);
				it.Click += delegate { ExNode n = Sel; if (n != null) { try { act(n); } catch (Exception ex) { EventLog.Error(L.T("Дерево"), ex.Message, ex); } } };
				m.Items.Add(it); return it;
			};
			ToolStripMenuItem iConnect = add(L.T("Подключиться"), n => ConnectProfile(n.Profile));
			ToolStripMenuItem iDisc = add(L.T("Отключиться"), n => DisconnectSelected());
			ToolStripMenuItem iOpen = add(L.T("Открыть"), n => Explorer.OpenFile(n.Profile, n.Remote));
			ToolStripMenuItem iRef = add(L.T("Обновить"), n => RefreshSelected());
			ToolStripMenuItem iUp = add(L.T("Выложить сюда файлы…"), n =>
			{
				OpenFileDialog d = new OpenFileDialog { Multiselect = true, Title = L.T("Что выложить в ") + (n.IsDir ? n.Remote : RemotePath.Parent(n.Remote)) };
				if (d.ShowDialog(this) == DialogResult.OK) Explorer.UploadFiles(n.Profile, n.IsDir ? n.Remote : RemotePath.Parent(n.Remote), d.FileNames);
			});
			ToolStripMenuItem iDown = add(L.T("Скачать папку в кэш"), n => Explorer.DownloadFolder(n.Profile, n.Remote));
			ToolStripMenuItem iNewF = add(L.T("Новая папка…"), n =>
			{
				string name = PromptForm.Ask(this, L.T("Новая папка"), L.T("Имя папки в ") + n.Remote, "", false);
				if (!string.IsNullOrEmpty(name)) Explorer.MakeDir(n.Profile, n.Remote, name, delegate { RefreshDir(n.Profile, n.Remote); });
			});
			ToolStripMenuItem iNewFile = add(L.T("Новый файл…"), n =>
			{
				string name = PromptForm.Ask(this, L.T("Новый файл"), L.T("Имя файла в ") + n.Remote, "", false);
				if (!string.IsNullOrEmpty(name)) Explorer.NewFile(n.Profile, n.Remote, name, delegate { RefreshDir(n.Profile, n.Remote); });
			});
			ToolStripMenuItem iRen = add(L.T("Переименовать\tF2"), DoRename);
			ToolStripMenuItem iDel = add(L.T("Удалить\tDel"), DoDelete);
			ToolStripMenuItem iCopy = add(L.T("Копировать путь на сервере"), n => Clipboard.SetText(n.Remote));
			ToolStripMenuItem iLocal = add(L.T("Копировать локальный путь (кэш)"), n => Clipboard.SetText(Plugin.LocalPathFor(n.Profile, n.Remote)));
			ToolStripMenuItem iBk = add(L.T("Бекапы этого файла"), n => Plugin.ShowBackupsFor(n.Profile, n.Remote));

			ToolStripMenuItem iBm = new ToolStripMenuItem(L.T("Закладки"));
			m.Items.Add(iBm);
			m.Opening += delegate (object s, System.ComponentModel.CancelEventArgs e)
			{
				ExNode n = Sel;
				if (n == null) { e.Cancel = true; return; }
				iBm.DropDownItems.Clear();
				if (n.IsRoot)
					foreach (Bookmark b in n.Profile.Bookmarks)
					{
						string target = b.Remote;
						iBm.DropDownItems.Add(b.Name + "   " + b.Remote, null, delegate { GoToPath(target); });
					}
				iBm.Visible = n.IsRoot && n.Profile.Bookmarks.Count > 0;
				bool on = Explorer.Connected.Contains(n.Profile.Name);
				bool file = n.Entry != null && !n.Entry.IsDir;
				bool dir = n.IsDir && (!n.IsRoot || on);
				iConnect.Visible = n.IsRoot && !on; iDisc.Visible = n.IsRoot && on;
				iOpen.Visible = file; iRef.Visible = dir; iUp.Visible = dir || file; iDown.Visible = dir && !n.IsRoot || n.IsRoot && on;
				iNewF.Visible = dir; iNewFile.Visible = dir; iRen.Visible = n.Entry != null && !n.Chain; iDel.Visible = n.Entry != null && !n.Chain;
				iCopy.Visible = !n.IsRoot || on; iLocal.Visible = !n.IsRoot; iBk.Visible = file;
			};
			return m;
		}

		// ---------------------------------------------------------------- transfer status
		public void UpdateItem(QueueItem q)
		{
			if (q.State != QState.Queued) ShowTransfer(q);
		}
	}
}
