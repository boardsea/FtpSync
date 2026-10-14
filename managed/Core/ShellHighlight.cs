using System;
using System.Text.RegularExpressions;

namespace FtpSync
{
	/// <summary>Client-side colouring of terminal text (only for characters the server left uncoloured): shell syntax on command lines, errors, warnings, URLs, IPs.</summary>
	public static class ShellHighlight
	{
		public const int Flag = 0x1000000;
		const int Cmd = Flag | 0x6BD66B, Opt = Flag | 0xE5C07B, Str = Flag | 0xCE9178, Var = Flag | 0x4EC9B0, Op = Flag | 0xC678DD, Cmt = Flag | 0x7F848E,
			Num = Flag | 0xB5CEA8, Path = Flag | 0x9CDCFE, Prompt = Flag | 0x56B6C2, Err = Flag | 0xF14C4C, Warn = Flag | 0xE5E510, Ok = Flag | 0x6BD66B, Url = Flag | 0x569CD6, Ip = Flag | 0x4EC9B0, Time = Flag | 0x7F848E;

		static readonly Regex PromptRx = new Regex(@"^(?<p>(?:\([^)\r\n]*\)\s*)?(?:\[[^\]\r\n]+\]|[\w.\-]+@[\w.\-]+(?::[^\s$#%>]*)?(?:\s[^\s$#%>]*)?)\s?[\$#%>])\s", RegexOptions.Compiled);
		static readonly Regex Token = new Regex(@"(?<cmt>(?<=^|\s)#.*$)|(?<str>""(?:[^""\\]|\\.)*""?|'[^']*'?)|(?<var>\$\{[^}]*\}?|\$[A-Za-z_][A-Za-z0-9_]*|\$[0-9?!#@*$])|(?<op>\|\||&&|2>&1|>>|[|;&<>])|(?<opt>(?<=^|\s)--?[A-Za-z0-9][\w\-=.,:/]*)|(?<word>[^\s|;&<>""'$]+)", RegexOptions.Compiled);
		static readonly Regex Words = new Regex(@"(?<err>\b(?:error|errors|failed|failure|fatal|denied|cannot|can't|refused|critical|panic|segmentation fault|not found|no such file or directory|timed out|timeout|unreachable)\b)|(?<warn>\b(?:warning|warn|deprecated|notice)\b)|(?<ok>\b(?:success|successful|successfully|done|passed|active \(running\)|enabled|started|ok)\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		static readonly Regex Others = new Regex(@"(?<url>https?://[^\s'""<>]+)|(?<time>\b\d{4}-\d\d-\d\d[ T]\d\d:\d\d(?::\d\d)?\b)|(?<ip>\b\d{1,3}(?:\.\d{1,3}){3}(?::\d+)?\b)", RegexOptions.Compiled);
		static readonly Regex NumRx = new Regex(@"^\d+(?:\.\d+)?$", RegexOptions.Compiled);

		/// <summary>Length of the prompt in the line (0 if the line does not look like a command line).</summary>
		public static int PromptLength(string line)
		{
			Match m = PromptRx.Match(line ?? "");
			return m.Success ? m.Groups["p"].Length + 1 : 0;
		}

		/// <summary>The command typed after the prompt, or null.</summary>
		public static string CommandAfterPrompt(string line)
		{
			int n = PromptLength(line);
			if (n == 0 || n > line.Length) return null;
			string c = line.Substring(n).Trim();
			return c.Length == 0 ? null : c;
		}

		static void Paint(int[] r, int start, int len, int color) { for (int i = start; i < start + len && i < r.Length; i++) if (r[i] == 0) r[i] = color; }

		public static int[] Spans(string text)
		{
			int[] r = new int[text.Length];
			if (text.Length == 0) return r;
			int pl = PromptLength(text);
			if (pl > 0)
			{
				Paint(r, 0, pl - 1, Prompt);
				string rest = text.Substring(pl);
				bool first = true;
				foreach (Match m in Token.Matches(rest))
				{
					int at = pl + m.Index, len = m.Length;
					if (m.Groups["cmt"].Success) Paint(r, at, len, Cmt);
					else if (m.Groups["str"].Success) Paint(r, at, len, Str);
					else if (m.Groups["var"].Success) Paint(r, at, len, Var);
					else if (m.Groups["op"].Success) { Paint(r, at, len, Op); if (m.Value == "|" || m.Value == "||" || m.Value == "&&" || m.Value == ";") first = true; }
					else if (m.Groups["opt"].Success) Paint(r, at, len, Opt);
					else
					{
						string w = m.Value;
						if (first) { Paint(r, at, len, w == "sudo" ? Op : Cmd); if (w != "sudo" && w != "time" && w != "nohup" && w != "env") first = false; }
						else if (NumRx.IsMatch(w)) Paint(r, at, len, Num);
						else if (w.IndexOf('/') >= 0 || w.StartsWith("~") || w.StartsWith(".")) Paint(r, at, len, Path);
					}
				}
				return r;
			}
			foreach (Match m in Others.Matches(text))
				Paint(r, m.Index, m.Length, m.Groups["url"].Success ? Url : m.Groups["time"].Success ? Time : Ip);
			foreach (Match m in Words.Matches(text))
				Paint(r, m.Index, m.Length, m.Groups["err"].Success ? Err : m.Groups["warn"].Success ? Warn : Ok);
			return r;
		}
	}
}
