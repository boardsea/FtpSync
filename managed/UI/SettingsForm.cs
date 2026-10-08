using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace FtpSync
{
	public class SettingsForm : Form
	{
		public AppSettings Data;
		readonly ListBox lst = new ListBox { Dock = DockStyle.Fill };
		readonly TextBox group = new TextBox(), comment = new TextBox(), initDir = new TextBox(), name = new TextBox(), host = new TextBox(), user = new TextBox(), pass = new TextBox { UseSystemPasswordChar = true }, key = new TextBox();
		readonly NumericUpDown port = new NumericUpDown { Minimum = 1, Maximum = 65535 };
		readonly ComboBox proto = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
		readonly CheckBox enabled = new CheckBox { Text = L.T("Профиль включён"), AutoSize = true },
			autoUp = new CheckBox { Text = L.T("Выкладывать на сервер при сохранении"), AutoSize = true },
			passive = new CheckBox { Text = L.T("Пассивный режим FTP"), AutoSize = true, Checked = true },
			remember = new CheckBox { Text = L.T("Запоминать последнюю папку (для этого аккаунта)"), AutoSize = true },
			hidden = new CheckBox { Text = L.T("Показывать скрытые файлы (.name)"), AutoSize = true },
			showStart = new CheckBox { Text = L.T("Показывать дерево подключений при запуске"), AutoSize = true };
		readonly DataGridView maps = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = true, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
		readonly TextBox backupRoot = new TextBox();
		readonly NumericUpDown poll = new NumericUpDown { Minimum = 10, Maximum = 3600 }, maxV = new NumericUpDown { Minimum = 3, Maximum = 1000 }, days = new NumericUpDown { Minimum = 0, Maximum = 3650 };
		readonly ComboBox lang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Left, Width = 220 };
		readonly List<string> langCodes = new List<string>();
		readonly CheckBox onSave = new CheckBox { Text = L.T("Проверять перед сохранением"), AutoSize = true },
			onAct = new CheckBox { Text = L.T("Проверять при переключении на вкладку"), AutoSize = true },
			onTimer = new CheckBox { Text = L.T("Проверять по таймеру (активный файл)"), AutoSize = true };
		Profile cur;
		readonly string configDir;

		public SettingsForm(AppSettings s, string pluginsConfigDir)
		{
			configDir = pluginsConfigDir;
			using (MemoryStream ms = new MemoryStream())
			{
				XmlSerializer xs = new XmlSerializer(typeof(AppSettings));
				xs.Serialize(ms, s); ms.Position = 0; Data = (AppSettings)xs.Deserialize(ms);
			}
			Text = L.T("FTP Sync - настройки"); Width = 900; Height = 640; StartPosition = FormStartPosition.CenterScreen;
			proto.Items.AddRange(new object[] { "FTP", L.T("FTPES (явный TLS)"), L.T("FTPS (неявный, не поддерживается)"), "SFTP" });

			TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

			Panel left = new Panel { Dock = DockStyle.Fill };
			FlowLayoutPanel lb = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 96, FlowDirection = FlowDirection.LeftToRight };
			Button bAdd = new Button { Text = L.T("Добавить"), Width = 90 }, bDel = new Button { Text = L.T("Удалить"), Width = 90 };
			Button bImp = new Button { Text = L.T("Импорт из NppFTP…"), Width = 190 }, bFz = new Button { Text = L.T("Импорт из FileZilla (XML/CSV)…"), Width = 190 };
			bAdd.Click += delegate { Commit(); Profile p = new Profile { Name = "profile" + (Data.Profiles.Count + 1), Port = 21 }; p.Maps.Add(new PathMap("", "/")); Data.Profiles.Add(p); Reload(p); };
			bDel.Click += delegate { if (cur != null) { Data.Profiles.Remove(cur); cur = null; Reload(null); } };
			bImp.Click += delegate { Import(); };
			bFz.Click += delegate { ImportFz(); };
			lb.Controls.AddRange(new Control[] { bAdd, bDel, bImp, bFz });
			left.Controls.Add(lst); left.Controls.Add(lb);
			lst.SelectedIndexChanged += delegate { Commit(); Show((Profile)lst.SelectedItem); };

			TabControl tc = new TabControl { Dock = DockStyle.Fill };
			TabPage tp = new TabPage(L.T("Профиль")), tg = new TabPage(L.T("Общие"));
			TableLayoutPanel f = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(8) };
			f.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			Row(f, L.T("Название"), name); Row(f, L.T("Хост"), host); Row(f, L.T("Порт"), port); Row(f, L.T("Протокол"), proto);
			Row(f, L.T("Пользователь"), user); Row(f, L.T("Пароль"), pass); Row(f, L.T("Ключ SFTP (файл)"), key); Row(f, L.T("Стартовая папка"), initDir); Row(f, L.T("Группа (папка в дереве)"), group); Row(f, L.T("Комментарий"), comment); Row(f, "", passive); Row(f, "", enabled); Row(f, "", autoUp); Row(f, "", remember); Row(f, "", hidden);
			foreach (Control c in new Control[] { name, host, user, pass, key, proto, initDir, group, comment }) c.Dock = DockStyle.Fill;
			Label ml = new Label { Text = L.T("Соответствие локальных папок (кэш NppFTP) и папок на сервере:"), Dock = DockStyle.Top, Height = 22 };
			maps.Columns.Add("local", L.T("Локальная папка")); maps.Columns.Add("remote", L.T("Папка на сервере"));
			Panel mp = new Panel { Dock = DockStyle.Fill };
			mp.Controls.Add(maps); mp.Controls.Add(ml);
			SplitContainer sc = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 390 };
			sc.Panel1.Controls.Add(f); sc.Panel2.Controls.Add(mp);
			tp.Controls.Add(sc);

			TableLayoutPanel g = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(8) };
			g.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220)); g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			backupRoot.Dock = DockStyle.Fill;
			Row(g, L.T("Папка бекапов"), backupRoot); Row(g, L.T("Период проверки, сек"), poll);
			Row(g, L.T("Версий на файл (макс.)"), maxV); Row(g, L.T("Хранить дней (0 = вечно)"), days);
			langCodes.Add("auto"); lang.Items.Add(L.T("Автоматически (язык системы)"));
			foreach (KeyValuePair<string, string> kv in L.Available()) { langCodes.Add(kv.Key); lang.Items.Add(kv.Value); }
			lang.SelectedIndex = Math.Max(0, langCodes.IndexOf(Data.Language ?? "auto"));
			Row(g, L.T("Язык интерфейса (после смены перезапустите Notepad++)"), lang);
			Row(g, "", onSave); Row(g, "", onAct); Row(g, "", onTimer); Row(g, "", showStart);
			tg.Controls.Add(g);
			tc.TabPages.Add(tp); tc.TabPages.Add(tg);

			root.Controls.Add(left, 0, 0); root.Controls.Add(tc, 1, 0);
			FlowLayoutPanel ok = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
			Button bOk = new Button { Text = L.T("Сохранить"), Width = 100, DialogResult = DialogResult.OK }, bCancel = new Button { Text = L.T("Отмена"), Width = 100, DialogResult = DialogResult.Cancel };
			bOk.Click += delegate { Commit(); Apply(); };
			ok.Controls.Add(bCancel); ok.Controls.Add(bOk);
			root.Controls.Add(ok, 0, 1); root.SetColumnSpan(ok, 2);
			Controls.Add(root);
			AcceptButton = bOk; CancelButton = bCancel;

			backupRoot.Text = Data.BackupRoot; poll.Value = Clamp(poll, Data.PollSeconds); maxV.Value = Clamp(maxV, Data.MaxVersions); days.Value = Clamp(days, Data.KeepDays);
			onSave.Checked = Data.CheckOnSave; onAct.Checked = Data.CheckOnActivate; onTimer.Checked = Data.CheckOnTimer; showStart.Checked = Data.ShowExplorerOnStart;
			Reload(null);
			if (lst.Items.Count > 0) lst.SelectedIndex = 0;
		}

		static decimal Clamp(NumericUpDown n, int v) { return Math.Max(n.Minimum, Math.Min(n.Maximum, v)); }

		static void Row(TableLayoutPanel t, string label, Control c)
		{
			t.RowCount++;
			t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
			t.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
			t.Controls.Add(c);
		}

		void Reload(Profile select)
		{
			lst.Items.Clear();
			List<Profile> sorted = new List<Profile>(Data.Profiles);
			sorted.Sort((x, y) => NaturalComparer.Instance.Compare(x.Name, y.Name));
			foreach (Profile p in sorted) lst.Items.Add(p);
			if (select != null) lst.SelectedItem = select; else Show(null);
		}

		void Show(Profile p)
		{
			cur = p;
			bool on = p != null;
			foreach (Control c in new Control[] { name, host, port, proto, user, pass, key, initDir, group, comment, passive, enabled, autoUp, remember, hidden, maps }) c.Enabled = on;
			maps.Rows.Clear();
			if (!on) return;
			name.Text = p.Name; host.Text = p.Host; port.Value = Clamp(port, p.Port); proto.SelectedIndex = (int)p.Protocol;
			user.Text = p.User; pass.Text = p.Password; key.Text = p.KeyFile; enabled.Checked = p.Enabled; autoUp.Checked = p.AutoUpload; hidden.Checked = p.ShowHidden; initDir.Text = p.InitialDir; remember.Checked = p.RememberLastDir; group.Text = p.Group; comment.Text = p.Comment; passive.Checked = p.Passive;
			foreach (PathMap m in p.Maps) maps.Rows.Add(m.Local, m.Remote);
		}

		void Commit()
		{
			if (cur == null) return;
			cur.Name = name.Text.Trim(); cur.Host = host.Text.Trim(); cur.Port = (int)port.Value;
			cur.Protocol = (Protocol)Math.Max(0, proto.SelectedIndex);
			cur.User = user.Text; cur.Password = pass.Text; cur.KeyFile = key.Text.Trim(); cur.Enabled = enabled.Checked; cur.AutoUpload = autoUp.Checked; cur.ShowHidden = hidden.Checked; cur.InitialDir = initDir.Text.Trim(); cur.RememberLastDir = remember.Checked; cur.Group = group.Text.Trim().Trim('/'); cur.Comment = comment.Text.Trim(); cur.Passive = passive.Checked;
			cur.Maps.Clear();
			foreach (DataGridViewRow r in maps.Rows)
			{
				if (r.IsNewRow) continue;
				string l = Convert.ToString(r.Cells[0].Value), rm = Convert.ToString(r.Cells[1].Value);
				if (!string.IsNullOrEmpty(l)) cur.Maps.Add(new PathMap(l.Trim(), string.IsNullOrEmpty(rm) ? "/" : rm.Trim()));
			}
			int i = lst.Items.IndexOf(cur);
			if (i >= 0 && lst.Items[i].ToString() != cur.Name) { lst.Items[i] = cur; }
		}

		void Apply()
		{
			Data.BackupRoot = backupRoot.Text.Trim();
			Data.PollSeconds = (int)poll.Value; Data.MaxVersions = (int)maxV.Value; Data.KeepDays = (int)days.Value;
			Data.Language = lang.SelectedIndex >= 0 ? langCodes[lang.SelectedIndex] : "auto";
			Data.CheckOnSave = onSave.Checked; Data.CheckOnActivate = onAct.Checked; Data.CheckOnTimer = onTimer.Checked; Data.ShowExplorerOnStart = showStart.Checked;
		}

		void ImportFz()
		{
			string def = FileZillaXmlImporter.DefaultFile();
			OpenFileDialog d = new OpenFileDialog { Title = "FileZilla", Filter = L.T("FileZilla (XML, CSV)|*.xml;*.csv|Все файлы|*.*") };
			if (def.Length > 0) { d.InitialDirectory = Path.GetDirectoryName(def); d.FileName = def; }
			if (d.ShowDialog(this) != DialogResult.OK) return;
			try
			{
				NppFtpImporter.Result r = FileZillaImporter.Import(d.FileName);
				Commit();
				foreach (Profile p in r.Profiles)
				{
					Data.Profiles.RemoveAll(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Group, p.Group, StringComparison.OrdinalIgnoreCase));
					Data.Profiles.Add(p);
				}
				Reload(r.Profiles.Count > 0 ? r.Profiles[0] : null);
				MessageBox.Show(this, L.F("Импортировано профилей: {0}", r.Profiles.Count) + (r.Warnings.Count > 0 ? "\n\n" + string.Join("\n", r.Warnings.ToArray()) : "") +
					L.T("\n\nНажмите «Сохранить». Пароли хранятся зашифрованно (DPAPI); файл с открытыми паролями лучше удалить."), "FTP Sync");
			}
			catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("Ошибка импорта")); }
		}

		void Import()
		{
			OpenFileDialog d = new OpenFileDialog { Title = "NppFTP.xml", Filter = L.T("NppFTP.xml|*.xml|Все файлы|*.*"), FileName = NppFtpImporter.DefaultFile(configDir) };
			string def = NppFtpImporter.DefaultFile(configDir);
			if (File.Exists(def)) { d.InitialDirectory = Path.GetDirectoryName(def); }
			if (d.ShowDialog(this) != DialogResult.OK) return;
			try
			{
				NppFtpImporter.Result r = NppFtpImporter.Import(d.FileName);
				Commit();
				foreach (Profile p in r.Profiles)
				{
					Data.Profiles.RemoveAll(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase));
					Data.Profiles.Add(p);
				}
				Reload(r.Profiles.Count > 0 ? r.Profiles[0] : null);
				MessageBox.Show(this, L.T("Импортировано профилей: ") + r.Profiles.Count + (r.Warnings.Count > 0 ? "\n\n" + string.Join("\n", r.Warnings.ToArray()) : "") +
					L.T("\n\nПроверьте протокол, порт и пароль, затем нажмите «Сохранить»."), "FTP Sync");
			}
			catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("Ошибка импорта")); }
		}
	}
}
