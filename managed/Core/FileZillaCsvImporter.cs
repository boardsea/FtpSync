using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FtpSync
{
	/// <summary>
	/// Imports a FileZilla site list exported as CSV
	/// (columns Host, Port, Protocol, Type, User, Pass, Logontype, ..., Name). Passwords are stored through DPAPI.
	/// FileZilla protocol codes: 0 FTP, 1 SFTP, 3 FTPS (implicit), 4 FTPES (explicit), 5 HTTPS (skipped), 6 plain FTP.
	/// </summary>
	public static class FileZillaCsvImporter
	{
		public static NppFtpImporter.Result Import(string csvFile)
		{
			NppFtpImporter.Result r = new NppFtpImporter.Result();
			string text = File.ReadAllText(csvFile, Encoding.UTF8);
			List<List<string>> rows = ParseCsv(text);
			if (rows.Count < 2) { r.Warnings.Add(L.T("CSV пустой.")); return r; }

			Dictionary<string, int> col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < rows[0].Count; i++) col[rows[0][i].Trim()] = i;
			Func<List<string>, string, string> get = delegate (List<string> row, string name)
			{
				int idx;
				return col.TryGetValue(name, out idx) && idx < row.Count ? row[idx] : "";
			};

			for (int i = 1; i < rows.Count; i++)
			{
				List<string> row = rows[i];
				string host = get(row, "Host").Trim();
				if (host.Length == 0) continue;
				int proto; int.TryParse(get(row, "Protocol"), out proto);
				string name = get(row, "Name").Trim();
				if (name.Length == 0) name = host;
				if (proto == 2 || proto == 5) { r.Warnings.Add(L.F("{0}: сайт HTTP/HTTPS пропущен.", name)); continue; }

				Profile p = new Profile { Name = name, Host = host, User = get(row, "User") };
				int port; p.Port = int.TryParse(get(row, "Port"), out port) && port > 0 ? port : 21;
				switch (proto)
				{
					case 1: p.Protocol = Protocol.Sftp; if (p.Port == 21) p.Port = 22; break;
					case 3: p.Protocol = Protocol.Ftps; break;
					case 4: p.Protocol = Protocol.Ftpes; break;
					default: p.Protocol = Protocol.Ftp; break;
				}
				string pass = get(row, "Pass");
				int logon; int.TryParse(get(row, "Logontype"), out logon);
				if (logon == 0 && p.User.Length == 0) p.User = "anonymous";
				if (pass.Length > 0 && logon != 2 && logon != 3) p.Password = pass; // 2/3: FileZilla asks every time
				r.Profiles.Add(p);
			}
			if (r.Profiles.Count == 0) r.Warnings.Add(L.T("В CSV не найдено ни одного сайта."));
			return r;
		}

		/// <summary>RFC 4180 style parser: quoted fields, doubled quotes, line breaks inside quotes.</summary>
		public static List<List<string>> ParseCsv(string text)
		{
			List<List<string>> rows = new List<List<string>>();
			List<string> row = new List<string>();
			StringBuilder f = new StringBuilder();
			bool q = false, any = false;
			if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
			for (int i = 0; i < text.Length; i++)
			{
				char c = text[i];
				if (q)
				{
					if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { f.Append('"'); i++; } else q = false; }
					else f.Append(c);
					continue;
				}
				if (c == '"') { q = true; any = true; }
				else if (c == ',') { row.Add(f.ToString()); f.Length = 0; any = true; }
				else if (c == '\r') { }
				else if (c == '\n')
				{
					if (any || f.Length > 0) { row.Add(f.ToString()); rows.Add(row); }
					row = new List<string>(); f.Length = 0; any = false;
				}
				else { f.Append(c); any = true; }
			}
			if (any || f.Length > 0) { row.Add(f.ToString()); rows.Add(row); }
			return rows;
		}
	}
}
