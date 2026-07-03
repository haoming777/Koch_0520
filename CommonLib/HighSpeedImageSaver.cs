using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CommonLib
{
/// <summary>高速图像保存器 — BlockingCollection后台队列, 异步存图不阻塞主流程</summary>
	public class HighSpeedImageSaver : IDisposable
	{
		private readonly BlockingCollection<ImageTask> _queue = new BlockingCollection<ImageTask>();
		private readonly BlockingCollection<ByteTask> _byteQueue = new BlockingCollection<ByteTask>();
		private readonly CancellationTokenSource _cts = new CancellationTokenSource();
		private bool _isDisposed = false;
		private Task _workerTask;      // Bitmap存图后台线程
		private Task _byteWorkerTask;  // byte[]存图后台线程(独立, 替代Task.Run风暴)

		private struct ByteTask
		{
			public byte[] Data;
			public string FilePath;
		}

		private struct ImageTask
		{
			public Bitmap Image;
			public string FilePath;
			public ImageFormat Format;
		}

		public HighSpeedImageSaver(object arg1 = null, object arg2 = null, object arg3 = null)
		{
			Start();
		}

		public void Start()
		{
			// Bitmap存图线程 (BlockingCollection单消费者)
			_workerTask = Task.Run(() =>
			{
				foreach (var task in _queue.GetConsumingEnumerable(_cts.Token))
				{
					try
					{
						string dir = Path.GetDirectoryName(task.FilePath);
						if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
							Directory.CreateDirectory(dir);

						task.Image.Save(task.FilePath, task.Format);
					}
					catch (Exception ex)
					{
						Logger.Error($"存图失败: {task.FilePath}, {ex.Message}");
					}
					finally
					{
						task.Image?.Dispose();
					}
				}
			}, _cts.Token);

			// byte[]存图线程 (BlockingCollection单消费者, 替代原来每个图Task.Run)
			_byteWorkerTask = Task.Run(() =>
			{
				foreach (var task in _byteQueue.GetConsumingEnumerable(_cts.Token))
				{
					try
					{
						string dir = Path.GetDirectoryName(task.FilePath);
						if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
							Directory.CreateDirectory(dir);

						File.WriteAllBytes(task.FilePath, task.Data);
						Logger.Debug($"[ImageSaver] byte存图: {Path.GetFileName(task.FilePath)} 队列={_byteQueue.Count}");
					}
					catch (OperationCanceledException) { break; }
					catch (Exception ex)
					{
						Logger.Error($"存Byte图失败: {task.FilePath}, {ex.Message}");
					}
				}
			}, _cts.Token);
		}

		public void Enqueue(Bitmap bmp, string path, ImageFormat format)
		{
			if (bmp == null || _isDisposed) return;
			_queue.Add(new ImageTask { Image = (Bitmap)bmp.Clone(), FilePath = path, Format = format });
			if (_queue.Count > 100)
				Logger.Warning($"[ImageSaver] 存图队列积压: {_queue.Count}");
		}

		// ================== 兼容旧代码 ==================
		public void AddSaveTask(Bitmap bmp, string path, ImageFormat format)
		{
			Enqueue(bmp, path, format);
		}

		// byte[]字节流存图: 改用BlockingCollection队列, 替代原来每个图Task.Run
		public void AddSaveTask(string path, byte[] data, bool flag, object extra = null)
		{
			if (data == null || data.Length == 0 || _isDisposed) return;
			_byteQueue.Add(new ByteTask { Data = data, FilePath = path });
			if (_byteQueue.Count > 100)
				Logger.Warning($"[ImageSaver] byte队列积压: {_byteQueue.Count}");
		}

		public void Stop()
		{
			if (!_isDisposed)
			{
				_queue.CompleteAdding();
				_byteQueue.CompleteAdding();
				_cts.Cancel();
			}
		}

		public void Dispose()
		{
			if (!_isDisposed)
			{
				Stop();
				
				// 等待后台工作任务完成
				if (_workerTask != null && !_workerTask.IsCompleted)
				{
					try { _workerTask.Wait(5000); }
					catch (AggregateException) { }
				}
				if (_byteWorkerTask != null && !_byteWorkerTask.IsCompleted)
				{
					try { _byteWorkerTask.Wait(5000); }
					catch (AggregateException) { }
				}

				_cts.Dispose();
				_queue.Dispose();
				_byteQueue.Dispose();
				_isDisposed = true;
				Logger.Info("HighSpeedImageSaver 已释放");
			}
		}
	}
}