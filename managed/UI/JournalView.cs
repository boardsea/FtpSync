using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FtpSync
{
	/// <summary>
	/// Event journal. Plain mode: just the list and the latest line (double-click shows the full text of a record).
	/// Extended mode: the same list with a button bar (details, copy, clear, file, filter, search, auto-scroll).
	/// </summary>
	public class JournalView : UserControl
	{
		readonly bool extended;
		readonly ListView jl = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false };
		readonly RichTextBox jDetails = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9f), BackColor = Color.White, WordWrap = true };
		readonly ComboBox jLevel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
		readonly TextBox jFind = new TextBox { Width = 140 };
		readonly CheckBox jAuto = new CheckBox { Text = L.T("Автопрокрутка"), Checked = true, AutoSize = true, Padding = new Padding(6, 4, 0, 0) };
		readonly Label jLast = new Label { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(4, 4, 4, 0), AutoEllipsis = true };
		readonly SplitContainer jSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
		bool expanded;

		public JournalView(bool extended)
		{
			this.extended = extended;
			Dock = DockStyle.Fill;
			jl.Columns.Add(L.T("Время"), 130); jl.Columns.Add("", 24); jl.Columns.Add(L.T("Источник"), 110); jl.Columns.Add(L.T("Событие"), 700);
			jSplit.Panel1.Controls.Add(jl); jSplit.Panel2.Controls.Add(jDetails);
			jSplit.SplitterDistance = 100; jSplit.Panel2Collapsed = true;
			Controls.Add(jSplit);

			Button bExpand = null;
			if (extended)
			{
				jLevel.Items.AddRange(new object[] { L.T("Все события"), L.T("Готово (зелёные)"), L.T("Выкладка (оранжевые)"), L.T("Предупреждения и ошибки"), L.T("Только ошибки") }); jLevel.SelectedIndex = 0;
				FlowLayoutPanel bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(2) };
				bExpand = new Button { Text = L.T("Раскрыть ▾"), Width = 90 };
				Button bCopy = new Button { Text = L.T("Копировать"), Width = 90 };
				Button bClear = new Button { Text = L.T("Очистить"), Width = 80 };
				Button bFile = new Button { Text = L.T("Файл журнала"), Width = 100 };
				Label fl = new Label { Text = L.T("Поиск:"), AutoSize = true, Padding = new Padding(8, 6, 0, 0) };
				bar.Controls.AddRange(new Control[] { bExpand, bCopy, bClear, bFile, jLevel, fl, jFind, jAuto });
				Controls.Add(bar);
				bExpand.Click += delegate { SetExpanded(!expanded, bExpand); };
				bCopy.Click += delegate
				{
					System.Text.StringBuilder sb = new System.Text.StringBuilder();
					if (jl.SelectedItems.Count > 0) foreach (ListViewItem it in jl.SelectedItems) sb.AppendLine(EventLog.Format((LogEntry)it.Tag));
					else foreach (ListViewItem it in jl.Items) sb.AppendLine(EventLog.Format((LogEntry)it.Tag));
					if (sb.Length > 0) Clipboard.SetText(sb.ToString());
				};
				bClear.Click += delegate { EventLog.Clear(); jl.Items.Clear(); jDetails.Clear(); jLast.Text = ""; };
				bFile.Click += delegate { if (!string.IsNullOrEmpty(EventLog.FilePath) && File.Exists(EventLog.FilePath)) Plugin.Reveal(EventLog.FilePath); };
				jLevel.SelectedIndexChanged += delegate { Rebuild(); };
				jFind.TextChanged += delegate { Rebuild(); };
			}
			Controls.Add(jLast);

			jl.SelectedIndexChanged += delegate { ShowDetails(); };
			jl.DoubleClick += delegate { if (!expanded) SetExpanded(true, bExpand); ShowDetails(); };
			jl.ContextMenuStrip = new ContextMenuStrip();
			jl.ContextMenuStrip.Items.Add(L.T("Копировать запись (с подробностями)"), null, delegate
			{
				if (jl.SelectedItems.Count > 0) Clipboard.SetText(EventLog.Format((LogEntry)jl.SelectedItems[0].Tag));
			});

			EventLog.Added += delegate (LogEntry e) { Plugin.UiInvoke(delegate { Add(e, true); }); };
			Rebuild();
		}

		void SetExpanded(bool on, Button b)
		{
			expanded = on; jSplit.Panel2Collapsed = !on;
			if (b != null) b.Text = on ? L.T("Свернуть ▴") : L.T("Раскрыть ▾");
			ShowDetails();
		}

		/// <summary>Done = green, upload = orange, error = red, warning = brown, plain = black.</summary>
		public static Color LevelColor(LogLevel l)
		{
			switch (l)
			{
				case LogLevel.Ok: return Color.FromArgb(27, 127, 59);
				case LogLevel.Upload: return Color.FromArgb(230, 126, 0);
				case LogLevel.Error: return Color.Firebrick;
				case LogLevel.Warn: return Color.FromArgb(139, 90, 0);
				default: return Color.Black;
			}
		}

		bool Matches(LogEntry e)
		{
			if (!extended) return true;
			if (jLevel.SelectedIndex == 1 && e.Level != LogLevel.Ok) return false;
			if (jLevel.SelectedIndex == 2 && e.Level != LogLevel.Upload) return false;
			if (jLevel.SelectedIndex == 3 && e.Level != LogLevel.Warn && e.Level != LogLevel.Error) return false;
			if (jLevel.SelectedIndex == 4 && e.Level != LogLevel.Error) return false;
			string f = jFind.Text.Trim();
			if (f.Length > 0 && (e.Message + " " + e.Source + " " + e.Details).IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) return false;
			return true;
		}

		void Rebuild()
		{
			jl.BeginUpdate();
			jl.Items.Clear();
			List<LogEntry> all = EventLog.Snapshot();
			for (int i = Math.Max(0, all.Count - 2000); i < all.Count; i++) Add(all[i], false);
			jl.EndUpdate();
			if (jl.Items.Count > 0) jl.EnsureVisible(jl.Items.Count - 1);
		}

		void Add(LogEntry e, bool live)
		{
			if (live)
			{
				jLast.Text = e.Time.ToString("HH:mm:ss") + "  " + e.Message;
				jLast.ForeColor = LevelColor(e.Level);
			}
			if (!Matches(e)) return;
			ListViewItem it = new ListViewItem(new[] { e.Time.ToString("yyyy-MM-dd HH:mm:ss"), string.IsNullOrEmpty(e.Details) ? "" : "▸", e.Source, e.Message });
			it.Tag = e;
			it.ForeColor = LevelColor(e.Level);
			jl.Items.Add(it);
			while (jl.Items.Count > 2000) jl.Items.RemoveAt(0);
			if (live && (!extended || jAuto.Checked)) it.EnsureVisible();
		}

		void ShowDetails()
		{
			if (!expanded) return;
			jDetails.Clear();
			if (jl.SelectedItems.Count == 0) { jDetails.Text = L.T("Выберите запись в списке."); return; }
			LogEntry e = (LogEntry)jl.SelectedItems[0].Tag;
			jDetails.SelectionFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
			jDetails.SelectionColor = LevelColor(e.Level);
			jDetails.AppendText(e.Time.ToString("yyyy-MM-dd HH:mm:ss") + "  " + e.Source + "\n" + e.Message + "\n\n");
			jDetails.SelectionFont = new Font("Consolas", 9f); jDetails.SelectionColor = Color.Black;
			jDetails.AppendText(string.IsNullOrEmpty(e.Details) ? L.T("(подробностей нет)") : e.Details);
			jDetails.SelectionStart = 0;
		}
	}
}
