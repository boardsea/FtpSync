using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FtpSync;

class FakeRemote : IRemote
{
	public static Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();
	public byte[] Download(string p) { byte[] b; return Files.TryGetValue(p, out b) ? b : null; }
	public void Upload(string p, byte[] d) { Files[p] = d; }
	public void Dispose() { }
}

class MockFtp
{
	public System.Net.Sockets.TcpListener L = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
	public Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();
	public List<string> Log = new List<string>();
	public int Port { get { return ((System.Net.IPEndPoint)L.LocalEndpoint).Port; } }
	public MockFtp()
	{
		L.Start();
		var t = new System.Threading.Thread(() => { while (true) { System.Net.Sockets.TcpClient c; try { c = L.AcceptTcpClient(); } catch (Exception) { return; } var h = new System.Threading.Thread(() => Handle(c)); h.IsBackground = true; h.Start(); } });
		t.IsBackground = true; t.Start();
	}
	void Handle(System.Net.Sockets.TcpClient c)
	{
		var st = c.GetStream();
		var rd = new StreamReader(st, new UTF8Encoding(false));
		Action<string> w = x => { var b = new UTF8Encoding(false).GetBytes(x + "\r\n"); st.Write(b, 0, b.Length); };
		System.Net.Sockets.TcpListener dl = null; string rnfr = null;
		w("220-Welcome to the mock\r\n220 Ready");
		string line;
		while ((line = rd.ReadLine()) != null)
		{
			lock (Log) Log.Add(line);
			string cmd = line.Split(' ')[0].ToUpper(), arg = line.Length > cmd.Length ? line.Substring(cmd.Length + 1) : "";
			switch (cmd)
			{
				case "USER": w("331 Password required"); break;
				case "PASS": w("230 Logged in"); break;
				case "OPTS": w("200 UTF8 on"); break;
				case "TYPE": w("200 Binary"); break;
				case "PWD": w("257 \"/home/u\" is the current directory"); break;
				case "EPSV": w("502 Command not implemented"); break;
				case "PASV":
					dl = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); dl.Start();
					int dp = ((System.Net.IPEndPoint)dl.LocalEndpoint).Port;
					w("227 Entering Passive Mode (10,0,0,5," + (dp / 256) + "," + (dp % 256) + ")"); break; // private address: the client must use the control address
				case "SIZE": { byte[] f; if (Files.TryGetValue(arg, out f)) w("213 " + f.Length); else w("550 not found"); break; }
				case "LIST":
					{
						if (arg.StartsWith("-a")) { w("550 -a not supported"); if (dl != null) { dl.Stop(); dl = null; } break; }
						w("150 Opening"); var d = dl.AcceptTcpClient(); dl.Stop(); dl = null;
						var lb = new UTF8Encoding(false).GetBytes("drwxr-xr-x 2 u u 4096 Jan  1  2024 docs\r\n-rw-r--r-- 1 u u 5 Jan  1 12:00 a.txt\r\n");
						d.GetStream().Write(lb, 0, lb.Length); d.Close(); w("226 Done"); break;
					}
				case "RETR":
					{
						byte[] f;
						if (!Files.TryGetValue(arg, out f)) { w("550 No such file"); if (dl != null) { dl.Stop(); dl = null; } break; }
						w("150 Sending"); var d = dl.AcceptTcpClient(); dl.Stop(); dl = null;
						d.GetStream().Write(f, 0, f.Length); d.Close(); w("226 Done"); break;
					}
				case "STOR":
					{
						w("150 Receiving"); var d = dl.AcceptTcpClient(); dl.Stop(); dl = null;
						var ms = new MemoryStream(); d.GetStream().CopyTo(ms); d.Close(); Files[arg] = ms.ToArray(); w("226 Done"); break;
					}
				case "MKD": w("257 created"); break;
				case "DELE": Files.Remove(arg); w("250 deleted"); break;
				case "RMD": w("250 removed"); break;
				case "RNFR": rnfr = arg; w("350 ready"); break;
				case "RNTO": { byte[] f; if (rnfr != null && Files.TryGetValue(rnfr, out f)) { Files.Remove(rnfr); Files[arg] = f; } w("250 renamed"); break; }
				case "QUIT": w("221 Bye"); c.Close(); return;
				default: w("500 unknown"); break;
			}
		}
	}
}

static class T
{
	static int fails, total;
	static void Eq(object a, object b, string name)
	{
		total++;
		if (!object.Equals(a, b)) { fails++; Console.WriteLine("FAIL " + name + ": expected [" + b + "] got [" + a + "]"); }
	}
	static byte[] B(string s) { return new UTF8Encoding(false).GetBytes(s); }

	static int Main()
	{
		// --- merge ---
		var m = Diff.Merge3("a\nb\nc\nd", "a\nB\nc\nd", "a\nb\nc\nD", "mine", "theirs");
		Eq(m.Clean, true, "merge clean"); Eq(m.Text, "a\nB\nc\nD", "merge text");
		m = Diff.Merge3("a\nb\nc", "a\nX\nc", "a\nY\nc", "mine", "theirs");
		Eq(m.Conflicts, 1, "merge conflict");
		Eq(m.Text, "a\n<<<<<<< mine\nX\n=======\nY\n>>>>>>> theirs\nc", "conflict text");
		m = Diff.Merge3("a\nb", "a\nb\nc", "z\na\nb", "m", "t");
		Eq(m.Text, "z\na\nb\nc", "insert both ends");
		m = Diff.Merge3("a\nb\nc", "a\nb\nc", "a\nb\nc\nd", "m", "t");
		Eq(m.Text, "a\nb\nc\nd", "theirs only");
		m = Diff.Merge3("a\nb\nc", "a\nc", "a\nc", "m", "t");
		Eq(m.Clean, true, "identical change"); Eq(m.Text, "a\nc", "identical change text");
		m = Diff.Merge3("", "x", "x", "m", "t");
		Eq(m.Text, "x", "empty base");

		// --- diff ---
		string u = Diff.Unified("1\n2\n3", "1\n2x\n3", 1);
		Eq(u.Contains("- ") && u.Contains("+ "), true, "unified");
		Eq(TextUtil.Sig("a\r\nb"), TextUtil.Sig("a\nb"), "sig eol");
		Eq(TextUtil.Sig(B("\u00ef\u00bb\u00bf".Substring(0,0) + "a")), TextUtil.Sig(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a' }), "sig bom");

		// --- crypto ---
		Eq(NppFtpCrypto.TryDecrypt(NppFtpCrypto.EncryptForTest("s3cret!")), "s3cret!", "des roundtrip");

		// --- importer ---
		string tmp = Path.Combine(Path.GetTempPath(), "nppg_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(tmp, "NppFTP"));
		string xml = Path.Combine(tmp, "NppFTP", "NppFTP.xml");
		File.WriteAllText(xml, "<?xml version=\"1.0\"?><NppFTP defaultCache=\"%CONFIGDIR%\\Cache\\%USERNAME%@%HOSTNAME%\"><Profiles>" +
			"<Profile name=\"site\" hostname=\"example.com\" port=\"21\" username=\"bob\" password=\"" + NppFtpCrypto.EncryptForTest("pw1") + "\" securityMode=\"2\" initialDir=\"/public_html\">" +
			"<Cache localpath=\"C:\\work\\site\" externalpath=\"/public_html\"/></Profile>" +
			"<Profile name=\"sf\" hostname=\"h2\" port=\"22\" username=\"u\" password=\"zz\" securityMode=\"3\"/></Profiles></NppFTP>");
		var imp = NppFtpImporter.Import(xml);
		Eq(imp.Profiles.Count, 2, "import count");
		Eq(imp.Profiles[0].Host, "example.com", "host");
		Eq(imp.Profiles[0].Protocol, Protocol.Ftpes, "proto ftpes");
		Eq(imp.Profiles[0].Password, "pw1", "pw");
		Eq(imp.Profiles[0].Maps[0].Remote, "/public_html", "map remote");
		Eq(imp.Profiles[1].Protocol, Protocol.Sftp, "sftp");
		Eq(imp.Warnings.Count, 1, "undecryptable warning");

		// --- guard ---
		var s = new AppSettings { BackupRoot = Path.Combine(tmp, "bk") };
		var prof = new Profile { Name = "site", Host = "h", User = "u" };
		prof.Maps.Add(new PathMap("C:\\cache\\site", "/www"));
		s.Profiles.Add(prof);
		RemoteFactory.Override = p => new FakeRemote();
		var g = new Guard(s, Path.Combine(tmp, "state.xml"));
		var r = g.Resolve("C:\\cache\\site\\wp-content\\a.php");
		Eq(r.Remote, "/www/wp-content/a.php", "resolve");
		Eq(g.Resolve("C:\\other\\a.php") == null, true, "unresolved");

		string path = "C:\\cache\\site\\a.php";
		FakeRemote.Files["/www/a.php"] = B("v1\nline\n");
		var c = g.Check(path, B("v1\nline\n"));
		Eq(c.Status, SyncStatus.InSync, "in sync");
		c = g.Check(path, B("v1\nline\nmine\n"));
		Eq(c.Status, SyncStatus.LocalAhead, "local ahead");
		FakeRemote.Files["/www/a.php"] = B("v1\nline\nserver\n");
		c = g.Check(path, B("v1\nline\n"));
		Eq(c.Status, SyncStatus.RemoteChanged, "remote changed");
		c = g.Check(path, B("v1\nline\nmine\n"));
		Eq(c.Status, SyncStatus.Conflict, "conflict");
		Eq(c.BaseText, "v1\nline\n", "base text");
		g.Ignore(c);
		c = g.Check(path, B("v1\nline\nmine\n"));
		Eq(c.Ignored, true, "ignored");
		FakeRemote.Files["/www/a.php"] = B("v1\nline\nserver2\n");
		c = g.Check(path, B("v1\nline\nmine\n"));
		Eq(c.Ignored, false, "ignored resets on new remote");
		g.AcceptRemote(c.Target, c.Remote);
		c = g.Check(path, B("v1\nline\nserver2\n"));
		Eq(c.Status, SyncStatus.InSync, "after accept");
		FakeRemote.Files.Remove("/www/a.php");
		Eq(g.Check(path, B("x")).Status, SyncStatus.RemoteMissing, "missing");

		// persisted baseline
		var g2 = new Guard(s, Path.Combine(tmp, "state.xml"));
		Eq(g2.PeekState(r == null ? null : g2.Resolve(path)) != null, true, "state persisted");

		// backups tree
		var versions = g.Backups.Versions("site", "/www/a.php");
		Eq(versions.Count >= 3, true, "backups exist: " + versions.Count);
		Eq(File.Exists(versions[0].Path), true, "backup file");
		Eq(versions[0].Path.Contains(Path.Combine("site", "www", "a.php")), true, "mirrored path");

		// upload + pre-upload backup
		FakeRemote.Files["/www/a.php"] = B("old");
		g.UploadFile(r = g.Resolve(path), B("new"));
		Eq(Encoding.UTF8.GetString(FakeRemote.Files["/www/a.php"]), "new", "uploaded");
		Eq(g.Backups.Versions("site", "/www/a.php").Exists(v => v.Reason == "pre-upload"), true, "pre-upload backup");

		Directory.Delete(tmp, true);
		fails += T2.Run();
		Console.WriteLine(fails == 0 ? "ALL OK (" + total + "+)" : fails + " FAILED");
		return fails;
	}
}

static class T2
{
	public static int Run()
	{
		int fails = 0;
		Action<object, object, string> eq = (a, b, n) => { if (!object.Equals(a, b)) { fails++; Console.WriteLine("FAIL " + n + ": expected [" + b + "] got [" + a + "]"); } };

		var l = FtpListParser.Parse(
			"total 8\r\ndrwxr-xr-x   2 user group     4096 Oct  7 12:00 .cache\r\n-rw-r--r--   1 user group      120 Jan  3  2024 my file.php\r\nlrwxrwxrwx   1 user group       11 Oct  7 12:00 www -> public_html\r\ndrwxr-xr-x   2 user group     4096 Oct  7 12:00 .\r\n", "/home/x");
		eq(l.Count, 3, "ls count");
		eq(l[0].IsDir, true, "dir"); eq(l[0].Path, "/home/x/.cache", "path");
		eq(l[1].Name, "my file.php", "space name"); eq(l[1].Size, 120L, "size"); eq(l[1].Modified.Value.Year, 2024, "year");
		eq(l[2].IsLink, true, "link"); eq(l[2].Name, "www", "link name");
		var d = FtpListParser.Parse("10-07-26  12:00PM       <DIR>          wp-content\r\n10-07-26  12:01PM                 1234 index.php\r\n", "/");
		eq(d.Count, 2, "dos count"); eq(d[0].IsDir, true, "dos dir"); eq(d[1].Size, 1234L, "dos size"); eq(d[1].Path, "/index.php", "dos path");
		eq(FtpRemote.ParsePwd("257 \"/home/s/seaboard\" is your current location"), "/home/s/seaboard", "pwd");
		eq(FtpRemote.ParsePwd("257 \"/a\"\"b\" ok"), "/a\"b", "pwd quote");
		eq(RemotePath.Parent("/a/b/c.txt"), "/a/b", "parent"); eq(RemotePath.Parent("/a"), "/", "parent root"); eq(RemotePath.Name("/a/b/"), "b", "name");

		string csv = "\uFEFF\"Host\",\"Port\",\"Protocol\",\"Type\",\"User\",\"Pass\",\"Logontype\",\"Name\"\r\n" +
			"\"h1.example\",\"21\",\"6\",\"1\",\"u1\",\"p,\"\"w\"\"d\",\"1\",\"Мой сайт\"\r\n" +
			"\"h2.example\",\"22\",\"1\",,\"u2\",\"pw2\",\"1\",\"sftp\"\r\n" +
			"\"h3.example\",\"443\",\"5\",,,,\"0\",\"web\"\r\n" +
			"\"h4.example\",\"21\",\"0\",\"1\",\"u4\",\"\",\"2\",\"ask\"\r\n";
		string csvFile = System.IO.Path.GetTempFileName();
		System.IO.File.WriteAllText(csvFile, csv, new System.Text.UTF8Encoding(false));
		var fz = FileZillaCsvImporter.Import(csvFile);
		System.IO.File.Delete(csvFile);
		eq(fz.Profiles.Count, 3, "fz count"); eq(fz.Profiles[0].Name, "Мой сайт", "fz name"); eq(fz.Profiles[0].Password, "p,\"w\"d", "fz quoted password");
		eq(fz.Profiles[1].Protocol, Protocol.Sftp, "fz sftp"); eq(fz.Profiles[1].Port, 22, "fz port"); eq(fz.Profiles[2].Password, "", "fz ask logon");
		eq(fz.Warnings.Count, 1, "fz https skipped");

		// --- FileZilla XML ---
		string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><FileZilla3 version=\"3.60\"><Servers>"
			+ "<Folder expanded=\"1\">Клиенты<Folder>Магазины<Server><Host>shop.example.com</Host><Port>22</Port><Protocol>1</Protocol><Type>0</Type><User>root</User>"
			+ "<Pass encoding=\"base64\">cMOkc3M=</Pass><Logontype>1</Logontype><PasvMode>MODE_ACTIVE</PasvMode><Name>Мой магазин</Name><Comments>тест</Comments>"
			+ "<LocalDir>C:\\sites\\shop</LocalDir><RemoteDir>1 0 4 html 3 www</RemoteDir>"
			+ "<Bookmark><Name>Тема</Name><LocalDir>C:\\sites\\shop\\theme</LocalDir><RemoteDir>1 0 4 html 5 theme</RemoteDir></Bookmark>Мой магазин</Server></Folder>"
			+ "<Server><Host>ftp.example.com</Host><Port>21</Port><Protocol>4</Protocol><User>u</User><Pass encoding=\"crypt\">abc</Pass><Logontype>1</Logontype><Name>Закрытый</Name></Server></Folder>"
			+ "<Server><Host>web.example.com</Host><Protocol>5</Protocol><Name>Веб</Name></Server>"
			+ "<Server><Host>ftp.root.com</Host><Protocol>0</Protocol><Logontype>0</Logontype><Name>Корень</Name></Server></Servers></FileZilla3>";
		string xmlFile = System.IO.Path.GetTempFileName();
		System.IO.File.WriteAllText(xmlFile, xml, new System.Text.UTF8Encoding(false));
		var fx = FileZillaImporter.Import(xmlFile);
		System.IO.File.Delete(xmlFile);
		eq(fx.Profiles.Count, 3, "fzx count"); eq(fx.Warnings.Count, 2, "fzx warnings (https + master password)");
		var sp = fx.Profiles[0];
		eq(sp.Name, "Мой магазин", "fzx name"); eq(sp.Group, "Клиенты/Магазины", "fzx group"); eq(sp.Protocol, Protocol.Sftp, "fzx sftp");
		eq(sp.Password, "päss", "fzx base64 password"); eq(sp.Passive, false, "fzx active mode"); eq(sp.InitialDir, "/html/www", "fzx remote dir");
		eq(sp.Comment, "тест", "fzx comment"); eq(sp.Bookmarks.Count, 1, "fzx bookmarks"); eq(sp.Bookmarks[0].Remote, "/html/theme", "fzx bookmark remote");
		eq(sp.Maps.Count, 2, "fzx maps");
		eq(fx.Profiles[1].Protocol, Protocol.Ftpes, "fzx ftpes"); eq(fx.Profiles[1].Password, "", "fzx crypt password empty"); eq(fx.Profiles[1].Group, "Клиенты", "fzx group2");
		eq(fx.Profiles[2].User, "anonymous", "fzx anonymous"); eq(fx.Profiles[2].Group, "", "fzx no group");
		eq(FileZillaXmlImporter.ServerPath("1 0 4 html 3 www"), "/html/www", "serverpath"); eq(FileZillaXmlImporter.ServerPath("/a/b"), "/a/b", "serverpath plain");
		eq(FileZillaXmlImporter.ServerPath("1 2 C: 3 usr"), "/usr", "serverpath prefix"); eq(FileZillaXmlImporter.ServerPath(""), "/", "serverpath empty");

		// --- settings export / import ---
		{
			var st = new AppSettings { PollSeconds = 30 };
			var pr = new Profile { Group = "g", Name = "s,1", Host = "h", Port = 22, Protocol = Protocol.Sftp, User = "u", Comment = "a\"b\nc" }; pr.Password = "p@ss"; pr.Maps.Add(new PathMap("C:\\x", "/y"));
			st.Profiles.Add(pr);
			string ex = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ftpsync-ex");
			SettingsExchange.ExportCsv(st, ex + ".csv", true); var c1 = SettingsExchange.ReadCsv(ex + ".csv");
			eq(c1.Count, 1, "csv count"); eq(c1[0].Name, "s,1", "csv name"); eq(c1[0].Password, "p@ss", "csv pass"); eq(c1[0].Protocol.ToString(), "Sftp", "csv proto"); eq(c1[0].Comment, "a\"b\nc", "csv comment"); eq(c1[0].Maps[0].Remote, "/y", "csv map");
			SettingsExchange.ExportCsv(st, ex + "2.csv", false); eq(SettingsExchange.ReadCsv(ex + "2.csv")[0].Password, "", "csv no pass");
			SettingsExchange.ExportXml(st, ex + ".xml", true); var x1 = SettingsExchange.ReadXml(ex + ".xml");
			eq(x1.PollSeconds, 30, "xml poll"); eq(x1.Profiles[0].Password, "p@ss", "xml pass"); eq(x1.Profiles[0].Host, "h", "xml host");
			var t2 = new AppSettings(); t2.Profiles.Add(new Profile { Group = "g", Name = "S,1" });
			eq(SettingsExchange.Merge(t2, x1.Profiles), 1, "merge n"); eq(t2.Profiles.Count, 1, "merge replaces same name");
		}

		// --- terminal emulator ---
		{
			Func<TermScreen, string, string> feed = (sc, t) => { var b = System.Text.Encoding.UTF8.GetBytes(t); sc.Feed(b, b.Length); return t; };
			Func<TermScreen, int, string> row = (sc, r) => sc.RowText(sc.RowAt(sc.ScrollbackCount + r));
			var t1 = new TermScreen(20, 5);
			feed(t1, "hello\r\nworld"); eq(row(t1, 0), "hello", "term row0"); eq(row(t1, 1), "world", "term row1"); eq(t1.CX, 5, "term cx"); eq(t1.CY, 1, "term cy");
			feed(t1, "\x1b[2J\x1b[H"); eq(row(t1, 0), "", "term clear"); eq(t1.CY, 0, "term home");
			feed(t1, "abc\x1b[2D\x1b[K"); eq(row(t1, 0), "a", "term erase to end");
			feed(t1, "\x1b[2J\x1b[1;1H\x1b[31mred\x1b[0m"); eq(t1.RowAt(t1.ScrollbackCount).C[0].Fg, 1, "term sgr fg"); eq(t1.RowAt(t1.ScrollbackCount).C[3].Fg, -1, "term sgr reset");
			feed(t1, "\x1b[38;2;10;20;30mX"); eq(t1.RowAt(t1.ScrollbackCount).C[3].Fg, 0x1000000 | (10 << 16) | (20 << 8) | 30, "term truecolor");
			var t2 = new TermScreen(10, 3);
			feed(t2, "1\r\n2\r\n3\r\n4\r\n5"); eq(t2.ScrollbackCount, 2, "term scrollback"); eq(t2.RowText(t2.RowAt(0)), "1", "term sb row"); eq(row(t2, 2), "5", "term last row");
			var t3 = new TermScreen(5, 3);
			feed(t3, "abcdefgh"); eq(row(t3, 0), "abcde", "term wrap 0"); eq(row(t3, 1), "fgh", "term wrap 1"); eq(t3.RowAt(0).Wrapped, true, "term wrapped flag");
			var t4 = new TermScreen(10, 3);
			feed(t4, "main\x1b[?1049h"); eq(t4.Alt, true, "term alt on"); eq(row(t4, 0), "", "term alt blank"); feed(t4, "vim\x1b[?1049l"); eq(t4.Alt, false, "term alt off"); eq(row(t4, 0), "main", "term main restored");
			var t5 = new TermScreen(10, 3); string rep = null; t5.Reply += s2 => rep = s2;
			feed(t5, "ab\x1b[6n"); eq(rep, "\x1b[1;3R", "term dsr");
			var t6 = new TermScreen(10, 3);
			feed(t6, "漢字x"); eq(t6.CX, 5, "term wide cx"); eq(t6.RowAt(0).C[1].Attr & TermScreen.WideTail, (int)TermScreen.WideTail, "term wide tail");
			var t7 = new TermScreen(10, 4);
			feed(t7, "a\r\nb\r\nc\r\nd"); t7.Resize(10, 2); eq(t7.ScrollbackCount, 2, "term shrink pushes history"); eq(t7.CY, 1, "term shrink cursor"); t7.Resize(12, 5); eq(t7.Cols, 12, "term grow");
			var t8 = new TermScreen(10, 3);
			feed(t8, "\x1b[1;3r\x1b[3;1H\n\x1b[1;3r"); // scroll region sanity
			var t9 = new TermScreen(10, 3); feed(t9, "\xe2\x82"); feed(t9, "\u00ac"); // split utf-8 does not throw
			var t10 = new TermScreen(10, 2); var raw = new byte[] { 0xD0, 0xBF, 0xD1, 0x80 }; t10.Feed(new byte[] { 0xD0 }, 1); t10.Feed(new byte[] { 0xBF }, 1); eq(row(t10, 0), "п", "term utf8 split across chunks");
			feed(t10, "\x1b]0;my title\x07"); eq(t10.Title, "my title", "term osc title");
			var t11 = new TermScreen(10, 2); feed(t11, "\x1b(0lqqk\x1b(Bx"); eq(row(t11, 0), "\u250C\u2500\u2500\u2510x", "term dec line drawing"); feed(t11, "\r\x0e" + "x" + "\x0f" + "x"); eq(row(t11, 0), "xx\u2500\u2510x", "term SO/SI uses G1 only when designated");
			// highlighting / prompt detection
			eq(ShellHighlight.CommandAfterPrompt("root@srv:~# ls -la /var"), "ls -la /var", "prompt root");
			eq(ShellHighlight.CommandAfterPrompt("user@host:/var/www$ tail -f x.log"), "tail -f x.log", "prompt user");
			eq(ShellHighlight.CommandAfterPrompt("[user@host ~]$ pwd"), "pwd", "prompt bracket");
			eq(ShellHighlight.CommandAfterPrompt("(venv) user@h:~$ python x.py"), "python x.py", "prompt venv");
			eq(ShellHighlight.CommandAfterPrompt("total 12"), null, "not a prompt");
			eq(ShellHighlight.CommandAfterPrompt("[sudo] password for user: "), null, "sudo prompt is not a command");
			var hsp = ShellHighlight.Spans("Error: connection refused to 10.0.0.1"); eq(hsp[0] != 0, true, "hl error"); eq(hsp[30] != 0, true, "hl ip");
			var sc2 = ShellHighlight.Spans("root@srv:~# ls -la | grep x"); eq(sc2[0] != 0, true, "hl prompt"); eq(sc2[12] != 0, true, "hl command");
			// command help
			var helpAll = CommandHelp.All(); eq(helpAll.Count > 100, true, "help count");
			eq(CommandHelp.Matches(helpAll.Find(h => h.Command == "tail -f file.log"), "follow log"), true, "help english search");
			var rec = new System.Collections.Generic.List<string>(); CommandHelp.Touch(rec, "a", 3); CommandHelp.Touch(rec, "b", 3); CommandHelp.Touch(rec, "a", 3); eq(rec[0], "a", "recent order"); eq(rec.Count, 2, "recent unique");
		}

		// --- own FTP client against a mock server ---
		{
			var ms = new MockFtp(); ms.Files["/home/u/a.txt"] = new UTF8Encoding(false).GetBytes("hello");
			var pr = new Profile { Name = "mock", Host = "127.0.0.1", Port = ms.Port, User = "u", Protocol = Protocol.Ftp, Passive = true };
			RemoteFactory.SetPassword(pr, "pw");
			using (var fs = new FtpRemote(pr, 5000))
			{
				eq(fs.GetHome(), "/home/u", "ftp pwd");
				var ls = fs.List("/home/u"); eq(ls.Count, 2, "ftp list count"); eq(ls[0].IsDir, true, "ftp list dir"); eq(ls[1].Name, "a.txt", "ftp list file");
				eq(System.Text.Encoding.UTF8.GetString(fs.Download("/home/u/a.txt")), "hello", "ftp download");
				eq(fs.Download("/home/u/none.txt") == null, true, "ftp download missing = null");
				fs.Upload("/home/u/b.txt", new UTF8Encoding(false).GetBytes("data")); eq(System.Text.Encoding.UTF8.GetString(ms.Files["/home/u/b.txt"]), "data", "ftp upload");
				fs.Rename("/home/u/b.txt", "/home/u/c.txt"); eq(ms.Files.ContainsKey("/home/u/c.txt"), true, "ftp rename");
				fs.Delete("/home/u/c.txt", false); eq(ms.Files.ContainsKey("/home/u/c.txt"), false, "ftp delete");
				fs.MakeDir("/home/u/new");
			}
			lock (ms.Log) eq(ms.Log.Exists(x => x.StartsWith("LIST -a")) && ms.Log.Exists(x => x == "LIST /home/u"), true, "ftp list falls back without -a");
		}

		// --- languages ---
		string ld = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nppl_" + Guid.NewGuid().ToString("N"));
		System.IO.Directory.CreateDirectory(System.IO.Path.Combine(ld, "lang"));
		System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.Combine(ld, "lang"), "en.txt"), "#name: English\nПривет\tHello\\n{0}\nДва\\tтаба\tTwo\\ttabs\n", new System.Text.UTF8Encoding(false));
		L.Init(ld, "en");
		eq(L.T("Привет"), "Hello\n{0}", "L translate"); eq(L.T("Нет такого"), "Нет такого", "L fallback"); eq(L.F("Привет", 5), "Hello\n5", "L format");
		eq(L.T("Два\ttаба"), "Два\ttаба", "L tab key not matched is fallback");
		eq(L.Available()["en"], "English", "L available"); eq(L.Available().ContainsKey("ru"), true, "L ru always");
		L.Init(ld, "xx"); eq(L.T("Привет"), "Привет", "L unknown lang = ru");
		L.Init(ld, "ru"); eq(L.Code, "ru", "L ru code");
		System.IO.Directory.Delete(ld, true);
		// every shipped language file: same placeholders and line breaks as the Russian key
		string shipped = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "lang");
		if (!System.IO.Directory.Exists(shipped)) shipped = System.IO.Path.GetFullPath("lang");
		var tpl = System.IO.File.Exists(System.IO.Path.Combine(shipped, "_template.txt")) ? L.Load(System.IO.Path.Combine(shipped, "_template.txt")) : null;
		if (tpl != null)
			foreach (string lf in System.IO.Directory.GetFiles(shipped, "*.txt"))
			{
				if (System.IO.Path.GetFileName(lf).StartsWith("_")) continue;
				var tr = L.Load(lf);
				eq(tr.Count, tpl.Count, "lang " + System.IO.Path.GetFileName(lf) + " complete");
				foreach (var kv in tpl)
				{
					string v; if (!tr.TryGetValue(kv.Key, out v)) { eq("missing", kv.Key, "lang key"); continue; }
					eq(System.Text.RegularExpressions.Regex.Matches(v, "\\{\\d+\\}").Count, System.Text.RegularExpressions.Regex.Matches(kv.Key, "\\{\\d+\\}").Count, "lang placeholders " + kv.Key);
				}
			}

		// --- clearing backups ---
		string cdir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nppc_" + Guid.NewGuid().ToString("N"));
		var cst = new BackupStore(cdir);
		bool cr;
		string fdir = cst.FileDir("p", "/a/b.txt");
		System.IO.Directory.CreateDirectory(fdir);
		for (int k = 0; k < 5; k++) System.IO.File.WriteAllText(System.IO.Path.Combine(fdir, "2024010" + (k + 1) + "-100000_saved.txt"), "v" + k);
		int cf; long cb;
		cst.Clear(cdir, 0, 3, out cf, out cb);
		eq(cf, 2, "clear keeps newest 3"); eq(BackupStore.VersionsIn(fdir).Count, 3, "3 versions remain");
		cst.Clear(cdir, 36500, 0, out cf, out cb);
		eq(cf, 0, "clear older-than keeps recent");
		cst.Clear(cdir, 0, 0, out cf, out cb);
		eq(cf, 3, "clear all"); eq(System.IO.Directory.Exists(fdir), false, "empty folders removed");
		System.IO.Directory.Delete(cdir, true);

		var names = new System.Collections.Generic.List<string> { "10. b", "2. b", "1. Локалка", "a", "1. МОЙ", "B" };
		names.Sort(NaturalComparer.Instance);
		eq(string.Join("|", names.ToArray()), "1. Локалка|1. МОЙ|2. b|10. b|a|B", "natural sort");

		EventLog.FilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nppg_log_" + Guid.NewGuid().ToString("N") + ".txt");
		EventLog.Info("t", "hello"); EventLog.Error("t", "bad", new InvalidOperationException("boom"));
		eq(EventLog.Snapshot().Count >= 2, true, "log entries");
		eq(EventLog.Last.Level, LogLevel.Error, "log last level");
		eq(EventLog.Last.Details.Contains("boom"), true, "log details");
		eq(System.IO.File.ReadAllText(EventLog.FilePath).Contains("[ERR] t: bad"), true, "log file");
		System.IO.File.Delete(EventLog.FilePath);

		var q = new TransferQueue();
		var log = new System.Collections.Generic.List<string>();
		var done = new System.Threading.ManualResetEvent(false);
		q.Changed += i => { lock (log) { log.Add(i.Id + ":" + i.State); if (i.Id == 3 && (i.State == QState.Done || i.State == QState.Cancelled)) done.Set(); } };
		q.Enqueue("a", "p", "/1", i => { });
		q.Enqueue("b", "p", "/2", i => { throw new Exception("boom"); });
		var third = q.Enqueue("c", "p", "/3", i => { for (int k = 0; k < 50; k++) { System.Threading.Thread.Sleep(10); i.Report(k, 50); } });
		done.WaitOne(5000);
		eq(log.Contains("1:Done"), true, "q1 done"); eq(log.Contains("2:Error"), true, "q2 error"); eq(log.Contains("3:Done"), true, "q3 done");

		var q2 = new TransferQueue();
		var fin = new System.Threading.ManualResetEvent(false);
		QueueItem slow = null;
		q2.Changed += i => { if (i.Id == 1 && i.State == QState.Cancelled) fin.Set(); };
		slow = q2.Enqueue("slow", "p", "/s", i => { for (int k = 0; k < 500; k++) { System.Threading.Thread.Sleep(10); i.Report(k, 500); } });
		System.Threading.Thread.Sleep(80); q2.Abort();
		eq(fin.WaitOne(3000), true, "abort cancels running");
		return fails;
	}
}
