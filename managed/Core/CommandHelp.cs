using System;
using System.Collections.Generic;

namespace FtpSync
{
	public class HelpCommand
	{
		public string Category, Command, Description, English;
		public override string ToString() { return Command; }
	}

	/// <summary>Cheat sheet of popular shell commands: localized descriptions and search.</summary>
	public static partial class CommandHelp
	{
		public static List<HelpCommand> All()
		{
			List<HelpCommand> list = new List<HelpCommand>();
			foreach (string[] d in Data)
				list.Add(new HelpCommand { Category = L.T(Cats[int.Parse(d[0])]), Command = d[1], Description = L.T(d[2]), English = d[3] });
			return list;
		}

		/// <summary>Every word of the query must occur in the command, the localized or the English description, or the category.</summary>
		public static bool Matches(HelpCommand c, string query)
		{
			if (string.IsNullOrEmpty(query)) return true;
			string hay = (c.Command + " " + c.Description + " " + c.English + " " + c.Category).ToLowerInvariant();
			foreach (string w in query.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
				if (hay.IndexOf(w, StringComparison.Ordinal) < 0) return false;
			return true;
		}

		public static HelpCommand Find(List<HelpCommand> all, string command)
		{
			foreach (HelpCommand c in all) if (c.Command == command) return c;
			return null;
		}

		/// <summary>Puts the command first in a most-recent-first list without duplicates.</summary>
		public static void Touch(List<string> list, string command, int max)
		{
			command = (command ?? "").Trim();
			if (command.Length == 0) return;
			list.Remove(command);
			list.Insert(0, command);
			while (list.Count > max) list.RemoveAt(list.Count - 1);
		}
	}
}
