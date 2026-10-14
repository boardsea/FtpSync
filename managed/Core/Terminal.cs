using System;
using System.Collections.Generic;
using System.Text;

namespace FtpSync
{
	public struct Cell
	{
		public char Ch;
		public int Fg, Bg;     // -1 default, 0..255 palette, 0x1000000 | 0xRRGGBB true color
		public byte Attr;
	}

	public class TermRow
	{
		public Cell[] C;
		public bool Wrapped;   // the line continues on the next row (soft wrap)
		public TermRow(int cols, int bg) { C = new Cell[cols]; for (int i = 0; i < cols; i++) C[i] = Blank(bg); }
		public static Cell Blank(int bg) { return new Cell { Ch = ' ', Fg = -1, Bg = bg, Attr = 0 }; }
		public TermRow Clone() { TermRow r = new TermRow(0, -1); r.C = (Cell[])C.Clone(); r.Wrapped = Wrapped; return r; }
	}

	/// <summary>Terminal emulator model (xterm / VT100 subset used by shells, less, nano, vim, top, mc, htop): screen, scrollback, escape sequence parser.</summary>
	public class TermScreen
	{
		public const byte Bold = 1, Dim = 2, Italic = 4, Underline = 8, Reverse = 16, Strike = 32, WideTail = 64;

		public readonly object Sync = new object();
		public int Cols { get; private set; }
		public int Rows { get; private set; }
		public int CX, CY;
		public bool CursorVisible = true, AppCursorKeys, BracketedPaste, AutoWrap = true, Alt;
		public string Title = "";
		public int MaxScrollback = 5000;
		public event Action<string> Reply;      // answers to the host (cursor position report, device attributes)
		public event Action Changed;
		public event Action<string> TitleChanged;

		TermRow[] main, altRows, grid;
		public readonly List<TermRow> Scrollback = new List<TermRow>();
		int top, bottom;
		bool wrapPending, originMode, insertMode;
		int pFg = -1, pBg = -1; byte pAttr;
		int savedX, savedY, savedFg = -1, savedBg = -1; byte savedAttr; bool savedWrap;
		int altSavedX, altSavedY;
		bool[] tabs;
		readonly Decoder dec = new UTF8Encoding(false).GetDecoder();
		int state; // 0 ground, 1 esc, 2 csi, 3 osc, 4 string(ignored), 5 esc-intermediate, 6 esc inside osc/string
		readonly StringBuilder csi = new StringBuilder(), osc = new StringBuilder();
		char lastPrinted;

		public TermScreen(int cols, int rows)
		{
			Cols = Math.Max(2, cols); Rows = Math.Max(1, rows);
			main = NewGrid(); altRows = NewGrid(); grid = main;
			top = 0; bottom = Rows - 1; tabs = NewTabs();
		}

		TermRow[] NewGrid() { TermRow[] g = new TermRow[Rows]; for (int i = 0; i < Rows; i++) g[i] = new TermRow(Cols, -1); return g; }
		bool[] NewTabs() { bool[] t = new bool[Cols]; for (int i = 8; i < Cols; i += 8) t[i] = true; return t; }

		// ------------------------------------------------------------------ access
		public int TotalLines { get { return (Alt ? 0 : Scrollback.Count) + Rows; } }

		/// <summary>Row by absolute index (scrollback first, then the screen).</summary>
		public TermRow RowAt(int index)
		{
			int sb = Alt ? 0 : Scrollback.Count;
			if (index < sb) return Scrollback[index];
			index -= sb;
			return index >= 0 && index < Rows ? grid[index] : null;
		}

		public int ScrollbackCount { get { return Alt ? 0 : Scrollback.Count; } }

		public string RowText(TermRow r)
		{
			StringBuilder sb = new StringBuilder(r.C.Length);
			foreach (Cell c in r.C) if ((c.Attr & WideTail) == 0) sb.Append(c.Ch);
			return sb.ToString().TrimEnd();
		}

		/// <summary>Text of the logical line (joined soft-wrapped rows) that contains the screen row.</summary>
		public string LogicalLineAtScreenRow(int row)
		{
			int s = row;
			while (s > 0 && grid[s - 1].Wrapped) s--;
			StringBuilder sb = new StringBuilder();
			for (int i = s; i < Rows; i++)
			{
				string t = RowText(grid[i]);
				sb.Append(grid[i].Wrapped ? PadRow(grid[i]) : t);
				if (!grid[i].Wrapped) break;
			}
			return sb.ToString();
		}

		string PadRow(TermRow r) { StringBuilder sb = new StringBuilder(); foreach (Cell c in r.C) if ((c.Attr & WideTail) == 0) sb.Append(c.Ch); return sb.ToString(); }

		// ------------------------------------------------------------------ resize
		public void Resize(int cols, int rows)
		{
			lock (Sync)
			{
				cols = Math.Max(2, cols); rows = Math.Max(1, rows);
				if (cols == Cols && rows == Rows) return;
				TermRow[] m = ResizeGrid(main, cols, rows, !Alt, true);
				TermRow[] a = ResizeGrid(altRows, cols, rows, Alt, false);
				int oldRows = Rows;
				Cols = cols; Rows = rows; main = m; altRows = a; grid = Alt ? altRows : main;
				top = 0; bottom = Rows - 1; tabs = NewTabs();
				CX = Math.Min(CX, Cols - 1); CY = Math.Max(0, Math.Min(CY - Math.Max(0, shrink), Rows - 1)); wrapPending = false;
				shrink = 0;
			}
			Notify();
		}

		int shrink;

		TermRow[] ResizeGrid(TermRow[] g, int cols, int rows, bool active, bool keepScrollback)
		{
			List<TermRow> list = new List<TermRow>(g);
			if (rows < list.Count)
			{
				int drop = list.Count - rows;
				// keep the cursor row visible: remove from the top (into scrollback) while the cursor is below the new bottom
				int fromTop = active ? Math.Min(drop, Math.Max(0, CY - (rows - 1))) : 0;
				if (active) shrink = fromTop;
				for (int i = 0; i < fromTop; i++) { if (keepScrollback) PushScrollback(list[0]); list.RemoveAt(0); }
				while (list.Count > rows) list.RemoveAt(list.Count - 1);
			}
			while (list.Count < rows) list.Add(new TermRow(cols, -1));
			for (int i = 0; i < list.Count; i++)
			{
				TermRow r = list[i];
				if (r.C.Length != cols)
				{
					Cell[] n = new Cell[cols];
					for (int k = 0; k < cols; k++) n[k] = k < r.C.Length ? r.C[k] : TermRow.Blank(-1);
					r.C = n;
				}
			}
			return list.ToArray();
		}

		void PushScrollback(TermRow r)
		{
			Scrollback.Add(r);
			if (Scrollback.Count > MaxScrollback) Scrollback.RemoveRange(0, Scrollback.Count - MaxScrollback);
		}

		public void ClearScrollback() { lock (Sync) Scrollback.Clear(); Notify(); }

		// ------------------------------------------------------------------ input from the host
		public void Feed(byte[] data, int count)
		{
			char[] chars = new char[count + 4];
			lock (Sync)
			{
				int n = dec.GetChars(data, 0, count, chars, 0);
				for (int i = 0; i < n; i++) Process(chars[i]);
			}
			Notify();
		}

		void Notify() { Action a = Changed; if (a != null) a(); }
		void Send(string s) { Action<string> r = Reply; if (r != null) r(s); }

		void Process(char c)
		{
			switch (state)
			{
				case 0: Ground(c); return;
				case 1: EscChar(c); return;
				case 2: CsiChar(c); return;
				case 3:
					if (c == '\a') { EndOsc(); state = 0; }
					else if (c == '\x1b') state = 6;
					else if (osc.Length < 2048) osc.Append(c);
					return;
				case 4:
					if (c == '\a') state = 0; else if (c == '\x1b') state = 6;
					return;
				case 5: state = 0; return; // ESC ( B and friends: skip the designator
				case 6:
					if (c == '\\') { if (osc.Length > 0) EndOsc(); state = 0; }
					else { state = 0; EscChar(c); }
					return;
			}
		}

		void EndOsc()
		{
			string s = osc.ToString(); osc.Length = 0;
			int semi = s.IndexOf(';');
			if (semi > 0 && (s.Substring(0, semi) == "0" || s.Substring(0, semi) == "2"))
			{
				Title = s.Substring(semi + 1);
				Action<string> t = TitleChanged; if (t != null) t(Title);
			}
		}

		void Ground(char c)
		{
			if (c < 32 || c == 127)
			{
				switch (c)
				{
					case '\x1b': state = 1; return;
					case '\b': if (CX > 0) CX--; wrapPending = false; return;
					case '\t': { int x = CX + 1; while (x < Cols - 1 && !tabs[x]) x++; CX = Math.Min(x, Cols - 1); wrapPending = false; return; }
					case '\n': case '\v': case '\f': Index(); return;
					case '\r': CX = 0; wrapPending = false; return;
					default: return; // BEL, SO, SI, ...
				}
			}
			if (c >= 0x80 && c < 0xA0) return;
			Put(c);
		}

		static int Width(char c)
		{
			if (c < 0x300) return 1;
			if ((c >= 0x300 && c <= 0x36F) || (c >= 0x200B && c <= 0x200F) || c == 0xFEFF || (c >= 0xFE00 && c <= 0xFE0F)) return 0;
			if ((c >= 0x1100 && c <= 0x115F) || (c >= 0x2E80 && c <= 0xA4CF) || (c >= 0xAC00 && c <= 0xD7A3) || (c >= 0xF900 && c <= 0xFAFF) ||
				(c >= 0xFE30 && c <= 0xFE6F) || (c >= 0xFF00 && c <= 0xFF60) || (c >= 0xFFE0 && c <= 0xFFE6)) return 2;
			return 1;
		}

		void Put(char c)
		{
			if (char.IsSurrogate(c)) c = '□';
			int w = Width(c);
			if (w == 0) return;
			if (wrapPending && AutoWrap) { grid[CY].Wrapped = true; CX = 0; Index(); wrapPending = false; }
			if (CX + w > Cols) { if (AutoWrap) { grid[CY].Wrapped = true; CX = 0; Index(); } else CX = Cols - w; }
			Cell[] row = grid[CY].C;
			if (insertMode) for (int i = Cols - 1; i >= CX + w; i--) row[i] = row[i - w];
			ClearWideAt(row, CX); if (w == 2) ClearWideAt(row, CX + 1);
			row[CX] = new Cell { Ch = c, Fg = pFg, Bg = pBg, Attr = pAttr };
			if (w == 2) row[CX + 1] = new Cell { Ch = ' ', Fg = pFg, Bg = pBg, Attr = (byte)(pAttr | WideTail) };
			lastPrinted = c;
			CX += w;
			if (CX >= Cols) { CX = Cols - 1; wrapPending = true; }
		}

		void ClearWideAt(Cell[] row, int x)
		{
			if (x < 0 || x >= row.Length) return;
			if ((row[x].Attr & WideTail) != 0 && x > 0) row[x - 1] = TermRow.Blank(pBg);
			if (x + 1 < row.Length && (row[x + 1].Attr & WideTail) != 0) row[x + 1] = TermRow.Blank(pBg);
		}

		// ------------------------------------------------------------------ cursor / scrolling
		void Index()
		{
			if (CY == bottom) ScrollUp(1);
			else if (CY < Rows - 1) CY++;
		}

		void ReverseIndex()
		{
			if (CY == top) ScrollDown(1);
			else if (CY > 0) CY--;
		}

		void ScrollUp(int n)
		{
			n = Math.Min(n, bottom - top + 1);
			for (int k = 0; k < n; k++)
			{
				TermRow gone = grid[top];
				if (top == 0 && !Alt) { gone.Wrapped = false; PushScrollback(gone); }
				for (int i = top; i < bottom; i++) grid[i] = grid[i + 1];
				grid[bottom] = new TermRow(Cols, pBg);
			}
		}

		void ScrollDown(int n)
		{
			n = Math.Min(n, bottom - top + 1);
			for (int k = 0; k < n; k++)
			{
				for (int i = bottom; i > top; i--) grid[i] = grid[i - 1];
				grid[top] = new TermRow(Cols, pBg);
			}
		}

		void MoveTo(int row, int col)
		{
			int minR = originMode ? top : 0, maxR = originMode ? bottom : Rows - 1;
			CY = Math.Max(minR, Math.Min(maxR, row + (originMode ? top : 0)));
			CX = Math.Max(0, Math.Min(Cols - 1, col));
			wrapPending = false;
		}

		// ------------------------------------------------------------------ erase helpers
		void EraseCells(int row, int from, int to) // [from, to)
		{
			Cell[] r = grid[row].C;
			from = Math.Max(0, from); to = Math.Min(Cols, to);
			for (int i = from; i < to; i++) r[i] = TermRow.Blank(pBg);
			if (from == 0 && to >= Cols) grid[row].Wrapped = false;
		}

		// ------------------------------------------------------------------ ESC
		void EscChar(char c)
		{
			state = 0;
			switch (c)
			{
				case '[': csi.Length = 0; state = 2; return;
				case ']': osc.Length = 0; state = 3; return;
				case 'P': case 'X': case '^': case '_': osc.Length = 0; state = 4; return;
				case '(': case ')': case '*': case '+': case '#': case '%': case ' ': state = 5; return;
				case '7': SaveCursor(); return;
				case '8': RestoreCursor(); return;
				case 'M': ReverseIndex(); return;
				case 'D': Index(); return;
				case 'E': CX = 0; Index(); wrapPending = false; return;
				case 'H': if (CX < Cols) tabs[CX] = true; return;
				case 'c': FullReset(); return;
				default: return; // ESC = / ESC > / ESC \ ...
			}
		}

		void SaveCursor() { savedX = CX; savedY = CY; savedFg = pFg; savedBg = pBg; savedAttr = pAttr; savedWrap = wrapPending; }

		void RestoreCursor()
		{
			CX = Math.Min(savedX, Cols - 1); CY = Math.Min(savedY, Rows - 1);
			pFg = savedFg; pBg = savedBg; pAttr = savedAttr; wrapPending = savedWrap;
		}

		void FullReset()
		{
			pFg = -1; pBg = -1; pAttr = 0; CursorVisible = true; AppCursorKeys = false; BracketedPaste = false; AutoWrap = true; originMode = false; insertMode = false;
			if (Alt) SwitchAlt(false, false);
			top = 0; bottom = Rows - 1; CX = 0; CY = 0; wrapPending = false; tabs = NewTabs();
			for (int i = 0; i < Rows; i++) grid[i] = new TermRow(Cols, -1);
		}

		void SwitchAlt(bool on, bool saveCursor)
		{
			if (on == Alt) return;
			if (on)
			{
				if (saveCursor) { altSavedX = CX; altSavedY = CY; }
				Alt = true; grid = altRows;
				for (int i = 0; i < Rows; i++) grid[i] = new TermRow(Cols, -1);
			}
			else
			{
				Alt = false; grid = main;
				if (saveCursor) { CX = Math.Min(altSavedX, Cols - 1); CY = Math.Min(altSavedY, Rows - 1); }
			}
			top = 0; bottom = Rows - 1; wrapPending = false;
		}

		// ------------------------------------------------------------------ CSI
		void CsiChar(char c)
		{
			if (c >= 0x40 && c <= 0x7E) { state = 0; DispatchCsi(csi.ToString(), c); return; }
			if (c == '\x1b') { state = 1; return; }
			if (c < 0x20) { Ground(c); return; } // control characters inside a sequence are executed, the sequence goes on
			if (csi.Length < 64) csi.Append(c);
		}

		static int[] ParseParams(string s, out char marker, out bool hasSpace)
		{
			marker = '\0'; hasSpace = false;
			if (s.Length > 0 && (s[0] == '?' || s[0] == '>' || s[0] == '=' || s[0] == '<')) { marker = s[0]; s = s.Substring(1); }
			if (s.EndsWith(" ")) { hasSpace = true; s = s.TrimEnd(' '); }
			if (s.Length == 0) return new int[0];
			string[] parts = s.Split(';', ':');
			int[] r = new int[parts.Length];
			for (int i = 0; i < parts.Length; i++) { int v; r[i] = int.TryParse(parts[i], out v) ? v : -1; }
			return r;
		}

		static int P(int[] p, int i, int def) { return i < p.Length && p[i] >= 0 ? p[i] : def; }
		static int P1(int[] p, int i) { int v = P(p, i, 1); return v == 0 ? 1 : v; }

		void DispatchCsi(string raw, char cmd)
		{
			char marker; bool sp;
			int[] p = ParseParams(raw, out marker, out sp);
			if (sp) return; // DECSCUSR and friends
			switch (cmd)
			{
				case 'A': CY = Math.Max(CY < top ? 0 : top, CY - P1(p, 0)); wrapPending = false; break;
				case 'B': CY = Math.Min(CY > bottom ? Rows - 1 : bottom, CY + P1(p, 0)); wrapPending = false; break;
				case 'C': CX = Math.Min(Cols - 1, CX + P1(p, 0)); wrapPending = false; break;
				case 'D': CX = Math.Max(0, CX - P1(p, 0)); wrapPending = false; break;
				case 'E': CX = 0; CY = Math.Min(Rows - 1, CY + P1(p, 0)); wrapPending = false; break;
				case 'F': CX = 0; CY = Math.Max(0, CY - P1(p, 0)); wrapPending = false; break;
				case 'G': case '`': CX = Math.Max(0, Math.Min(Cols - 1, P1(p, 0) - 1)); wrapPending = false; break;
				case 'd': MoveTo(P1(p, 0) - 1, CX); break;
				case 'H': case 'f': MoveTo(P1(p, 0) - 1, P1(p, 1) - 1); break;
				case 'I': { int n = P1(p, 0); for (int i = 0; i < n; i++) { int x = CX + 1; while (x < Cols - 1 && !tabs[x]) x++; CX = Math.Min(x, Cols - 1); } break; }
				case 'Z': { int n = P1(p, 0); for (int i = 0; i < n; i++) { int x = CX - 1; while (x > 0 && !tabs[x]) x--; CX = Math.Max(x, 0); } break; }
				case 'J':
					{
						int m = P(p, 0, 0);
						if (m == 0) { EraseCells(CY, CX, Cols); for (int i = CY + 1; i < Rows; i++) EraseCells(i, 0, Cols); }
						else if (m == 1) { for (int i = 0; i < CY; i++) EraseCells(i, 0, Cols); EraseCells(CY, 0, CX + 1); }
						else if (m == 2) { for (int i = 0; i < Rows; i++) EraseCells(i, 0, Cols); }
						else if (m == 3 && !Alt) Scrollback.Clear();
						break;
					}
				case 'K':
					{
						int m = P(p, 0, 0);
						if (m == 0) EraseCells(CY, CX, Cols); else if (m == 1) EraseCells(CY, 0, CX + 1); else if (m == 2) EraseCells(CY, 0, Cols);
						break;
					}
				case 'L': if (CY >= top && CY <= bottom) { int save = top; top = CY; ScrollDown(P1(p, 0)); top = save; } break;
				case 'M': if (CY >= top && CY <= bottom) { int save = top; top = CY; ScrollUpNoHistory(P1(p, 0)); top = save; } break;
				case 'P': { int n = Math.Min(P1(p, 0), Cols - CX); Cell[] r = grid[CY].C; for (int i = CX; i < Cols; i++) r[i] = i + n < Cols ? r[i + n] : TermRow.Blank(pBg); break; }
				case '@': { int n = Math.Min(P1(p, 0), Cols - CX); Cell[] r = grid[CY].C; for (int i = Cols - 1; i >= CX; i--) r[i] = i - n >= CX ? r[i - n] : TermRow.Blank(pBg); break; }
				case 'X': EraseCells(CY, CX, CX + P1(p, 0)); break;
				case 'S': ScrollUp(P1(p, 0)); break;
				case 'T': ScrollDown(P1(p, 0)); break;
				case 'b': { int n = P1(p, 0); for (int i = 0; i < n && lastPrinted != 0; i++) Put(lastPrinted); break; }
				case 'g': if (P(p, 0, 0) == 3) tabs = new bool[Cols]; else if (CX < Cols) tabs[CX] = false; break;
				case 'm': Sgr(p); break;
				case 'r':
					{
						int t = P1(p, 0) - 1, b = P(p, 1, Rows) - 1;
						if (t < 0) t = 0; if (b >= Rows) b = Rows - 1;
						if (t < b) { top = t; bottom = b; } else { top = 0; bottom = Rows - 1; }
						MoveTo(0, 0);
						break;
					}
				case 's': if (marker == '\0') SaveCursor(); break;
				case 'u': if (marker == '\0') RestoreCursor(); break;
				case 'h': SetMode(p, marker, true); break;
				case 'l': SetMode(p, marker, false); break;
				case 'n':
					if (marker == '\0')
					{
						if (P(p, 0, 0) == 5) Send("\x1b[0n");
						else if (P(p, 0, 0) == 6) Send("\x1b[" + ((originMode ? CY - top : CY) + 1) + ";" + (CX + 1) + "R");
					}
					break;
				case 'c':
					if (marker == '\0') Send("\x1b[?1;2c"); else if (marker == '>') Send("\x1b[>0;276;0c");
					break;
				default: break; // window ops (t), mouse, cursor style ...
			}
		}

		void ScrollUpNoHistory(int n)
		{
			n = Math.Min(n, bottom - top + 1);
			for (int k = 0; k < n; k++)
			{
				for (int i = top; i < bottom; i++) grid[i] = grid[i + 1];
				grid[bottom] = new TermRow(Cols, pBg);
			}
		}

		void SetMode(int[] p, char marker, bool on)
		{
			foreach (int m in p)
			{
				if (marker == '?')
				{
					switch (m)
					{
						case 1: AppCursorKeys = on; break;
						case 6: originMode = on; MoveTo(0, 0); break;
						case 7: AutoWrap = on; break;
						case 25: CursorVisible = on; break;
						case 47: case 1047: SwitchAlt(on, false); break;
						case 1049: SwitchAlt(on, true); break;
						case 2004: BracketedPaste = on; break;
					}
				}
				else if (marker == '\0' && m == 4) insertMode = on;
			}
		}

		void Sgr(int[] p)
		{
			if (p.Length == 0) { pFg = -1; pBg = -1; pAttr = 0; return; }
			for (int i = 0; i < p.Length; i++)
			{
				int v = p[i] < 0 ? 0 : p[i];
				if (v == 0) { pFg = -1; pBg = -1; pAttr = 0; }
				else if (v == 1) pAttr |= Bold; else if (v == 2) pAttr |= Dim; else if (v == 3) pAttr |= Italic; else if (v == 4) pAttr |= Underline;
				else if (v == 7) pAttr |= Reverse; else if (v == 9) pAttr |= Strike;
				else if (v == 22) pAttr = (byte)(pAttr & ~(Bold | Dim)); else if (v == 23) pAttr = (byte)(pAttr & ~Italic); else if (v == 24) pAttr = (byte)(pAttr & ~Underline);
				else if (v == 27) pAttr = (byte)(pAttr & ~Reverse); else if (v == 29) pAttr = (byte)(pAttr & ~Strike);
				else if (v >= 30 && v <= 37) pFg = v - 30; else if (v == 39) pFg = -1;
				else if (v >= 40 && v <= 47) pBg = v - 40; else if (v == 49) pBg = -1;
				else if (v >= 90 && v <= 97) pFg = v - 90 + 8; else if (v >= 100 && v <= 107) pBg = v - 100 + 8;
				else if (v == 38 || v == 48)
				{
					int color = -1;
					if (i + 1 < p.Length && p[i + 1] == 5 && i + 2 < p.Length) { color = Math.Max(0, Math.Min(255, p[i + 2])); i += 2; }
					else if (i + 1 < p.Length && p[i + 1] == 2 && i + 4 < p.Length) { color = 0x1000000 | ((p[i + 2] & 255) << 16) | ((p[i + 3] & 255) << 8) | (p[i + 4] & 255); i += 4; }
					if (color >= 0) { if (v == 38) pFg = color; else pBg = color; }
				}
			}
		}
	}
}
