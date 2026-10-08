using System;
using System.Collections.Generic;
using System.Threading;

namespace FtpSync
{
	public enum QState { Queued, Running, Done, Error, Cancelled }

	public class QueueItem
	{
		public int Id;
		public string Action, Profile, Remote;
		public bool IsUpload { get { return Action == L.T("Выкладка") || Action == L.T("Выкладка папки"); } }
		public bool IsDownload { get { return Action == L.T("Скачивание") || Action == L.T("Скачивание папки"); } }
		public long Done, Total;
		public QState State = QState.Queued;
		public string Error, ErrorDetail;
		/// <summary>Work body; runs on the worker thread. Report progress via item.Done/Total and call Check().</summary>
		public Action<QueueItem> Work;
		internal volatile bool CancelRequested;

		public void Check() { if (CancelRequested) throw new OperationCanceledException(); }
		public void Report(long done, long total) { Done = done; Total = total; Check(); Queue.Raise(this); }
		internal TransferQueue Queue;

		public string ProgressText
		{
			get
			{
				switch (State)
				{
					case QState.Queued: return L.T("в очереди");
					case QState.Done: return L.T("готово");
					case QState.Cancelled: return L.T("отменено");
					case QState.Error: return L.T("ошибка");
					default: return Total > 0 ? (int)(Done * 100 / Total) + "%" : TextUtil.FormatSize(Done);
				}
			}
		}
	}

	/// <summary>One worker thread, strictly sequential like NppFTP's transfer queue.</summary>
	public class TransferQueue
	{
		readonly LinkedList<QueueItem> pending = new LinkedList<QueueItem>();
		readonly object gate = new object();
		Thread worker;
		int nextId = 1;
		QueueItem current;

		/// <summary>Raised on the worker thread for every state or progress change.</summary>
		public event Action<QueueItem> Changed;

		internal void Raise(QueueItem i) { Action<QueueItem> h = Changed; if (h != null) h(i); }

		public QueueItem Enqueue(string action, string profile, string remote, Action<QueueItem> work)
		{
			QueueItem it = new QueueItem { Id = nextId++, Action = action, Profile = profile, Remote = remote, Work = work, Queue = this };
			lock (gate)
			{
				pending.AddLast(it);
				if (worker == null) { worker = new Thread(Run) { IsBackground = true, Name = "FtpSyncQueue" }; worker.Start(); }
				Monitor.Pulse(gate);
			}
			Raise(it);
			return it;
		}

		public void Abort()
		{
			List<QueueItem> dropped = new List<QueueItem>();
			lock (gate)
			{
				foreach (QueueItem i in pending) { i.State = QState.Cancelled; dropped.Add(i); }
				pending.Clear();
				if (current != null) current.CancelRequested = true;
			}
			foreach (QueueItem i in dropped) Raise(i);
		}

		public bool Busy { get { lock (gate) return current != null || pending.Count > 0; } }

		void Run()
		{
			while (true)
			{
				QueueItem it;
				lock (gate)
				{
					while (pending.Count == 0) Monitor.Wait(gate);
					it = pending.First.Value; pending.RemoveFirst(); current = it;
				}
				it.State = QState.Running; Raise(it);
				if (it.IsUpload) EventLog.Upload(L.T("Очередь"), it.Action + " " + it.Remote + (string.IsNullOrEmpty(it.Profile) ? "" : "  [" + it.Profile + "]") + L.T(": отправка"));
				try { it.Work(it); if (it.State == QState.Running) it.State = QState.Done; }
				catch (OperationCanceledException) { it.State = QState.Cancelled; }
				catch (Exception ex) { it.State = QState.Error; it.Error = ex.Message; it.ErrorDetail = ex.ToString(); }
				lock (gate) current = null;
				string what = it.Action + " " + it.Remote + (string.IsNullOrEmpty(it.Profile) ? "" : "  [" + it.Profile + "]");
				if (it.State == QState.Error) EventLog.Error(L.T("Очередь"), what + ": " + it.Error, it.ErrorDetail);
				else if (it.State == QState.Cancelled) EventLog.Warn(L.T("Очередь"), what + L.T(": отменено"));
				else EventLog.Ok(L.T("Очередь"), what + L.T(": готово"));
				Raise(it);
			}
		}
	}
}
