using System;
using System.Diagnostics;

namespace Utils
{
	/// <summary>
	/// 计时器作用域 - using 语句自动记录耗时
	/// </summary>
	public class StopwatchScope : IDisposable
	{
		private Stopwatch _stopwatch;
		private Action<double> _callback;
		private bool _disposed = false;

		public StopwatchScope(Action<double> callback)
		{
			_callback = callback;
			_stopwatch = Stopwatch.StartNew();
		}

		public void Dispose()
		{
			if (!_disposed)
			{
				_disposed = true;
				_stopwatch.Stop();
				_callback?.Invoke(_stopwatch.Elapsed.TotalMilliseconds);
			}
		}

		public double ElapsedMilliseconds => _stopwatch.Elapsed.TotalMilliseconds;

		public void Restart()
		{
			_stopwatch.Restart();
		}
	}
}