using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XL.Tool;

namespace Utils
{
	public class HighSpeedImageSaver : IDisposable
	{
		private readonly BlockingCollection<SaveTask> _saveQueue;
		private readonly Thread[] _workerThreads;
		private bool _disposed = false;
		private readonly string _saverName;
		private readonly int _threadCount;
		private long _totalSaved = 0;
		private long _totalFailed = 0;
		private readonly object _statLock = new object();

		// 统计属性
		public long TotalSaved => Interlocked.Read(ref _totalSaved);
		public long TotalFailed => Interlocked.Read(ref _totalFailed);
		public int QueueCount => _saveQueue.Count;

		private class SaveTask
		{
			public string FilePath { get; set; }
			public byte[] ImageData { get; set; }
			public bool IsJpg { get; set; }
			public int Quality { get; set; }
			public DateTime EnqueueTime { get; set; }
			public Action<string, bool> Callback { get; set; }  // 保存完成回调
		}

		public HighSpeedImageSaver(string name = "高速保存器", int threadCount = 4, int maxQueueSize = 500)
		{
			_saverName = name;
			_threadCount = Math.Max(1, Math.Min(threadCount, 8));
			_saveQueue = new BlockingCollection<SaveTask>(new ConcurrentQueue<SaveTask>(), maxQueueSize);

			_workerThreads = new Thread[_threadCount];
			for (int i = 0; i < _threadCount; i++)
			{
				_workerThreads[i] = new Thread(WorkerMethod)
				{
					Name = $"{name}_Worker{i + 1}",
					IsBackground = true,
					Priority = ThreadPriority.BelowNormal
				};
				_workerThreads[i].Start();
			}

			Logger.Info($"高速保存器初始化完成: 线程数={_threadCount}, 队列大小={maxQueueSize}");
		}

		private volatile bool _isStopping = false;

		public void Flush(int timeoutMs = 5000)
		{
			if (_saveQueue.Count == 0) return;

			var startTime = DateTime.Now;
			while (_saveQueue.Count > 0 && (DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
			{
				Thread.Sleep(10);
			}
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			_isStopping = true;

			Flush(5000);
			_saveQueue.CompleteAdding();

			foreach (var thread in _workerThreads)
			{
				if (thread != null && thread.IsAlive)
				{
					try { thread.Join(2000); } catch { }
				}
			}
			_saveQueue.Dispose();

			Logger.Info($"高速保存器已释放: 总保存={_totalSaved}, 失败={_totalFailed}");
		}

		private void WorkerMethod()
		{
			try
			{
				foreach (var task in _saveQueue.GetConsumingEnumerable())
				{
					if (_isStopping && _saveQueue.Count == 0) break;

					bool success = SaveImageDirect(task);

					if (success)
						Interlocked.Increment(ref _totalSaved);
					else
						Interlocked.Increment(ref _totalFailed);

					task.Callback?.Invoke(task.FilePath, success);
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"保存器工作线程异常: {ex.Message}");
			}
		}

		public bool AddSaveTask(string filePath, byte[] imageData, bool isJpg = true, int quality = 85,
			Action<string, bool> callback = null)
		{
			if (_disposed || imageData == null || imageData.Length == 0) return false;

			var task = new SaveTask
			{
				FilePath = filePath,
				ImageData = imageData,
				IsJpg = isJpg,
				Quality = quality,
				EnqueueTime = DateTime.Now,
				Callback = callback
			};

			try
			{
				return _saveQueue.TryAdd(task, 100);
			}
			catch
			{
				return false;
			}
		}

		private bool SaveImageDirect(SaveTask task)
		{
			try
			{
				string directory = Path.GetDirectoryName(task.FilePath);
				if (!Directory.Exists(directory))
					Directory.CreateDirectory(directory);

				File.WriteAllBytes(task.FilePath, task.ImageData);
				return true;
			}
			catch (Exception ex)
			{
				Logger.Error($"文件写入失败 {task.FilePath}: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// 获取统计信息
		/// </summary>
		public (long saved, long failed, int queued) GetStats()
		{
			return (TotalSaved, TotalFailed, QueueCount);
		}
	}
}