using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace FtpSync
{
	/// <summary>Small text-free icons: a white page with a coloured badge whose colour and shape tell the file type.</summary>
	public static class FileIcons
	{
		public const string Server = "server", ServerOn = "server-on", Folder = "folder", File = "file", Link = "link", Version = "version";

		static readonly Dictionary<string, string> ext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		static FileIcons()
		{
			Map("php", ".php", ".phtml", ".php3", ".php4", ".php5", ".php7", ".inc");
			Map("js", ".js", ".mjs", ".cjs", ".jsx");
			Map("ts", ".ts", ".tsx");
			Map("css", ".css", ".scss", ".sass", ".less", ".map");
			Map("html", ".html", ".htm", ".xhtml", ".tpl", ".twig", ".vue");
			Map("json", ".json", ".jsonc", ".webmanifest");
			Map("xml", ".xml", ".rss", ".atom", ".xsl", ".xslt", ".sitemap");
			Map("md", ".md", ".markdown", ".rst");
			Map("txt", ".txt", ".csv", ".tsv");
			Map("log", ".log");
			Map("image", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".ico", ".bmp", ".avif", ".tif", ".tiff");
			Map("zip", ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz");
			Map("sql", ".sql", ".sqlite", ".db", ".mysql");
			Map("conf", ".ini", ".conf", ".cfg", ".htaccess", ".htpasswd", ".env", ".yml", ".yaml", ".toml", ".user", ".config", ".properties");
			Map("sh", ".sh", ".bash", ".zsh", ".bat", ".cmd", ".ps1");
			Map("pdf", ".pdf");
			Map("media", ".mp3", ".wav", ".ogg", ".mp4", ".webm", ".mov", ".avi", ".mkv", ".flac", ".m4a");
		}

		static void Map(string key, params string[] exts) { foreach (string e in exts) ext[e] = key; }

		public static string KeyFor(string name, bool isDir, bool isLink)
		{
			if (isDir) return isLink ? Link : Folder;
			if (isLink) return Link;
			string n = (name ?? "").ToLowerInvariant();
			if (n == ".htaccess" || n == ".htpasswd" || n == ".env" || n == "php.ini" || n == ".user.ini") return "conf";
			string e = Path.GetExtension(n), k;
			if (n.EndsWith(".min.js") || n.EndsWith(".min.css")) e = Path.GetExtension(n.Substring(0, n.Length - e.Length)) == ".min" ? e : e;
			return ext.TryGetValue(e, out k) ? k : File;
		}

		static Color C(string hex) { return ColorTranslator.FromHtml(hex); }

		public static ImageList Build()
		{
			float scale = 1f;
			try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = Math.Max(1f, g.DpiX / 96f); } catch (Exception) { }
			int size = (int)Math.Round(16 * scale);
			ImageList il = new ImageList { ImageSize = new Size(size, size), ColorDepth = ColorDepth.Depth32Bit };
			Action<string, Action<Graphics>> add = delegate (string key, Action<Graphics> draw)
			{
				Bitmap b = new Bitmap(size, size);
				using (Graphics g = Graphics.FromImage(b))
				{
					g.SmoothingMode = SmoothingMode.AntiAlias;
					g.ScaleTransform(scale, scale);
					draw(g);
				}
				il.Images.Add(key, b);
			};

			// the first five keep their positions (0..4): server, server-on, folder, file, link
			add(Server, g => { Fill(g, "#4682B4", 2, 2, 12, 5); Fill(g, "#B0C4DE", 2, 8, 12, 5); g.FillEllipse(Brushes.Gray, 10, 4, 2, 2); });
			add(ServerOn, g => { Fill(g, "#4682B4", 2, 2, 12, 5); Fill(g, "#B0C4DE", 2, 8, 12, 5); g.FillEllipse(Brushes.LimeGreen, 10, 3.5f, 3, 3); });
			add(Folder, g => { Fill(g, "#DAA520", 1, 3, 6, 3); Fill(g, "#FFD700", 1, 5, 14, 9); });
			add(File, g => Page(g, null));
			add(Link, g => { Page(g, null); using (Pen p = new Pen(C("#4682B4"), 1.8f)) { g.DrawLine(p, 5, 12, 11, 6); g.DrawLine(p, 7.5f, 6, 11, 6); g.DrawLine(p, 11, 6, 11, 9.5f); } });
			add(Version, g => { Page(g, null); g.FillEllipse(new SolidBrush(C("#1F6FE0")), 8.5f, 9, 5, 5); });

			add("php", g => Page(g, () => { g.FillEllipse(new SolidBrush(C("#777BB3")), 1.5f, 7.5f, 13, 7.5f); using (Pen p = new Pen(Color.White, 1.2f)) { g.DrawLine(p, 5, 9.5f, 5, 13); g.DrawLine(p, 8, 9.5f, 8, 13); g.DrawLine(p, 11, 9.5f, 11, 13); } }));
			add("js", g => Page(g, () => { Fill(g, "#F7DF1E", 2, 7, 12, 8); using (Pen p = new Pen(C("#323330"), 1.7f)) { g.DrawLines(p, new[] { new PointF(9.5f, 8.8f), new PointF(9.5f, 12f), new PointF(8.5f, 13.4f), new PointF(6.8f, 13.4f) }); } }));
			add("ts", g => Page(g, () => { Fill(g, "#3178C6", 2, 7, 12, 8); using (Pen p = new Pen(Color.White, 1.6f)) { g.DrawLine(p, 5, 9.5f, 11, 9.5f); g.DrawLine(p, 8, 9.5f, 8, 13.5f); } }));
			add("css", g => Page(g, () => { Shield(g, "#2965F1"); using (Pen p = new Pen(Color.White, 1.3f)) { g.DrawLine(p, 5, 9.5f, 11, 9.5f); g.DrawLine(p, 5.5f, 11.5f, 10.5f, 11.5f); } }));
			add("html", g => Page(g, () => { Shield(g, "#E34F26"); using (Pen p = new Pen(Color.White, 1.3f)) { g.DrawLine(p, 5, 9.5f, 11, 9.5f); g.DrawLine(p, 5.5f, 11.5f, 10.5f, 11.5f); g.DrawLine(p, 8, 9.5f, 8, 13); } }));
			add("json", g => Page(g, () => { g.FillEllipse(new SolidBrush(C("#2E7D32")), 2.5f, 7.5f, 11, 7.5f); g.FillEllipse(Brushes.White, 5, 10, 2, 2); g.FillEllipse(Brushes.White, 9, 10, 2, 2); }));
			add("xml", g => Page(g, () => { using (Pen p = new Pen(C("#E8710A"), 1.8f)) { g.DrawLines(p, new[] { new PointF(6, 8.2f), new PointF(2.8f, 11.3f), new PointF(6, 14.4f) }); g.DrawLines(p, new[] { new PointF(10, 8.2f), new PointF(13.2f, 11.3f), new PointF(10, 14.4f) }); } }));
			add("md", g => Page(g, () => { Lines(g, "#1F6FE0"); Fill(g, "#1F6FE0", 3, 13, 10, 2); }));
			add("txt", g => Page(g, () => Lines(g, "#9AA0A6")));
			add("log", g => Page(g, () => { Lines(g, "#9AA0A6"); g.FillEllipse(new SolidBrush(C("#E8710A")), 9.5f, 9.5f, 4, 4); }));
			add("image", g => Page(g, () => { Fill(g, "#87CEEB", 2, 7, 12, 8); g.FillPolygon(new SolidBrush(C("#3CB043")), new[] { new PointF(2, 15), new PointF(7, 10), new PointF(11, 15) }); g.FillEllipse(new SolidBrush(C("#FFD54F")), 9.5f, 8, 3.5f, 3.5f); }));
			add("zip", g => Page(g, () => { Fill(g, "#B08D57", 2, 7, 12, 8); for (int y = 7; y < 15; y += 2) { Fill(g, "#5D4630", 7, y, 1.2f, 1f); Fill(g, "#F5E6C8", 8.2f, y + 1, 1.2f, 1f); } }));
			add("sql", g => Page(g, () => { Brush body = new SolidBrush(C("#5C7C99")); g.FillRectangle(body, 3, 9, 10, 4.5f); g.FillEllipse(body, 3, 11.5f, 10, 3.5f); g.FillEllipse(new SolidBrush(C("#8FB0CC")), 3, 7.3f, 10, 3.5f); }));
			add("conf", g => Page(g, () =>
			{
				Brush gear = new SolidBrush(C("#6B7280"));
				for (int i = 0; i < 4; i++) { g.TranslateTransform(8, 11); g.RotateTransform(i * 45); g.FillRectangle(gear, -1, -4.2f, 2, 8.4f); g.ResetTransform(); g.ScaleTransform(scale, scale); }
				g.FillEllipse(gear, 4.5f, 7.5f, 7, 7); g.FillEllipse(Brushes.White, 6.7f, 9.7f, 2.6f, 2.6f);
			}));
			add("sh", g => Page(g, () => { Fill(g, "#263238", 2, 7, 12, 8); using (Pen p = new Pen(C("#69F0AE"), 1.4f)) { g.DrawLines(p, new[] { new PointF(4.5f, 9), new PointF(7.5f, 11), new PointF(4.5f, 13) }); g.DrawLine(p, 9, 13, 12, 13); } }));
			add("pdf", g => Page(g, () => { Fill(g, "#D32F2F", 2, 7, 12, 8); using (Pen p = new Pen(Color.White, 1.2f)) { g.DrawLine(p, 4.5f, 10, 11.5f, 10); g.DrawLine(p, 4.5f, 12.5f, 9, 12.5f); } }));
			add("media", g => Page(g, () => { Fill(g, "#7E57C2", 2, 7, 12, 8); g.FillPolygon(Brushes.White, new[] { new PointF(6.5f, 9), new PointF(11, 11), new PointF(6.5f, 13) }); }));
			return il;
		}

		static void Fill(Graphics g, string hex, float x, float y, float w, float h) { using (Brush b = new SolidBrush(C(hex))) g.FillRectangle(b, x, y, w, h); }

		static void Lines(Graphics g, string hex)
		{
			using (Pen p = new Pen(C(hex), 1.2f)) { g.DrawLine(p, 5.5f, 6, 10, 6); g.DrawLine(p, 5.5f, 8.5f, 10.5f, 8.5f); g.DrawLine(p, 5.5f, 11, 10.5f, 11); }
		}

		static void Shield(Graphics g, string hex)
		{
			using (Brush b = new SolidBrush(C(hex))) g.FillPolygon(b, new[] { new PointF(2, 7), new PointF(14, 7), new PointF(13, 12.2f), new PointF(8, 15), new PointF(3, 12.2f) });
		}

		/// <summary>White page with a folded corner; the badge (drawn by the callback) covers the lower part.</summary>
		static void Page(Graphics g, Action badge)
		{
			PointF[] pg = { new PointF(3, 1), new PointF(10, 1), new PointF(13, 4), new PointF(13, 15), new PointF(3, 15) };
			g.FillPolygon(Brushes.White, pg);
			using (Pen p = new Pen(C("#9AA0A6"), 1f)) g.DrawPolygon(p, pg);
			g.FillPolygon(new SolidBrush(C("#DADCE0")), new[] { new PointF(10, 1), new PointF(10, 4), new PointF(13, 4) });
			if (badge != null) badge();
		}
	}
}
