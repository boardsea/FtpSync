using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FtpSync
{
	public class NodeInfo
	{
		public string Kind;       // dir | file | version
		public string Path;       // directory or version path
		public string Profile;    // profile name
		public string Remote;     // remote path for file/version nodes
	}

	/// <summary>Docked panel: tab "Status" (files being watched) and tab "Backups" (mirror of the remote tree).</summary>
	public class PanelForm : Form
	{
		readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };
		readonly ListView list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
		readonly TreeView tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false };
		readonly TextBox filter = new TextBox { Width = 180 };
		readonly Label info = new Label { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(4, 4, 4, 0) };
		readonly Dictionary<string, ListViewItem> rows = new Dictionary<string, ListViewItem>();

		readonly ImageList icons = FileIcons.Build();

		public PanelForm()
		{
			Text = "FTP Sync"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
			tree.ImageList = icons; tree.ItemHeight = 22; list.SmallImageList = icons;

			TabPage tStatus = new TabPage(L.T("Статус файлов")), tBackups = new TabPage(L.T("Бекапы"));
			list.Columns.Add(L.T("Файл на сервере"), 380); list.Columns.Add(L.T("Профиль"), 110); list.Columns.Add(L.T("Состояние"), 220); list.Columns.Add(L.T("Проверен"), 80);
			ContextMenuStrip lm = new ContextMenuStrip();
			lm.Items.Add(L.T("Проверить сейчас"), null, delegate { Selected(delegate (string local) { Plugin.CheckNow(local, true); }); });
			lm.Items.Add(L.T("Показать различия"), null, delegate { Selected(delegate (string local) { Plugin.ShowDiffFor(local); }); });
			lm.Items.Add(L.T("Открыть в редакторе"), null, delegate { Selected(delegate (string local) { Npp.Open(local); }); });
			list.ContextMenuStrip = lm;
			list.DoubleClick += delegate { Selected(delegate (string local) { Npp.Open(local); }); };
			tStatus.Controls.Add(list);

			FlowLayoutPanel bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(2) };
			Button bRef = new Button { Text = L.T("Обновить"), Width = 80 }; bRef.Click += delegate { RebuildTree(); };
			Button bCur = new Button { Text = L.T("Текущий файл"), Width = 100 }; bCur.Click += delegate { SelectCurrentFile(); };
			Button bFolder = new Button { Text = L.T("Папка бекапов"), Width = 100 }; bFolder.Click += delegate { Plugin.OpenBackupFolder(); };
			Label fl = new Label { Text = L.T("Фильтр:"), AutoSize = true, Padding = new Padding(8, 6, 0, 0) };
			filter.TextChanged += delegate { RebuildTree(); };
			bar.Controls.AddRange(new Control[] { bRef, bCur, bFolder, fl, filter });
			tree.BeforeExpand += OnBeforeExpand;
			tree.AfterSelect += delegate { ShowInfo(); };
			tree.NodeMouseClick += delegate (object s, TreeNodeMouseClickEventArgs e) { tree.SelectedNode = e.Node; };
			tree.NodeMouseDoubleClick += delegate (object s, TreeNodeMouseClickEventArgs e) { NodeInfo n = e.Node.Tag as NodeInfo; if (n != null && n.Kind == "version") Plugin.OpenVersion(n.Path); };
			tree.ContextMenuStrip = BuildTreeMenu();
			tBackups.Controls.Add(tree); tBackups.Controls.Add(bar); tBackups.Controls.Add(info);

			TabPage tLog = new TabPage(L.T("Журнал")), tLogX = new TabPage(L.T("Расширенный журнал"));
			tLog.Controls.Add(new JournalView(false)); tLogX.Controls.Add(new JournalView(true));
			tabs.TabPages.Add(tStatus); tabs.TabPages.Add(tBackups); tabs.TabPages.Add(tLog); tabs.TabPages.Add(tLogX);
			Controls.Add(tabs);
			Shown += delegate { RebuildTree(); };
		}

		void Selected(Action<string> a)
		{
			if (list.SelectedItems.Count == 0) return;
			a((string)list.SelectedItems[0].Tag);
		}

		ContextMenuStrip BuildTreeMenu()
		{
			ContextMenuStrip m = new ContextMenuStrip();
			m.Opening += delegate (object s, System.ComponentModel.CancelEventArgs e)
			{
				NodeInfo n = tree.SelectedNode == null ? null : tree.SelectedNode.Tag as NodeInfo;
				foreach (ToolStripItem it in m.Items) it.Enabled = n != null && (n.Kind == "version" || it.Tag as string == "any");
			};
			Action<string, string, Action<NodeInfo>> add = delegate (string text, string kind, Action<NodeInfo> act)
			{
				ToolStripItem it = m.Items.Add(text, null, delegate { NodeInfo n = tree.SelectedNode == null ? null : tree.SelectedNode.Tag as NodeInfo; if (n != null) act(n); });
				it.Tag = kind;
			};
			add(L.T("Открыть копию в редакторе"), "ver", n => Plugin.OpenVersion(n.Path));
			add(L.T("Сравнить с серверной версией"), "ver", n => Plugin.CompareWithServer(n));
			add(L.T("Сравнить с открытым в редакторе"), "ver", n => Plugin.CompareWithEditor(n));
			add(L.T("Восстановить в редактор (заменить текст)"), "ver", n => Plugin.RestoreToEditor(n));
			add(L.T("Выложить эту версию на сервер…"), "ver", n => Plugin.UploadVersion(n));
			add(L.T("Показать в проводнике"), "any", n => Plugin.Reveal(n.Path));
			add(L.T("Удалить версию"), "ver", n => { if (MessageBox.Show(L.T("Удалить эту копию?"), "FTP Sync", MessageBoxButtons.YesNo) == DialogResult.Yes) { try { File.Delete(n.Path); } catch (IOException) { } RebuildTree(); } });
			return m;
		}

		// ---- status tab ----
		static string StatusText(CheckResult c)
		{
			switch (c.Status)
			{
				case SyncStatus.InSync: return L.T("совпадает с сервером");
				case SyncStatus.LocalAhead: return L.T("ваши правки ещё не на сервере");
				case SyncStatus.RemoteChanged: return L.T("ИЗМЕНЁН НА СЕРВЕРЕ");
				case SyncStatus.Conflict: return L.T("КОНФЛИКТ (сервер и вы)");
				case SyncStatus.NoBaseline: return L.T("отличается, база неизвестна");
				case SyncStatus.RemoteMissing: return L.T("нет на сервере");
				case SyncStatus.Error: return L.T("ошибка: ") + c.Message;
				default: return "";
			}
		}

		public void SetStatus(string local, CheckResult c)
		{
			if (c.Target == null) return;
			ListViewItem it;
			if (!rows.TryGetValue(local, out it))
			{
				it = new ListViewItem(new string[4]); it.Tag = local; rows[local] = it; list.Items.Add(it);
			}
			it.ImageKey = FileIcons.KeyFor(c.Target.Remote, false, false); it.SubItems[0].Text = c.Target.Remote; it.SubItems[1].Text = c.Target.Profile.Name;
			it.SubItems[2].Text = StatusText(c); it.SubItems[3].Text = c.Time.ToString("HH:mm:ss");
			it.ForeColor = c.Status == SyncStatus.RemoteChanged || c.Status == SyncStatus.Conflict || c.Status == SyncStatus.Error ? Color.Firebrick
				: c.Status == SyncStatus.LocalAhead ? Color.DarkOrange : Color.Black;
		}

		public void RemoveStatus(string local)
		{
			ListViewItem it;
			if (rows.TryGetValue(local, out it)) { list.Items.Remove(it); rows.Remove(local); }
		}

		public void ShowTab(int i) { tabs.SelectedIndex = i; }

		// ---- backups tab ----
		void ShowInfo()
		{
			NodeInfo n = tree.SelectedNode == null ? null : tree.SelectedNode.Tag as NodeInfo;
			info.Text = n == null ? "" : n.Kind == "version" ? n.Path : (n.Remote ?? n.Path);
		}

		static bool HasVersions(string dir)
		{
			try { foreach (string f in Directory.GetFiles(dir)) if (BackupStore.IsVersionFile(Path.GetFileName(f))) return true; }
			catch (IOException) { }
			return false;
		}

		string RemoteOf(string root, string path)
		{
			string rel = path.Substring(root.Length).Trim('\\', '/');
			string[] seg = rel.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
			if (seg.Length <= 1) return "/";
			return "/" + string.Join("/", seg, 1, seg.Length - 1);
		}

		public void RebuildTree()
		{
			tree.BeginUpdate();
			tree.Nodes.Clear();
			string root = Plugin.Backups.Root;
			string f = filter.Text.Trim().ToLowerInvariant();
			if (Directory.Exists(root))
				foreach (string pd in Directory.GetDirectories(root))
				{
					TreeNode pn = new TreeNode(Path.GetFileName(pd)) { ImageKey = FileIcons.Server, SelectedImageKey = FileIcons.Server, Tag = new NodeInfo { Kind = "dir", Path = pd, Profile = Path.GetFileName(pd), Remote = "/" } };
					if (FillDir(pn, pd, root, f)) tree.Nodes.Add(pn);
				}
			tree.EndUpdate();
			if (f.Length > 0) tree.ExpandAll();
		}

		/// <summary>Builds the directory tree; returns false when nothing matched the filter.</summary>
		bool FillDir(TreeNode parent, string dir, string root, string filter)
		{
			bool any = false;
			string[] subs;
			try { subs = Directory.GetDirectories(dir); } catch (IOException) { return false; }
			Array.Sort(subs, StringComparer.OrdinalIgnoreCase);
			foreach (string sd in subs)
			{
				string name = Path.GetFileName(sd);
				if (HasVersions(sd))
				{
					if (filter.Length > 0 && RemoteOf(root, sd).ToLowerInvariant().IndexOf(filter) < 0) continue;
					TreeNode fn = new TreeNode(name) { ImageKey = FileIcons.KeyFor(name, false, false), SelectedImageKey = FileIcons.KeyFor(name, false, false) };
					NodeInfo ni = (NodeInfo)parent.Tag;
					fn.Tag = new NodeInfo { Kind = "file", Path = sd, Profile = ni.Profile, Remote = RemoteOf(root, sd) };
					List<BackupVersion> vs = BackupStore.VersionsIn(sd);
					foreach (BackupVersion v in vs)
					{
						TreeNode vn = new TreeNode(v.Time.ToString("yyyy-MM-dd HH:mm:ss") + "  " + v.Reason + "  " + TextUtil.FormatSize(v.Size)) { ImageKey = FileIcons.Version, SelectedImageKey = FileIcons.Version };
						vn.Tag = new NodeInfo { Kind = "version", Path = v.Path, Profile = ni.Profile, Remote = RemoteOf(root, sd) };
						fn.Nodes.Add(vn);
					}
					fn.Text = name + "  (" + vs.Count + ")";
					parent.Nodes.Add(fn); any = true;
				}
				else
				{
					NodeInfo ni = (NodeInfo)parent.Tag;
					TreeNode dn = new TreeNode(name) { ImageKey = FileIcons.Folder, SelectedImageKey = FileIcons.Folder, Tag = new NodeInfo { Kind = "dir", Path = sd, Profile = ni.Profile, Remote = RemoteOf(root, sd) } };
					if (FillDir(dn, sd, root, filter)) { parent.Nodes.Add(dn); any = true; }
				}
			}
			return any || filter.Length == 0;
		}

		void OnBeforeExpand(object s, TreeViewCancelEventArgs e) { }

		public void SelectCurrentFile()
		{
			Plugin.SelectBackupsForCurrent(this);
		}

		public bool SelectRemote(string profile, string remote)
		{
			string want = BackupStore.SafeSegment(profile);
			foreach (TreeNode pn in tree.Nodes)
				if (string.Equals(pn.Text, want, StringComparison.OrdinalIgnoreCase))
				{
					TreeNode n = Find(pn, remote);
					if (n != null) { tree.SelectedNode = n; n.Expand(); n.EnsureVisible(); tabs.SelectedIndex = 1; return true; }
				}
			return false;
		}

		static TreeNode Find(TreeNode n, string remote)
		{
			foreach (TreeNode c in n.Nodes)
			{
				NodeInfo ni = c.Tag as NodeInfo;
				if (ni != null && ni.Kind == "file" && ni.Remote == remote) return c;
				TreeNode r = Find(c, remote);
				if (r != null) return r;
			}
			return null;
		}
	}
}
