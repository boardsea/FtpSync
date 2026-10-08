using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FtpSync
{
	public enum Choice { None, UseServer, Merge, OverwriteServer, Ignore }

	/// <summary>Warning: the server copy changed. Used before saving (modal) and for background detection (modeless).</summary>
	public class ConflictForm : Form
	{
		public Choice Result = Choice.None;
		readonly RichTextBox rtb = DiffView.Create();
		readonly ComboBox view = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
		readonly string local;

		public ConflictForm(CheckResult cr, string localText, bool beforeSave, bool bufferDirty)
		{
			local = localText;
			Text = "FTP Sync - " + cr.Target.Remote;
			Width = 980; Height = 650; StartPosition = FormStartPosition.CenterScreen; ShowInTaskbar = false;
			MinimizeBox = false; TopMost = !beforeSave;

			bool hasBase = cr.BaseText != null;
			string remoteText = TextUtil.Decode(cr.Remote);
			string head;
			switch (cr.Status)
			{
				case SyncStatus.Conflict: head = L.T("Файл изменён на сервере, и у вас есть свои правки."); break;
				case SyncStatus.RemoteChanged: head = L.T("Файл на сервере изменился с момента, как вы его загрузили."); break;
				default: head = L.T("Версия на сервере отличается от открытой, а исходная (база) неизвестна."); break;
			}
			if (beforeSave) head += L.T("\nСохранение ещё не произошло. Закрытие окна = безопасный вариант (загрузится серверная версия, ваш текст уйдёт в новую вкладку).");
			Label lbl = new Label { Text = head + "\n" + cr.Target.Profile.Name + "  " + cr.Target.Remote, Dock = DockStyle.Top, Height = 62, Padding = new Padding(8, 6, 8, 0) };

			List<string> views = new List<string>();
			if (hasBase) { view.Items.Add(L.T("Что изменилось на сервере (база → сервер)")); views.Add("server"); }
			if (hasBase) { view.Items.Add(L.T("Ваши правки (база → вы)")); views.Add("mine"); }
			view.Items.Add(L.T("Сервер и ваша версия (сервер → вы)")); views.Add("both");
			view.SelectedIndexChanged += delegate
			{
				string kind = view.SelectedIndex >= 0 && view.SelectedIndex < views.Count ? views[view.SelectedIndex] : "both";
				if (kind == "server") DiffView.Fill(rtb, Diff.Unified(cr.BaseText, remoteText, 3));
				else if (kind == "mine") DiffView.Fill(rtb, Diff.Unified(cr.BaseText, local, 3));
				else DiffView.Fill(rtb, Diff.Unified(remoteText, local, 3));
			};

			FlowLayoutPanel btns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(6), FlowDirection = FlowDirection.RightToLeft };
			Button bOver = Btn(beforeSave ? L.T("Перезаписать сервер моей версией") : L.T("Оставить моё, игнорировать"), beforeSave ? Choice.OverwriteServer : Choice.Ignore, 210);
			string useText = beforeSave ? L.T("Взять серверную (моя → в новую вкладку)") : (bufferDirty ? L.T("Заменить серверной версией") : L.T("Обновить с сервера"));
			Button bUse = Btn(useText, Choice.UseServer, 240);
			Button bMerge = Btn(L.T("Авто-слияние"), Choice.Merge, 120);
			bMerge.Enabled = hasBase && cr.Status == SyncStatus.Conflict;
			if (!beforeSave) { Button bIgn = Btn(L.T("Игнорировать эту версию"), Choice.Ignore, 150); btns.Controls.Add(bIgn); }
			else btns.Controls.Add(bOver);
			btns.Controls.Add(bMerge); btns.Controls.Add(bUse);

			Controls.Add(rtb); Controls.Add(view); Controls.Add(lbl); Controls.Add(btns);
			view.SelectedIndex = 0;
			FormClosing += delegate { if (Result == Choice.None) Result = beforeSave ? Choice.UseServer : Choice.Ignore; };
		}

		Button Btn(string text, Choice c, int w)
		{
			Button b = new Button { Text = text, Width = w, Height = 30 };
			b.Click += delegate { Result = c; Close(); };
			return b;
		}
	}
}
