using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CommonLib
{
	public class HighSpeedImageSaver : IDisposable
	{
		private readonly BlockingCollection<ImageTask> _queue = new BlockingCollection<ImageTask>();
		private readonly CancellationTokenSource _cts = new CancellationTokenSource();
		private bool _isDisposed = false;
		private Task _workerTask;  // 保存后台工作任务引用

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

		// 【新增补丁】完美兼容旧代码：4个参数，直接存 byte[] 字节流 
		// 报错信息：参数1: string, 参数2: byte[], 参数3: bool
		public void AddSaveTask(string path, byte[] data, bool flag, object extra = null)
		{
			if (data == null || data.Length == 0 || _isDisposed) return;

			// 异步直接写入字节流到文件，但使用取消令牌
			Task.Run(() =>
			{
				if (_cts.Token.IsCancellationRequested) return;

				try
				{
					string dir = Path.GetDirectoryName(path);
					if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
						Directory.CreateDirectory(dir);

					File.WriteAllBytes(path, data);
				}
				catch (OperationCanceledException)
				{
					// 任务被取消，正常退出
				}
				catch (Exception ex)
				{
					Logger.Error($"存Byte图失败: {path}, {ex.Message}");
				}
			}, _cts.Token);
		}

		public void Stop()
		{
			if (!_isDisposed)
			{
				_queue.CompleteAdding();
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
					try
					{
						_workerTask.Wait(5000); // 最多等待5秒
					}
					catch (AggregateException)
					{
						// 忽略任务取消异常
					}
				}
				
				_cts.Dispose();
				_queue.Dispose();
				_isDisposed = true;
				Logger.Info("HighSpeedImageSaver 已释放");
			}
		}
	}
}