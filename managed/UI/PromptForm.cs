using System;
using System.Windows.Forms;

namespace FtpSync
{
	public class PromptForm : Form
	{
		readonly TextBox box = new TextBox { Dock = DockStyle.Top };
		public string Value { get { return box.Text; } }

		public PromptForm(string title, string label, string value, bool password)
		{
			Text = title; Width = 420; Height = 150; FormBorderStyle = FormBorderStyle.FixedDialog;
			StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
			box.Text = value ?? ""; box.UseSystemPasswordChar = password;
			Label l = new Label { Text = label, Dock = DockStyle.Top, Height = 38, Padding = new Padding(0, 4, 0, 0) };
			FlowLayoutPanel b = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.RightToLeft };
			Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 }, cancel = new Button { Text = L.T("Отмена"), DialogResult = DialogResult.Cancel, Width = 90 };
			b.Controls.Add(cancel); b.Controls.Add(ok);
			Padding = new Padding(10, 8, 10, 4);
			Controls.Add(box); Controls.Add(l); Controls.Add(b);
			AcceptButton = ok; CancelButton = cancel;
			Shown += delegate { box.Focus(); box.SelectAll(); };
		}

		public static string Ask(IWin32Window owner, string title, string label, string value, bool password)
		{
			using (PromptForm f = new PromptForm(title, label, value, password))
				return f.ShowDialog(owner) == DialogResult.OK ? f.Value : null;
		}
	}
}
