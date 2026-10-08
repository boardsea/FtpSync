using System;
using System.Drawing;
using System.Windows.Forms;

namespace FtpSync
{
	public static class DiffView
	{
		public static RichTextBox Create()
		{
			return new RichTextBox
			{
				ReadOnly = true, Dock = DockStyle.Fill, WordWrap = false, BorderStyle = BorderStyle.FixedSingle,
				Font = new Font("Consolas", 9.5f), BackColor = Color.White, DetectUrls = false
			};
		}

		public static void Fill(RichTextBox rtb, string unified)
		{
			rtb.SuspendLayout();
			rtb.Clear();
			if (unified.Trim().Length == 0)
			{
				rtb.SelectionColor = Color.Gray;
				rtb.AppendText(L.T("(различий нет)"));
			}
			else
			{
				foreach (string line in unified.Split('\n'))
				{
					if (line.Length == 0) continue;
					Color c = line.StartsWith("- ") ? Color.FromArgb(180, 30, 30)
						: line.StartsWith("+ ") ? Color.FromArgb(20, 130, 40)
						: line.StartsWith("@@") ? Color.Gray : Color.Black;
					rtb.SelectionStart = rtb.TextLength;
					rtb.SelectionColor = c;
					rtb.AppendText(line + "\n");
				}
			}
			rtb.SelectionStart = 0;
			rtb.ResumeLayout();
		}
	}

	/// <summary>Plain window showing a diff between two texts.</summary>
	public class DiffForm : Form
	{
		public DiffForm(string title, string caption, string a, string b)
		{
			Text = title; Width = 900; Height = 600; StartPosition = FormStartPosition.CenterParent;
			Label l = new Label { Text = caption, Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 8, 8, 0) };
			RichTextBox r = DiffView.Create();
			Controls.Add(r); Controls.Add(l);
			DiffView.Fill(r, Diff.Unified(a, b, 3));
		}
	}
}
