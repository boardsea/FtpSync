using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FtpSync
{
	/// <summary>Notepad++ / Scintilla messaging.</summary>
	public static class Npp
	{
		public static IntPtr Hwnd, Sci1, Sci2;

		const uint WM_USER = 0x400;
		const uint NPPMSG = WM_USER + 1000;
		const uint NPPM_GETCURRENTSCINTILLA = NPPMSG + 4;
		const uint NPPM_DMMSHOW = NPPMSG + 30;
		const uint NPPM_DMMHIDE = NPPMSG + 31;
		const uint NPPM_DMMREGASDCKDLG = NPPMSG + 33;
		const uint NPPM_RELOADFILE = NPPMSG + 36;
		const uint NPPM_SWITCHTOFILE = NPPMSG + 37;
		const uint NPPM_GETPLUGINSCONFIGDIR = NPPMSG + 46;
		const uint NPPM_MENUCOMMAND = NPPMSG + 48;
		const uint NPPM_GETFULLPATHFROMBUFFERID = NPPMSG + 58;
		const uint NPPM_GETCURRENTBUFFERID = NPPMSG + 60;
		const uint NPPM_DOOPEN = NPPMSG + 77;
		const uint NPPM_GETFULLCURRENTPATH = WM_USER + 3000 + 1;
		const int IDM_FILE_NEW = 41001;

		public const uint NPPN_FILEOPENED = 1004, NPPN_FILEBEFORESAVE = 1007, NPPN_FILESAVED = 1008,
			NPPN_SHUTDOWN = 1009, NPPN_BUFFERACTIVATED = 1010, NPPN_READY = 1001, NPPN_FILECLOSED = 1005;

		const uint SCI_GETEOLMODE = 2030, SCI_GETLENGTH = 2006, SCI_GETTEXT = 2182, SCI_GETMODIFY = 2159,
			SCI_GETCODEPAGE = 2137, SCI_SETTARGETRANGE = 2686, SCI_REPLACETARGET = 2194,
			SCI_BEGINUNDOACTION = 2078, SCI_ENDUNDOACTION = 2079, SCI_SETTEXT = 2181, SCI_SETSAVEPOINT = 2014;

		[DllImport("user32.dll", EntryPoint = "SendMessageW")]
		static extern IntPtr Send(IntPtr h, uint msg, IntPtr w, IntPtr l);
		[DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
		static extern IntPtr SendStr(IntPtr h, uint msg, IntPtr w, string l);
		[DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
		static extern IntPtr SendSb(IntPtr h, uint msg, IntPtr w, StringBuilder l);
		[DllImport("user32.dll", EntryPoint = "SendMessageW")]
		static extern IntPtr SendBytes(IntPtr h, uint msg, IntPtr w, byte[] l);

		public static IntPtr Sci()
		{
			IntPtr mem = Marshal.AllocHGlobal(4);
			try
			{
				Marshal.WriteInt32(mem, -1);
				Send(Hwnd, NPPM_GETCURRENTSCINTILLA, IntPtr.Zero, mem);
				return Marshal.ReadInt32(mem) == 1 ? Sci2 : Sci1;
			}
			finally { Marshal.FreeHGlobal(mem); }
		}

		public static string ConfigDir()
		{
			StringBuilder sb = new StringBuilder(520);
			SendSb(Hwnd, NPPM_GETPLUGINSCONFIGDIR, (IntPtr)520, sb);
			return sb.ToString();
		}

		public static string CurrentPath()
		{
			StringBuilder sb = new StringBuilder(520);
			SendSb(Hwnd, NPPM_GETFULLCURRENTPATH, (IntPtr)520, sb);
			return sb.ToString();
		}

		public static IntPtr CurrentBufferId() { return Send(Hwnd, NPPM_GETCURRENTBUFFERID, IntPtr.Zero, IntPtr.Zero); }

		public static string PathFromBufferId(IntPtr id)
		{
			StringBuilder sb = new StringBuilder(520);
			SendSb(Hwnd, NPPM_GETFULLPATHFROMBUFFERID, id, sb);
			return sb.ToString();
		}

		public static bool IsModified() { return Send(Sci(), SCI_GETMODIFY, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero; }

		public static bool IsUtf8() { return (int)Send(Sci(), SCI_GETCODEPAGE, IntPtr.Zero, IntPtr.Zero) == 65001; }

		public static byte[] GetText()
		{
			IntPtr sci = Sci();
			int len = (int)Send(sci, SCI_GETLENGTH, IntPtr.Zero, IntPtr.Zero);
			byte[] buf = new byte[len + 1];
			SendBytes(sci, SCI_GETTEXT, (IntPtr)(len + 1), buf);
			byte[] r = new byte[len];
			Array.Copy(buf, r, len);
			return r;
		}

		/// <summary>Replaces the whole document (undoable). Text uses LF and is converted to the document's EOL mode.</summary>
		public static void SetText(string textLf)
		{
			IntPtr sci = Sci();
			int eol = (int)Send(sci, SCI_GETEOLMODE, IntPtr.Zero, IntPtr.Zero);
			string t = textLf.Replace("\r\n", "\n").Replace('\r', '\n');
			if (eol == 0) t = t.Replace("\n", "\r\n"); else if (eol == 1) t = t.Replace('\n', '\r');
			Encoding enc = IsUtf8() ? (Encoding)new UTF8Encoding(false) : Encoding.Default;
			byte[] b = enc.GetBytes(t);
			byte[] z = new byte[b.Length + 1];
			Array.Copy(b, z, b.Length);
			int len = (int)Send(sci, SCI_GETLENGTH, IntPtr.Zero, IntPtr.Zero);
			Send(sci, SCI_BEGINUNDOACTION, IntPtr.Zero, IntPtr.Zero);
			Send(sci, SCI_SETTARGETRANGE, IntPtr.Zero, (IntPtr)len);
			SendBytes(sci, SCI_REPLACETARGET, (IntPtr)b.Length, z);
			Send(sci, SCI_ENDUNDOACTION, IntPtr.Zero, IntPtr.Zero);
		}

		public static void Open(string path) { SendStr(Hwnd, NPPM_DOOPEN, IntPtr.Zero, path); }
		public static void Switch(string path) { SendStr(Hwnd, NPPM_SWITCHTOFILE, IntPtr.Zero, path); }
		public static void Reload(string path) { SendStr(Hwnd, NPPM_RELOADFILE, IntPtr.Zero, path); }

		/// <summary>New untitled tab with the given text.</summary>
		public static void NewTab(string textLf)
		{
			Send(Hwnd, NPPM_MENUCOMMAND, IntPtr.Zero, (IntPtr)IDM_FILE_NEW);
			SetText(textLf);
		}

		[StructLayout(LayoutKind.Sequential)]
		struct RECT { public int left, top, right, bottom; }

		[StructLayout(LayoutKind.Sequential)]
		struct TbData
		{
			public IntPtr hClient; public IntPtr pszName; public int dlgID; public uint uMask;
			public IntPtr hIconTab; public IntPtr pszAddInfo; public RECT rcFloat; public int iPrevCont; public IntPtr pszModuleName;
		}

		static IntPtr tb;

		public static void RegisterDock(IntPtr clientHwnd, string title, int id, uint mask)
		{
			TbData d = new TbData();
			d.hClient = clientHwnd;
			d.pszName = Marshal.StringToHGlobalUni(title);
			d.dlgID = id;
			d.uMask = mask;
			d.pszAddInfo = Marshal.StringToHGlobalUni("");
			d.iPrevCont = -1;
			d.pszModuleName = Marshal.StringToHGlobalUni("FtpSync.dll");
			IntPtr tb = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TbData)));
			Marshal.StructureToPtr(d, tb, false);
			Send(Hwnd, NPPM_DMMREGASDCKDLG, IntPtr.Zero, tb);
		}

		public const uint DockRight = 0x10000000, DockBottom = 0x30000000;

		public static void ShowDock(IntPtr h) { Send(Hwnd, NPPM_DMMSHOW, IntPtr.Zero, h); }
		public static void HideDock(IntPtr h) { Send(Hwnd, NPPM_DMMHIDE, IntPtr.Zero, h); }

		const uint NPPM_ADDTOOLBARICON = NPPMSG + 41;
		public const uint NPPN_TBMODIFICATION = 1002;

		[StructLayout(LayoutKind.Sequential)]
		struct ToolbarIcons { public IntPtr hBmp; public IntPtr hIcon; }

		public static void AddToolbarIcon(int cmdId, System.Drawing.Bitmap bmp)
		{
			ToolbarIcons t = new ToolbarIcons();
			t.hBmp = bmp.GetHbitmap(System.Drawing.Color.Fuchsia);
			t.hIcon = bmp.GetHicon();
			IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ToolbarIcons)));
			Marshal.StructureToPtr(t, p, false);
			Send(Hwnd, NPPM_ADDTOOLBARICON, (IntPtr)cmdId, p);
		}
	}
}
