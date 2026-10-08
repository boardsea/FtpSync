using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FtpSync
{
	public enum LogLevel { Info, Ok, Upload, Warn, Error }

	public class LogEntry
	{
		public DateTime Time;
		public LogLevel Level;
		public string Source, Message, Details;
	}

	/// <summary>Thread-safe event journal: in-memory ring for the UI plus a full text file.</summary>
	public static class EventLog
	{
		public static string FilePath;
		const int MaxEntries = 5000;
		static readonly List<LogEntry> entries = new List<LogEntry>();
		static readonly object gate = new object();

		/// <summary>Raised on the calling thread; subscribers must marshal to the UI thread themselves.</summary>
		public static event Action<LogEntry> Added;

		public static void Info(string source, string message) { Add(LogLevel.Info, source, message, null); }
		public static void Ok(string source, string message) { Add(LogLevel.Ok, source, message, null); }
		public static void Upload(string source, string message) { Add(LogLevel.Upload, source, message, null); }
		public static void Warn(string source, string message, string details = null) { Add(LogLevel.Warn, source, message, details); }
		public static void Error(string source, string message, string details = null) { Add(LogLevel.Error, source, message, details); }
		public static void Error(string source, string message, Exception ex) { Add(LogLevel.Error, source, message, ex == null ? null : ex.ToString()); }

		public static List<LogEntry> Snapshot() { lock (gate) return new List<LogEntry>(entries); }

		public static void Clear() { lock (gate) entries.Clear(); }

		public static LogEntry Last { get { lock (gate) return entries.Count == 0 ? null : entries[entries.Count - 1]; } }

		public static string Format(LogEntry e)
		{
			StringBuilder sb = new StringBuilder();
			sb.Append(e.Time.ToString("yyyy-MM-dd HH:mm:ss")).Append(" [").Append(e.Level == LogLevel.Error ? "ERR" : e.Level == LogLevel.Warn ? "WRN" : e.Level == LogLevel.Ok ? "OK " : e.Level == LogLevel.Upload ? "UPL" : "INF").Append("] ");
			if (!string.IsNullOrEmpty(e.Source)) sb.Append(e.Source).Append(": ");
			sb.Append(e.Message);
			if (!string.IsNullOrEmpty(e.Details))
				foreach (string line in e.Details.Replace("\r", "").Split('\n')) sb.Append("\r\n      ").Append(line);
			return sb.ToString();
		}

		static void Add(LogLevel level, string source, string message, string details)
		{
			LogEntry e = new LogEntry { Time = DateTime.Now, Level = level, Source = source, Message = message ?? "", Details = details };
			lock (gate)
			{
				entries.Add(e);
				if (entries.Count > MaxEntries) entries.RemoveRange(0, entries.Count - MaxEntries);
				WriteFile(e);
			}
			Action<LogEntry> h = Added;
			if (h != null) { try { h(e); } catch (Exception) { } }
		}

		static void WriteFile(LogEntry e)
		{
			try
			{
				string path = FilePath ?? Path.Combine(Path.GetTempPath(), "FtpSync.log");
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				FileInfo fi = new FileInfo(path);
				if (fi.Exists && fi.Length > 2 * 1024 * 1024)
				{
					string old = path + ".1";
					if (File.Exists(old)) File.Delete(old);
					File.Move(path, old);
				}
				File.AppendAllText(path, Format(e) + "\r\n", Encoding.UTF8);
			}
			catch (Exception) { }
		}
	}
}
