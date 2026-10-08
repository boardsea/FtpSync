using System;
using System.Security.Cryptography;
using System.Text;

namespace FtpSync
{
	/// <summary>Case-insensitive comparison where digit runs compare as numbers ("2." before "10.").</summary>
	public class NaturalComparer : System.Collections.Generic.IComparer<string>
	{
		public static readonly NaturalComparer Instance = new NaturalComparer();

		public int Compare(string a, string b)
		{
			a = a ?? ""; b = b ?? "";
			int i = 0, j = 0;
			while (i < a.Length && j < b.Length)
			{
				if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
				{
					int si = i, sj = j;
					while (i < a.Length && char.IsDigit(a[i])) i++;
					while (j < b.Length && char.IsDigit(b[j])) j++;
					string na = a.Substring(si, i - si).TrimStart('0'), nb = b.Substring(sj, j - sj).TrimStart('0');
					if (na.Length != nb.Length) return na.Length < nb.Length ? -1 : 1;
					int c = string.CompareOrdinal(na, nb);
					if (c != 0) return c;
				}
				else
				{
					int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
					if (c != 0) return c;
					i++; j++;
				}
			}
			return (a.Length - i).CompareTo(b.Length - j);
		}
	}

	/// <summary>Byte/text helpers. Signatures ignore BOM and line-ending differences.</summary>
	public static class TextUtil
	{
		public static bool IsBinary(byte[] data)
		{
			int n = Math.Min(data.Length, 8000);
			for (int i = 0; i < n; i++) if (data[i] == 0) return true;
			return false;
		}

		public static string Decode(byte[] data)
		{
			if (data == null || data.Length == 0) return "";
			int skip = 0;
			if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) skip = 3;
			try
			{
				return new UTF8Encoding(false, true).GetString(data, skip, data.Length - skip);
			}
			catch (ArgumentException)
			{
				return Encoding.Default.GetString(data, skip, data.Length - skip);
			}
		}

		public static string Normalize(string s)
		{
			return s.Replace("\r\n", "\n").Replace('\r', '\n');
		}

		public static string Sig(byte[] data)
		{
			if (data == null) return null;
			if (IsBinary(data)) return "bin:" + Hex(SHA1.Create().ComputeHash(data));
			return Sig(Decode(data));
		}

		public static string Sig(string text)
		{
			byte[] b = new UTF8Encoding(false).GetBytes(Normalize(text));
			return Hex(SHA1.Create().ComputeHash(b));
		}

		public static string Hex(byte[] b)
		{
			StringBuilder sb = new StringBuilder(b.Length * 2);
			foreach (byte x in b) sb.Append(x.ToString("x2"));
			return sb.ToString();
		}

		public static string[] Lines(string normalized)
		{
			if (normalized.Length == 0) return new string[0];
			return normalized.Split('\n');
		}

		public static string FormatSize(long n)
		{
			if (n < 1024) return n + " B";
			if (n < 1024 * 1024) return (n / 1024.0).ToString("0.0") + " KB";
			return (n / 1048576.0).ToString("0.0") + " MB";
		}
	}
}
