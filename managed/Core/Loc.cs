using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FtpSync
{
	/// <summary>
	/// Interface language. The Russian text in the source is the key (L.T("текст")); translations live in plain-text files
	/// lang\xx.txt next to the plugin (one "key TAB translation" per line, \n and \t escaped). Missing key = Russian text.
	/// To add a language, copy lang\_template.txt to lang\xx.txt and translate the right-hand column.
	/// </summary>
	public static class L
	{
		static Dictionary<string, string> map = new Dictionary<string, string>();
		static string langDir = "";
		public static string Code = "ru";

		public static string T(string ru)
		{
			string v;
			return ru != null && map.TryGetValue(ru, out v) && v.Length > 0 ? v : ru;
		}

		public static string F(string ru, params object[] args)
		{
			try { return string.Format(T(ru), args); }
			catch (FormatException) { return string.Format(ru, args); }
		}

		/// <summary>setting: "auto" (follow the system/Notepad++ language) or a code like "en".</summary>
		public static void Init(string pluginDir, string setting)
		{
			langDir = Path.Combine(pluginDir ?? "", "lang");
			string code = string.IsNullOrEmpty(setting) || setting == "auto" ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : setting;
			code = code.ToLowerInvariant();
			Dictionary<string, string> m = new Dictionary<string, string>();
			if (code != "ru")
			{
				string f = Path.Combine(langDir, code + ".txt");
				if (!File.Exists(f) && setting == "auto" && File.Exists(Path.Combine(langDir, "en.txt"))) { code = "en"; f = Path.Combine(langDir, "en.txt"); }
				if (File.Exists(f)) m = Load(f); else code = "ru";
			}
			map = m; Code = code;
		}

		public static Dictionary<string, string> Load(string file)
		{
			Dictionary<string, string> m = new Dictionary<string, string>();
			foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
			{
				if (line.Length == 0 || line[0] == '#') continue;
				int tab = line.IndexOf('\t');
				if (tab <= 0) continue;
				m[Unescape(line.Substring(0, tab))] = Unescape(line.Substring(tab + 1));
			}
			return m;
		}

		public static string Escape(string s) { return s.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n").Replace("\t", "\\t"); }

		public static string Unescape(string s)
		{
			if (s.IndexOf('\\') < 0) return s;
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < s.Length; i++)
			{
				if (s[i] == '\\' && i + 1 < s.Length)
				{
					char n = s[++i];
					sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);
				}
				else sb.Append(s[i]);
			}
			return sb.ToString();
		}

		/// <summary>Available languages: code => native name (first line "#name: Deutsch"), always including ru.</summary>
		public static SortedDictionary<string, string> Available()
		{
			SortedDictionary<string, string> r = new SortedDictionary<string, string>();
			r["ru"] = "Русский";
			if (Directory.Exists(langDir))
				foreach (string f in Directory.GetFiles(langDir, "*.txt"))
				{
					string code = Path.GetFileNameWithoutExtension(f);
					if (code.StartsWith("_")) continue;
					string name = code;
					try
					{
						using (StreamReader sr = new StreamReader(f, Encoding.UTF8))
						{
							string first = sr.ReadLine();
							if (first != null && first.StartsWith("#name:")) name = first.Substring(6).Trim();
						}
					}
					catch (IOException) { }
					r[code] = name;
				}
			return r;
		}
	}
}
