using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Renci.SshNet;

namespace FtpSync
{
	/// <summary>Interactive SSH shell (PTY) of a profile: bytes in, bytes out, window resize.</summary>
	public class SshSession
	{
		readonly Profile p;
		SshClient client;
		ShellStream stream;
		Thread reader;
		volatile bool closed;
		readonly object writeGate = new object();
		readonly AutoResetEvent data = new AutoResetEvent(false);
		FieldInfo channelField; PropertyInfo openProp;

		public event Action<byte[], int> Received;
		public event Action<string> Ended;     // reason (null = closed by the user)

		public SshSession(Profile profile) { p = profile; }

		public bool IsOpen { get { return !closed && client != null && client.IsConnected; } }

		/// <summary>Connects and starts reading. Throws on failure.</summary>
		public void Open(int cols, int rows)
		{
			AuthenticationMethod auth;
			string pw = RemoteFactory.PasswordFor(p);
			if (!string.IsNullOrEmpty(p.KeyFile) && File.Exists(p.KeyFile))
			{
				PrivateKeyFile key = string.IsNullOrEmpty(pw) ? new PrivateKeyFile(p.KeyFile) : new PrivateKeyFile(p.KeyFile, pw);
				auth = new PrivateKeyAuthenticationMethod(p.User, key);
			}
			else auth = new PasswordAuthenticationMethod(p.User, pw);
			ConnectionInfo ci = new ConnectionInfo(p.Host, p.Port, p.User, auth);
			ci.Timeout = TimeSpan.FromMilliseconds(Math.Max(15000, RemoteFactory.TimeoutMs));
			client = new SshClient(ci);
			client.KeepAliveInterval = TimeSpan.FromSeconds(30);
			client.Connect();
			stream = client.CreateShellStream("xterm-256color", (uint)cols, (uint)rows, 0, 0, 8192);
			channelField = typeof(ShellStream).GetField("_channel", BindingFlags.NonPublic | BindingFlags.Instance);
			if (channelField != null) { object ch = channelField.GetValue(stream); if (ch != null) openProp = ch.GetType().GetProperty("IsOpen"); }
			stream.DataReceived += delegate { data.Set(); };
			reader = new Thread(ReadLoop) { IsBackground = true, Name = "FtpSync SSH" };
			reader.Start();
		}

		bool ChannelOpen()
		{
			try { if (channelField != null && openProp != null) return (bool)openProp.GetValue(channelField.GetValue(stream), null); }
			catch (Exception) { }
			return true;
		}

		void ReadLoop()
		{
			byte[] buf = new byte[16384];
			string why = null;
			try
			{
				while (!closed)
				{
					if (stream.DataAvailable)
					{
						int n = stream.Read(buf, 0, buf.Length);
						if (n > 0) { Action<byte[], int> r = Received; if (r != null) r(buf, n); }
						continue;
					}
					if (!client.IsConnected) { why = "connection lost"; break; }
					if (!ChannelOpen()) { Thread.Sleep(50); if (!stream.DataAvailable) break; continue; }
					data.WaitOne(40);
				}
			}
			catch (Exception ex) { if (!closed) why = ex.Message; }
			bool byUser = closed;
			closed = true;
			Action<string> e = Ended; if (e != null) e(byUser ? null : (why ?? ""));
		}

		public void Write(byte[] b)
		{
			if (closed || stream == null) return;
			try { lock (writeGate) { stream.Write(b, 0, b.Length); stream.Flush(); } }
			catch (Exception) { }
		}

		public void Write(string s) { Write(new System.Text.UTF8Encoding(false).GetBytes(s)); }

		public void Resize(int cols, int rows)
		{
			if (closed || stream == null || channelField == null) return;
			try
			{
				object ch = channelField.GetValue(stream);
				MethodInfo m = ch.GetType().GetMethod("SendWindowChangeRequest", new[] { typeof(uint), typeof(uint), typeof(uint), typeof(uint) });
				if (m != null) m.Invoke(ch, new object[] { (uint)cols, (uint)rows, 0u, 0u });
			}
			catch (Exception) { }
		}

		public void Close()
		{
			closed = true; data.Set();
			try { if (stream != null) stream.Dispose(); } catch (Exception) { }
			try { if (client != null) { if (client.IsConnected) client.Disconnect(); client.Dispose(); } } catch (Exception) { }
		}
	}
}
