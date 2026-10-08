using System;
using System.Collections.Generic;
using System.Text;

namespace FtpSync
{
	public enum EditKind { Equal, Delete, Insert }

	public struct Edit
	{
		public EditKind Kind; public string Line;
		public Edit(EditKind k, string l) { Kind = k; Line = l; }
	}

	/// <summary>Line diff (LCS) and three-way merge (diff3).</summary>
	public static class Diff
	{
		const int MaxCells = 20000000;

		/// <summary>Edit script turning a into b.</summary>
		public static List<Edit> Compute(string[] a, string[] b)
		{
			List<Edit> res = new List<Edit>();
			int start = 0;
			while (start < a.Length && start < b.Length && a[start] == b[start]) start++;
			int ea = a.Length, eb = b.Length;
			while (ea > start && eb > start && a[ea - 1] == b[eb - 1]) { ea--; eb--; }

			for (int i = 0; i < start; i++) res.Add(new Edit(EditKind.Equal, a[i]));

			int n = ea - start, m = eb - start;
			if (n == 0) { for (int j = 0; j < m; j++) res.Add(new Edit(EditKind.Insert, b[start + j])); }
			else if (m == 0) { for (int i = 0; i < n; i++) res.Add(new Edit(EditKind.Delete, a[start + i])); }
			else if ((long)n * m > MaxCells)
			{
				for (int i = 0; i < n; i++) res.Add(new Edit(EditKind.Delete, a[start + i]));
				for (int j = 0; j < m; j++) res.Add(new Edit(EditKind.Insert, b[start + j]));
			}
			else
			{
				int[,] t = new int[n + 1, m + 1];
				for (int i = n - 1; i >= 0; i--)
					for (int j = m - 1; j >= 0; j--)
						t[i, j] = a[start + i] == b[start + j] ? t[i + 1, j + 1] + 1 : Math.Max(t[i + 1, j], t[i, j + 1]);
				int x = 0, y = 0;
				while (x < n && y < m)
				{
					if (a[start + x] == b[start + y]) { res.Add(new Edit(EditKind.Equal, a[start + x])); x++; y++; }
					else if (t[x + 1, y] >= t[x, y + 1]) { res.Add(new Edit(EditKind.Delete, a[start + x])); x++; }
					else { res.Add(new Edit(EditKind.Insert, b[start + y])); y++; }
				}
				while (x < n) { res.Add(new Edit(EditKind.Delete, a[start + x])); x++; }
				while (y < m) { res.Add(new Edit(EditKind.Insert, b[start + y])); y++; }
			}

			for (int i = ea; i < a.Length; i++) res.Add(new Edit(EditKind.Equal, a[i]));
			return res;
		}

		public static bool HasChanges(List<Edit> edits)
		{
			foreach (Edit e in edits) if (e.Kind != EditKind.Equal) return true;
			return false;
		}

		/// <summary>Unified-style text: "-" only in a, "+" only in b, with context lines.</summary>
		public static string Unified(string a, string b, int context)
		{
			List<Edit> ed = Compute(TextUtil.Lines(TextUtil.Normalize(a)), TextUtil.Lines(TextUtil.Normalize(b)));
			int n = ed.Count;
			bool[] show = new bool[n];
			for (int i = 0; i < n; i++)
				if (ed[i].Kind != EditKind.Equal)
					for (int k = Math.Max(0, i - context); k <= Math.Min(n - 1, i + context); k++) show[k] = true;
			StringBuilder sb = new StringBuilder();
			int la = 0, lb = 0;
			bool gap = false;
			for (int i = 0; i < n; i++)
			{
				Edit e = ed[i];
				if (show[i])
				{
					if (gap) { sb.Append("@@ ... @@\n"); gap = false; }
					string tag = e.Kind == EditKind.Equal ? "  " : e.Kind == EditKind.Delete ? "- " : "+ ";
					int num = e.Kind == EditKind.Insert ? lb + 1 : la + 1;
					sb.Append(tag).Append(num.ToString().PadLeft(5)).Append(" | ").Append(e.Line).Append('\n');
				}
				else gap = true;
				if (e.Kind != EditKind.Insert) la++;
				if (e.Kind != EditKind.Delete) lb++;
			}
			return sb.ToString();
		}

		public class MergeResult
		{
			public string Text;
			public int Conflicts;
			public bool Clean { get { return Conflicts == 0; } }
		}

		struct Hunk { public int BaseStart, BaseEnd; public List<string> Lines; }

		static List<Hunk> Hunks(string[] baseL, string[] other)
		{
			List<Edit> ed = Compute(baseL, other);
			List<Hunk> res = new List<Hunk>();
			int bi = 0;
			int i = 0;
			while (i < ed.Count)
			{
				if (ed[i].Kind == EditKind.Equal) { bi++; i++; continue; }
				Hunk h = new Hunk { BaseStart = bi, Lines = new List<string>() };
				while (i < ed.Count && ed[i].Kind != EditKind.Equal)
				{
					if (ed[i].Kind == EditKind.Delete) bi++; else h.Lines.Add(ed[i].Line);
					i++;
				}
				h.BaseEnd = bi;
				res.Add(h);
			}
			return res;
		}

		static bool SameLines(List<string> a, List<string> b)
		{
			if (a.Count != b.Count) return false;
			for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
			return true;
		}

		/// <summary>Three-way merge of mine and theirs against their common base.</summary>
		public static MergeResult Merge3(string baseText, string mine, string theirs, string mineLabel, string theirsLabel)
		{
			string[] bl = TextUtil.Lines(TextUtil.Normalize(baseText));
			string[] ml = TextUtil.Lines(TextUtil.Normalize(mine));
			string[] tl = TextUtil.Lines(TextUtil.Normalize(theirs));
			List<Hunk> hm = Hunks(bl, ml), ht = Hunks(bl, tl);

			List<string> outp = new List<string>();
			int conflicts = 0;
			int pos = 0, im = 0, it = 0;
			while (im < hm.Count || it < ht.Count)
			{
				bool hasM = im < hm.Count, hasT = it < ht.Count;
				Hunk? m = hasM ? (Hunk?)hm[im] : null;
				Hunk? t = hasT ? (Hunk?)ht[it] : null;
				int nextStart = int.MaxValue;
				if (hasM) nextStart = Math.Min(nextStart, m.Value.BaseStart);
				if (hasT) nextStart = Math.Min(nextStart, t.Value.BaseStart);
				for (; pos < nextStart; pos++) outp.Add(bl[pos]);

				// collect overlapping group
				int gStart = nextStart, gEnd = nextStart;
				List<Hunk> gm = new List<Hunk>(), gt = new List<Hunk>();
				bool grew = true;
				while (grew)
				{
					grew = false;
					while (im < hm.Count && hm[im].BaseStart <= gEnd) { gm.Add(hm[im]); gEnd = Math.Max(gEnd, hm[im].BaseEnd); im++; grew = true; }
					while (it < ht.Count && ht[it].BaseStart <= gEnd) { gt.Add(ht[it]); gEnd = Math.Max(gEnd, ht[it].BaseEnd); it++; grew = true; }
				}

				List<string> mSide = Apply(bl, gStart, gEnd, gm), tSide = Apply(bl, gStart, gEnd, gt);
				List<string> baseSide = new List<string>();
				for (int k = gStart; k < gEnd; k++) baseSide.Add(bl[k]);

				if (gt.Count == 0) outp.AddRange(mSide);
				else if (gm.Count == 0) outp.AddRange(tSide);
				else if (SameLines(mSide, tSide)) outp.AddRange(mSide);
				else
				{
					conflicts++;
					outp.Add("<<<<<<< " + mineLabel);
					outp.AddRange(mSide);
					outp.Add("=======");
					outp.AddRange(tSide);
					outp.Add(">>>>>>> " + theirsLabel);
				}
				pos = gEnd;
			}
			for (; pos < bl.Length; pos++) outp.Add(bl[pos]);
			return new MergeResult { Text = string.Join("\n", outp.ToArray()), Conflicts = conflicts };
		}

		static List<string> Apply(string[] bl, int start, int end, List<Hunk> hunks)
		{
			List<string> r = new List<string>();
			int p = start;
			foreach (Hunk h in hunks)
			{
				for (; p < h.BaseStart; p++) r.Add(bl[p]);
				r.AddRange(h.Lines);
				p = h.BaseEnd;
			}
			for (; p < end; p++) r.Add(bl[p]);
			return r;
		}
	}
}
