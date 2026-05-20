using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using XL.Tool;

namespace Utils
{
	public class ImageBufferPool : IDisposable
	{
		private class PoolItem<T> where T : IDisposable
		{
			public T Resource { get; set; }
			public DateTime LastUsed { get; set; }
			public long Size { get; set; }
		}

		private readonly ConcurrentBag<PoolItem<Bitmap>> _bitmapPool = new ConcurrentBag<PoolItem<Bitmap>>();
		private readonly ConcurrentBag<PoolItem<Mat>> _matPool = new ConcurrentBag<PoolItem<Mat>>();
		private readonly int _defaultWidth, _defaultHeight;
		private readonly PixelFormat _pixelFormat;
		private readonly int _initialCapacity, _maxCapacity;
		private readonly long _maxMemoryBytes;
		private long _totalAllocatedMemory = 0;
		private bool _disposed = false;
		private readonly object _lock = new object();
		XLToolClass toolClass = new XLToolClass();

		private long _rentCount = 0, _returnCount = 0, _newAllocationCount = 0, _poolHitCount = 0;
		public string PoolName { get; set; } = "ImageBufferPool";
		public int AvailableBitmapCount => _bitmapPool.Count;
		public int AvailableMatCount => _matPool.Count;
		public double PoolHitRate => _rentCount > 0 ? (double)_poolHitCount / _rentCount * 100 : 0;

		public ImageBufferPool(int width, int height, PixelFormat pixelFormat,
			int initialCapacity = 5, int maxCapacity = 20, long maxMemoryBytes = 500 * 1024 * 1024)
		{
			_defaultWidth = width; _defaultHeight = height;
			_pixelFormat = pixelFormat;
			_initialCapacity = initialCapacity; _maxCapacity = maxCapacity;
			_maxMemoryBytes = maxMemoryBytes;
			InitializePool();
			ThreadPool.QueueUserWorkItem(MonitorPool);
		}

		private void InitializePool()
		{
			for (int i = 0; i < _initialCapacity; i++)
			{
				try
				{
					var bitmap = new Bitmap(_defaultWidth, _defaultHeight, _pixelFormat);
					var mat = new Mat(_defaultHeight, _defaultWidth,
						_pixelFormat == PixelFormat.Format8bppIndexed ? MatType.CV_8UC1 : MatType.CV_8UC3);
					_bitmapPool.Add(new PoolItem<Bitmap> { Resource = bitmap, LastUsed = DateTime.Now });
					_matPool.Add(new PoolItem<Mat> { Resource = mat, LastUsed = DateTime.Now });
				}
				catch { }
			}
		}

		public Mat RentMat()
		{
			Interlocked.Increment(ref _rentCount);
			if (_matPool.TryTake(out var poolItem))
			{
				Interlocked.Increment(ref _poolHitCount);
				poolItem.LastUsed = DateTime.Now;
				return poolItem.Resource;
			}
			Interlocked.Increment(ref _newAllocationCount);
			return new Mat(_defaultHeight, _defaultWidth,
				_pixelFormat == PixelFormat.Format8bppIndexed ? MatType.CV_8UC1 : MatType.CV_8UC3);
		}

		public void ReturnMat(Mat mat)
		{
			if (mat == null || _disposed) return;
			Interlocked.Increment(ref _returnCount);
			if (mat.Width == _defaultWidth && mat.Height == _defaultHeight && _matPool.Count < _maxCapacity)
			{
				mat.SetTo(Scalar.All(0));
				_matPool.Add(new PoolItem<Mat> { Resource = mat, LastUsed = DateTime.Now });
			}
			else { mat.Dispose(); }
		}

		private void MonitorPool(object state)
		{
			while (!_disposed)
			{
				Thread.Sleep(30000);
				DateTime cutoff = DateTime.Now.AddMinutes(-5);
				CleanOldItems(_bitmapPool, cutoff, item => item.Resource.Dispose());
				CleanOldItems(_matPool, cutoff, item => item.Resource.Dispose());
			}
		}

		private void CleanOldItems<T>(ConcurrentBag<PoolItem<T>> pool, DateTime cutoff, Action<PoolItem<T>> dispose) where T : IDisposable
		{
			var temp = new List<PoolItem<T>>();
			while (pool.TryTake(out var item)) temp.Add(item);
			int keep = 0;
			foreach (var item in temp)
			{
				if (item.LastUsed < cutoff && keep < temp.Count - _initialCapacity)
					dispose?.Invoke(item);
				else { pool.Add(item); keep++; }
			}
		}
		
		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			while (_bitmapPool.TryTake(out var b)) b.Resource?.Dispose();
			while (_matPool.TryTake(out var m)) m.Resource?.Dispose();
		}
	}
}