using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FtpSync
{
	/// <summary>Draws a TermScreen and turns keys / mouse / clipboard into bytes for the host.</summary>
	public class TerminalControl : Control
	{
		public TermScreen Screen;
		public bool Highlight = true;
		public event Action<byte[]> UserInput;
		public event Action<int, int> GridResized;
		public event Action EnterPressed;
		public event Action<string> AddFavorite;

		static readonly Color DefBg = Color.FromArgb(30, 30, 30), DefFg = Color.FromArgb(204, 204, 204), SelBg = Color.FromArgb(38, 79, 120);
		static readonly int[] Ansi = {
			0x000000, 0xCD3131, 0x0DBC79, 0xE5E510, 0x2472C8, 0xBC3FBC, 0x11A8CD, 0xE5E5E5,
			0x666666, 0xF14C4C, 0x23D18B, 0xF5F543, 0x3B8EEA, 0xD670D6, 0x29B8DB, 0xFFFFFF };

		readonly VScrollBar sb = new VScrollBar { Dock = DockStyle.Right, Minimum = 0, SmallChange = 1 };
		readonly System.Windows.Forms.Timer blink = new System.Windows.Forms.Timer { Interval = 530 };
		readonly Dictionary<string, int[]> hlCache = new Dictionary<string, int[]>();
		readonly Dictionary<int, Font> fonts = new Dictionary<int, Font>();
		string fontName; float fontSize = 10f;
		int cw = 8, ch = 16, scrollOff;
		bool cursorOn = true, dragging, haveSel, ignoreScroll;
		int selA, selAc, selB, selBc;          // absolute row / column of the anchor and the caret
		int pending;
		char highSurrogate;
		static readonly Encoding Utf8 = new UTF8Encoding(false);

		public TerminalControl()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.Opaque, true);
			TabStop = true; BackColor = DefBg; Cursor = Cursors.IBeam;
			foreach (string n in new[] { "Cascadia Mono", "Consolas", "Courier New" })
				using (FontFamily f = TryFamily(n)) if (f != null) { fontName = n; break; }
			if (fontName == null) fontName = "Courier New";
			MeasureFont();
			Controls.Add(sb);
			sb.Scroll += delegate { if (ignoreScroll) return; SetScrollOffFromBar(); Invalidate(); };
			blink.Tick += delegate { cursorOn = !cursorOn; if (Focused) Invalidate(CursorRect()); };
			blink.Start();
			ContextMenuStrip = BuildMenu();
			Screen = new TermScreen(80, 24);
			Attach();
		}

		static FontFamily TryFamily(string n) { try { return new FontFamily(n); } catch (ArgumentException) { return null; } }

		public void SetScreen(TermScreen s) { Screen = s; Attach(); ComputeGrid(true); }

		void Attach()
		{
			Screen.Changed += delegate { if (Interlocked.Exchange(ref pending, 1) == 0 && IsHandleCreated) { try { BeginInvoke(new Action(UiRefresh)); } catch (InvalidOperationException) { } } };
		}

		void UiRefresh()
		{
			Interlocked.Exchange(ref pending, 0);
			if (IsDisposed) return;
			if (scrollOff > 0 && Screen.ScrollbackCount == 0) scrollOff = 0;
			SyncScrollBar();
			Invalidate();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing) { blink.Stop(); blink.Dispose(); foreach (Font f in fonts.Values) f.Dispose(); }
			base.Dispose(disposing);
		}

		// ------------------------------------------------------------------ geometry
		Font GetFont(byte attr)
		{
			int key = ((attr & TermScreen.Bold) != 0 ? 1 : 0) | ((attr & TermScreen.Italic) != 0 ? 2 : 0);
			Font f;
			if (!fonts.TryGetValue(key, out f))
			{
				FontStyle st = FontStyle.Regular; if ((key & 1) != 0) st |= FontStyle.Bold; if ((key & 2) != 0) st |= FontStyle.Italic;
				f = new Font(fontName, fontSize, st, GraphicsUnit.Point); fonts[key] = f;
			}
			return f;
		}

		void MeasureFont()
		{
			foreach (Font f in fonts.Values) f.Dispose();
			fonts.Clear();
			Font r = GetFont(0);
			Size sz = TextRenderer.MeasureText("MMMMMMMMMM", r, new Size(10000, 1000), TextFormatFlags.NoPadding);
			cw = Math.Max(4, (int)Math.Round(sz.Width / 10.0)); ch = Math.Max(8, sz.Height);
		}

		void ComputeGrid(bool force)
		{
			int cols = Math.Max(2, (ClientSize.Width - sb.Width) / cw), rows = Math.Max(1, ClientSize.Height / ch);
			if (!force && cols == Screen.Cols && rows == Screen.Rows) return;
			Screen.Resize(cols, rows);
			Action<int, int> g = GridResized; if (g != null) g(cols, rows);
			SyncScrollBar();
		}

		protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if (Screen != null) ComputeGrid(false); Invalidate(); }

		public int Cols { get { return Screen.Cols; } }
		public int Rows { get { return Screen.Rows; } }

		void SyncScrollBar()
		{
			int total = Screen.TotalLines, rows = Screen.Rows;
			ignoreScroll = true;
			try
			{
				sb.Maximum = Math.Max(0, total - 1); sb.LargeChange = Math.Max(1, rows);
				scrollOff = Math.Max(0, Math.Min(scrollOff, Math.Max(0, total - rows)));
				sb.Value = Math.Max(0, Math.Min(sb.Maximum - sb.LargeChange + 1, total - rows - scrollOff));
				sb.Enabled = total > rows;
			}
			catch (ArgumentException) { }
			finally { ignoreScroll = false; }
		}

		void SetScrollOffFromBar() { scrollOff = Math.Max(0, Screen.TotalLines - Screen.Rows - sb.Value); }

		void ScrollBy(int lines) // positive = up into history
		{
			scrollOff = Math.Max(0, Math.Min(Math.Max(0, Screen.TotalLines - Screen.Rows), scrollOff + lines));
			SyncScrollBar(); Invalidate();
		}

		// ------------------------------------------------------------------ colours
		static Color Rgb(int rgb) { return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

		static Color Palette(int n)
		{
			if (n < 16) return Rgb(Ansi[n]);
			if (n < 232) { n -= 16; int r = n / 36, g = (n / 6) % 6, b = n % 6; Func<int, int> f = v => v == 0 ? 0 : 55 + v * 40; return Color.FromArgb(f(r), f(g), f(b)); }
			int l = 8 + (n - 232) * 10; return Color.FromArgb(l, l, l);
		}

		static Color Resolve(int c, bool fg, bool bold)
		{
			if (c < 0) return fg ? DefFg : DefBg;
			if ((c & 0x1000000) != 0) return Rgb(c & 0xFFFFFF);
			if (fg && bold && c < 8) c += 8;
			return Palette(c);
		}

		// ------------------------------------------------------------------ painting
		static string TextOf(TermRow r) { char[] a = new char[r.C.Length]; for (int i = 0; i < a.Length; i++) a[i] = r.C[i].Ch; return new string(a); }

		int[] Spans(string text)
		{
			int[] r;
			if (hlCache.TryGetValue(text, out r)) return r;
			if (hlCache.Count > 3000) hlCache.Clear();
			r = ShellHighlight.Spans(text); hlCache[text] = r; return r;
		}

		bool Selected(int absRow, int col)
		{
			if (!haveSel) return false;
			int r1 = selA, c1 = selAc, r2 = selB, c2 = selBc;
			if (r1 > r2 || (r1 == r2 && c1 > c2)) { int t = r1; r1 = r2; r2 = t; t = c1; c1 = c2; c2 = t; }
			if (absRow < r1 || absRow > r2) return false;
			if (absRow == r1 && col < c1) return false;
			if (absRow == r2 && col > c2) return false;
			return true;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics g = e.Graphics;
			g.Clear(DefBg);
			lock (Screen.Sync)
			{
				int rows = Screen.Rows, cols = Screen.Cols, total = Screen.TotalLines;
				int topIdx = total - rows - scrollOff;
				bool hl = Highlight && !Screen.Alt;
				for (int vr = 0; vr < rows; vr++)
				{
					int y = vr * ch;
					if (y > e.ClipRectangle.Bottom || y + ch < e.ClipRectangle.Top) continue;
					TermRow row = Screen.RowAt(topIdx + vr);
					if (row == null) continue;
					DrawRow(g, row, topIdx + vr, y, Math.Min(cols, row.C.Length), hl);
				}
				if (scrollOff == 0 && Screen.CursorVisible && Screen.CY < rows)
				{
					Rectangle cr = CursorRect();
					TermRow row = Screen.RowAt(topIdx + Screen.CY);
					if (Focused)
					{
						if (cursorOn)
						{
							using (SolidBrush b = new SolidBrush(Color.FromArgb(220, 220, 220))) g.FillRectangle(b, cr);
							if (row != null && Screen.CX < row.C.Length) { char c = row.C[Screen.CX].Ch; if (c > ' ') TextRenderer.DrawText(g, c.ToString(), GetFont(0), new Point(cr.X, cr.Y), DefBg, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix); }
						}
					}
					else using (Pen p = new Pen(Color.FromArgb(200, 200, 200))) g.DrawRectangle(p, cr.X, cr.Y, cr.Width - 1, cr.Height - 1);
				}
			}
		}

		Rectangle CursorRect() { return new Rectangle(Screen.CX * cw, Screen.CY * ch, cw, ch); }

		void DrawRow(Graphics g, TermRow row, int absRow, int y, int n, bool hl)
		{
			int[] spans = hl ? Spans(TextOf(row)) : null;
			Color[] fg = new Color[n], bg = new Color[n];
			for (int x = 0; x < n; x++)
			{
				Cell c = row.C[x];
				bool rev = (c.Attr & TermScreen.Reverse) != 0, bold = (c.Attr & TermScreen.Bold) != 0;
				Color f = Resolve(c.Fg, true, bold), b = Resolve(c.Bg, false, false);
				if (spans != null && c.Fg < 0 && !rev && x < spans.Length && spans[x] != 0) f = Rgb(spans[x] & 0xFFFFFF);
				if ((c.Attr & TermScreen.Dim) != 0) f = Color.FromArgb((f.R + b.R * 2) / 3, (f.G + b.G * 2) / 3, (f.B + b.B * 2) / 3);
				if (rev) { Color t = f; f = b; b = t; if (c.Fg < 0 && c.Bg < 0) { f = DefBg; b = DefFg; } }
				if (Selected(absRow, x)) b = SelBg;
				fg[x] = f; bg[x] = b;
			}
			// backgrounds
			int s = 0;
			while (s < n)
			{
				int e = s + 1;
				while (e < n && bg[e] == bg[s]) e++;
				if (bg[s] != DefBg) using (SolidBrush br = new SolidBrush(bg[s])) g.FillRectangle(br, s * cw, y, (e - s) * cw + (e == n ? sb.Width : 0), ch);
				s = e;
			}
			// text
			StringBuilder run = new StringBuilder();
			int runStart = 0; Color runColor = Color.Empty; byte runAttr = 0;
			Action flush = delegate
			{
				if (run.Length == 0) return;
				string t = run.ToString();
				if (t.Trim().Length > 0 || (runAttr & (TermScreen.Underline | TermScreen.Strike)) != 0)
					TextRenderer.DrawText(g, t, GetFont(runAttr), new Point(runStart * cw, y), runColor, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
				if ((runAttr & TermScreen.Underline) != 0) using (Pen p = new Pen(runColor)) g.DrawLine(p, runStart * cw, y + ch - 2, (runStart + t.Length) * cw, y + ch - 2);
				if ((runAttr & TermScreen.Strike) != 0) using (Pen p = new Pen(runColor)) g.DrawLine(p, runStart * cw, y + ch / 2, (runStart + t.Length) * cw, y + ch / 2);
				run.Length = 0;
			};
			for (int x = 0; x < n; x++)
			{
				Cell c = row.C[x];
				if ((c.Attr & TermScreen.WideTail) != 0) continue;
				byte style = (byte)(c.Attr & (TermScreen.Bold | TermScreen.Italic | TermScreen.Underline | TermScreen.Strike));
				bool plain = c.Ch >= ' ' && c.Ch < 0x7F;
				if (!plain)
				{
					flush();
					if (c.Ch > ' ') TextRenderer.DrawText(g, c.Ch.ToString(), GetFont(style), new Point(x * cw, y), fg[x], TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
					continue;
				}
				if (run.Length > 0 && (fg[x] != runColor || style != runAttr)) flush();
				if (run.Length == 0) { runStart = x; runColor = fg[x]; runAttr = style; }
				run.Append(c.Ch);
			}
			flush();
		}

		// ------------------------------------------------------------------ keyboard
		protected override bool IsInputKey(Keys keyData) { return true; }
		protected override bool IsInputChar(char charCode) { return true; }

		void Send(byte[] b) { Action<byte[]> a = UserInput; if (a != null) a(b); }
		void Send(string s) { Send(Utf8.GetBytes(s)); }

		/// <summary>Text typed into the terminal as if with the keyboard (no Enter).</summary>
		public void Type(string s) { scrollOff = 0; Send(s); Focus(); }

		static string Mod(Keys k) { int m = 1 + ((k & Keys.Shift) != 0 ? 1 : 0) + ((k & Keys.Alt) != 0 ? 2 : 0) + ((k & Keys.Control) != 0 ? 4 : 0); return m == 1 ? "" : m.ToString(); }

		string Csi(string letterOrNum, char final, Keys k)
		{
			string m = Mod(k);
			if (m.Length == 0) return "\x1b[" + (letterOrNum == "1" && final >= 'A' ? "" : letterOrNum) + final;
			return "\x1b[" + letterOrNum + ";" + m + final;
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			Keys k = e.KeyCode, all = e.KeyData;
			bool ctrl = e.Control, alt = e.Alt, shift = e.Shift;
			if (ctrl && alt && !shift) return; // AltGr: the character arrives through KeyPress
			string seq = null;
			bool app = Screen.AppCursorKeys && !ctrl && !alt && !shift;
			switch (k)
			{
				case Keys.Up: seq = app ? "\x1bOA" : Csi("1", 'A', all); break;
				case Keys.Down: seq = app ? "\x1bOB" : Csi("1", 'B', all); break;
				case Keys.Right: seq = app ? "\x1bOC" : Csi("1", 'C', all); break;
				case Keys.Left: seq = app ? "\x1bOD" : Csi("1", 'D', all); break;
				case Keys.Home: seq = app ? "\x1bOH" : Csi("1", 'H', all); break;
				case Keys.End: seq = app ? "\x1bOF" : Csi("1", 'F', all); break;
				case Keys.Insert: if (shift && !ctrl) { Paste(); e.SuppressKeyPress = true; e.Handled = true; return; } seq = Csi("2", '~', all); break;
				case Keys.Delete: seq = Csi("3", '~', all); break;
				case Keys.PageUp:
					if (shift && !Screen.Alt) { ScrollBy(Screen.Rows - 1); e.SuppressKeyPress = true; e.Handled = true; return; }
					seq = Csi("5", '~', all); break;
				case Keys.PageDown:
					if (shift && !Screen.Alt) { ScrollBy(-(Screen.Rows - 1)); e.SuppressKeyPress = true; e.Handled = true; return; }
					seq = Csi("6", '~', all); break;
				case Keys.F1: seq = Mod(all).Length == 0 ? "\x1bOP" : "\x1b[1;" + Mod(all) + "P"; break;
				case Keys.F2: seq = Mod(all).Length == 0 ? "\x1bOQ" : "\x1b[1;" + Mod(all) + "Q"; break;
				case Keys.F3: seq = Mod(all).Length == 0 ? "\x1bOR" : "\x1b[1;" + Mod(all) + "R"; break;
				case Keys.F4: seq = Mod(all).Length == 0 ? "\x1bOS" : "\x1b[1;" + Mod(all) + "S"; break;
				case Keys.F5: seq = Csi("15", '~', all); break;
				case Keys.F6: seq = Csi("17", '~', all); break;
				case Keys.F7: seq = Csi("18", '~', all); break;
				case Keys.F8: seq = Csi("19", '~', all); break;
				case Keys.F9: seq = Csi("20", '~', all); break;
				case Keys.F10: seq = Csi("21", '~', all); break;
				case Keys.F11: seq = Csi("23", '~', all); break;
				case Keys.F12: seq = Csi("24", '~', all); break;
				case Keys.Enter:
					{ Action en = EnterPressed; if (en != null) en(); seq = alt ? "\x1b\r" : "\r"; break; }
				case Keys.Back: seq = ctrl ? "\x17" : alt ? "\x1b\x7f" : "\x7f"; break;
				case Keys.Tab: seq = shift ? "\x1b[Z" : "\t"; break;
				case Keys.Escape: seq = "\x1b"; break;
				case Keys.Space: if (ctrl) seq = "\0"; break;
			}
			if (seq == null && ctrl)
			{
				if (shift && k == Keys.C) { Copy(); Done(e); return; }
				if (shift && k == Keys.V) { Paste(); Done(e); return; }
				if (k == Keys.V) { Paste(); Done(e); return; }
				if (k == Keys.C && haveSel) { Copy(); haveSel = false; Invalidate(); Done(e); return; }
				if (k >= Keys.A && k <= Keys.Z) seq = ((char)(k - Keys.A + 1)).ToString();
				else if (k == Keys.OemOpenBrackets) seq = "\x1b";
				else if (k == Keys.OemBackslash || k == Keys.Oem5) seq = "\x1c";
				else if (k == Keys.OemCloseBrackets) seq = "\x1d";
				else if (k == Keys.D6) seq = "\x1e";
				else if (k == Keys.OemMinus || k == Keys.OemQuestion) seq = "\x1f";
				else if (k == Keys.Add || k == Keys.Oemplus) { Zoom(1); Done(e); return; }
				else if (k == Keys.Subtract) { Zoom(-1); Done(e); return; }
			}
			if (seq == null && alt && !ctrl)
			{
				char c = '\0';
				if (k >= Keys.A && k <= Keys.Z) c = (char)((shift ? 'A' : 'a') + (k - Keys.A));
				else if (k >= Keys.D0 && k <= Keys.D9) c = (char)('0' + (k - Keys.D0));
				if (c != '\0') seq = "\x1b" + c;
			}
			if (seq == null) return;
			if (haveSel) { haveSel = false; Invalidate(); }
			scrollOff = 0; SyncScrollBar();
			Send(seq);
			Done(e);
		}

		static void Done(KeyEventArgs e) { e.Handled = true; e.SuppressKeyPress = true; }

		protected override void OnKeyPress(KeyPressEventArgs e)
		{
			base.OnKeyPress(e);
			char c = e.KeyChar;
			if (c < ' ' || c == 127) { e.Handled = true; return; }
			e.Handled = true;
			if (char.IsHighSurrogate(c)) { highSurrogate = c; return; }
			string s = char.IsLowSurrogate(c) && highSurrogate != '\0' ? new string(new[] { highSurrogate, c }) : c.ToString();
			highSurrogate = '\0';
			if (haveSel) { haveSel = false; Invalidate(); }
			scrollOff = 0; SyncScrollBar();
			Send(s);
		}

		void Zoom(int d)
		{
			fontSize = Math.Max(7f, Math.Min(26f, fontSize + d));
			MeasureFont(); ComputeGrid(true); Invalidate();
		}

		// ------------------------------------------------------------------ clipboard / selection
		public string SelectedText()
		{
			if (!haveSel) return "";
			int r1 = selA, c1 = selAc, r2 = selB, c2 = selBc;
			if (r1 > r2 || (r1 == r2 && c1 > c2)) { int t = r1; r1 = r2; r2 = t; t = c1; c1 = c2; c2 = t; }
			StringBuilder sbd = new StringBuilder();
			lock (Screen.Sync)
				for (int r = r1; r <= r2; r++)
				{
					TermRow row = Screen.RowAt(r); if (row == null) continue;
					int from = r == r1 ? c1 : 0, to = r == r2 ? c2 : row.C.Length - 1;
					StringBuilder line = new StringBuilder();
					for (int x = from; x <= to && x < row.C.Length; x++) if ((row.C[x].Attr & TermScreen.WideTail) == 0) line.Append(row.C[x].Ch);
					string t = line.ToString();
					if (!row.Wrapped || r == r2) t = t.TrimEnd();
					sbd.Append(t);
					if (r != r2 && !row.Wrapped) sbd.Append("\r\n");
				}
			return sbd.ToString();
		}

		public void Copy()
		{
			string t = SelectedText();
			if (t.Length == 0) return;
			try { Clipboard.SetText(t); } catch (Exception) { }
		}

		public void Paste()
		{
			string t;
			try { if (!Clipboard.ContainsText()) return; t = Clipboard.GetText(); } catch (Exception) { return; }
			if (string.IsNullOrEmpty(t)) return;
			t = t.Replace("\r\n", "\r").Replace('\n', '\r');
			if (t.IndexOf('\r') >= 0 && t.TrimEnd('\r').IndexOf('\r') >= 0 &&
				MessageBox.Show(this, L.T("Вставить несколько строк? Каждая строка будет выполнена."), "FTP Sync", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
			scrollOff = 0; SyncScrollBar();
			Send(Screen.BracketedPaste ? "\x1b[200~" + t + "\x1b[201~" : t);
		}

		public void SelectAll()
		{
			lock (Screen.Sync) { selA = 0; selAc = 0; selB = Screen.TotalLines - 1; selBc = Screen.Cols - 1; }
			haveSel = true; Invalidate();
		}

		ContextMenuStrip BuildMenu()
		{
			ContextMenuStrip m = new ContextMenuStrip();
			ToolStripItem iCopy = m.Items.Add(L.T("Копировать"), null, delegate { Copy(); });
			m.Items.Add(L.T("Вставить"), null, delegate { Paste(); });
			m.Items.Add(L.T("Выделить всё"), null, delegate { SelectAll(); });
			ToolStripItem iFav = m.Items.Add(L.T("Выделенное в избранные команды"), null, delegate { Action<string> a = AddFavorite; string t = SelectedText().Trim(); if (a != null && t.Length > 0) a(t); });
			m.Items.Add(new ToolStripSeparator());
			m.Items.Add(L.T("Очистить историю прокрутки"), null, delegate { Screen.ClearScrollback(); scrollOff = 0; SyncScrollBar(); Invalidate(); });
			m.Opening += delegate { bool has = haveSel && SelectedText().Length > 0; iCopy.Enabled = has; iFav.Enabled = has; };
			return m;
		}

		// ------------------------------------------------------------------ mouse
		bool HitCell(MouseEventArgs e, out int absRow, out int col)
		{
			col = Math.Max(0, Math.Min(Screen.Cols - 1, e.X / cw));
			int vr = Math.Max(0, Math.Min(Screen.Rows - 1, e.Y / ch));
			absRow = Screen.TotalLines - Screen.Rows - scrollOff + vr;
			return true;
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			Focus();
			base.OnMouseDown(e);
			if (e.Button != MouseButtons.Left) return;
			int r, c; HitCell(e, out r, out c);
			selA = selB = r; selAc = selBc = c; haveSel = false; dragging = true;
			Invalidate();
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if (!dragging) return;
			int r, c; HitCell(e, out r, out c);
			if (r == selB && c == selBc) return;
			selB = r; selBc = c; haveSel = !(selA == selB && selAc == selBc);
			if (e.Y < 0) ScrollBy(1); else if (e.Y > ClientSize.Height) ScrollBy(-1);
			Invalidate();
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			dragging = false;
		}

		protected override void OnMouseDoubleClick(MouseEventArgs e)
		{
			base.OnMouseDoubleClick(e);
			if (e.Button != MouseButtons.Left) return;
			int r, c; HitCell(e, out r, out c);
			lock (Screen.Sync)
			{
				TermRow row = Screen.RowAt(r); if (row == null) return;
				Func<int, bool> word = x => x >= 0 && x < row.C.Length && !char.IsWhiteSpace(row.C[x].Ch);
				if (!word(c)) return;
				int a = c, b = c;
				while (word(a - 1)) a--;
				while (word(b + 1)) b++;
				selA = selB = r; selAc = a; selBc = b; haveSel = true;
			}
			Invalidate();
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			base.OnMouseWheel(e);
			int steps = Math.Max(1, Math.Abs(e.Delta) / 120) * 3;
			if ((Control.ModifierKeys & Keys.Control) != 0) { Zoom(e.Delta > 0 ? 1 : -1); return; }
			if (Screen.Alt)
			{
				string a = Screen.AppCursorKeys ? (e.Delta > 0 ? "\x1bOA" : "\x1bOB") : (e.Delta > 0 ? "\x1b[A" : "\x1b[B");
				for (int i = 0; i < steps; i++) Send(a);
				return;
			}
			ScrollBy(e.Delta > 0 ? steps : -steps);
		}

		protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); cursorOn = true; Invalidate(CursorRect()); }
		protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(CursorRect()); }
	}
}
