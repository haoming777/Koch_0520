using MT.Camera.SDK;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Utils;

namespace Hardware
{
	public class CameraManager : IDisposable
	{
		private readonly Dictionary<int, DaHuaSDK> _cameras = new Dictionary<int, DaHuaSDK>();
		private readonly Dictionary<int, CameraInfo> _cameraInfos = new Dictionary<int, CameraInfo>();
		private bool _disposed;
		private bool _simulateMode;
		public int ConnectionTimeoutMs { get; set; } = 10000;  // 连接超时10秒

		public event Action<int, Bitmap> OnImageReceived;
		public event Action<int, bool> OnConnectionChanged;

		public class CameraInfo
		{
			public int CameraId { get; set; }
			public string SerialNumber { get; set; }
			public string Name { get; set; }
			public string StationName { get; set; }
			public bool IsConnected { get; set; }
			public bool IsStreaming { get; set; }
			public string LastError { get; set; }
		}

		public CameraManager(bool simulateMode = true)
		{
			_simulateMode = simulateMode;

			var configs = new (int id, string sn, string name, string station)[]
			{
				(1, SystemConfig.GetValue("Camera1SN", ""), "正面左", "Front"),
				(2, SystemConfig.GetValue("Camera2SN", ""), "正面右", "Front"),
				(3, SystemConfig.GetValue("Camera3SN", ""), "背面左", "Back"),
				(4, SystemConfig.GetValue("Camera4SN", ""), "背面右", "Back"),
				(5, SystemConfig.GetValue("Camera5SN", ""), "上端面", "EndFace"),
				(6, SystemConfig.GetValue("Camera6SN", ""), "下端面", "EndFace"),
				(7, SystemConfig.GetValue("Camera7SN", ""), "左侧面", "Side"),
				(8, SystemConfig.GetValue("Camera8SN", ""), "右侧面", "Side"),
			};

			foreach (var cfg in configs)
			{
				_cameraInfos[cfg.id] = new CameraInfo
				{
					CameraId = cfg.id,
					SerialNumber = cfg.sn,
					Name = cfg.name,
					StationName = cfg.station,
					IsConnected = false,
					IsStreaming = false
				};
			}
		}

		public bool InitializeAll()
		{
			Logger.Info($"========== 初始化相机（模拟模式: {_simulateMode}） ==========");
			bool allOk = true;

			foreach (var info in _cameraInfos.Values)
			{
				bool success = InitializeSingleCamera(info);
				if (!success) allOk = false;
			}

			Logger.Info($"========== 相机初始化{(allOk ? "全部成功" : "部分失败")} ==========");
			return allOk;
		}

		private bool InitializeSingleCamera(CameraInfo info)
		{
			try
			{
				if (_simulateMode)
				{
					info.IsConnected = true;
					info.IsStreaming = true;
					OnConnectionChanged?.Invoke(info.CameraId, true);
					Logger.Info($"Camera{info.CameraId}({info.Name}) 模拟模式初始化成功");
					return true;
				}

				if (string.IsNullOrEmpty(info.SerialNumber))
				{
					Logger.Warning($"Camera{info.CameraId}({info.Name}) 序列号未配置，跳过");
					return false;
				}

				Logger.Info($"正在连接 Camera{info.CameraId}({info.Name}) 序列号: {info.SerialNumber}");

				var camera = new DaHuaSDK();
				camera.SetCameraInterface(new CameraCallback(this, info.CameraId));
				camera.SetCameraByKey(info.SerialNumber);

				// 使用超时机制打开相机
				var openTask = Task.Run(() => camera.Open());
				if (!openTask.Wait(ConnectionTimeoutMs))
				{
					Logger.Error($"Camera{info.CameraId} 连接超时({ConnectionTimeoutMs}ms)");
					info.LastError = "连接超时";
					return false;
				}

				camera.SetAcquisitionMode(0);
				camera.SetTriggerMode(1);
				camera.setTriggerSource(0);

				_cameras[info.CameraId] = camera;
				info.IsConnected = true;
				OnConnectionChanged?.Invoke(info.CameraId, true);
				Logger.Info($"Camera{info.CameraId}({info.Name}) 连接成功");
				return true;
			}
			catch (Exception ex)
			{
				Logger.Error($"Camera{info.CameraId} 初始化失败: {ex.Message}");
				info.LastError = ex.Message;
				info.IsConnected = false;
				return false;
			}
		}

		public void StartAll()
		{
			foreach (var info in _cameraInfos.Values)
			{
				if (_simulateMode)
				{
					info.IsStreaming = true;
					Logger.Debug($"Camera{info.CameraId}({info.Name}) 模拟模式已启动");
					continue;
				}

				// 关键：检查相机对象是否存在
				if (_cameras.TryGetValue(info.CameraId, out var camera))
				{
					try
					{
						camera.StartStreamGrabber();
						info.IsStreaming = true;
						Logger.Info($"Camera{info.CameraId}({info.Name}) 开始采集");
					}
					catch (Exception ex)
					{
						Logger.Error($"Camera{info.CameraId}启动失败: {ex.Message}");
						info.IsStreaming = false;
					}
				}
				else
				{
					Logger.Warning($"Camera{info.CameraId}({info.Name}) 相机对象不存在，跳过启动");
				}
			}
			Logger.Info(_simulateMode ? "模拟模式：所有相机已启动" : "所有相机已启动");
		}

		public void StopAll()
		{
			foreach (var kvp in _cameras)
			{
				try { kvp.Value.StopStreamGrabber(); } catch { }
			}
			foreach (var info in _cameraInfos.Values)
			{
				info.IsStreaming = false;
			}
			Logger.Info("所有相机已停止");
		}

		public bool IsConnected(int cameraId)
		{
			return _cameraInfos.TryGetValue(cameraId, out var info) && info.IsConnected;
		}

		public CameraInfo GetInfo(int cameraId)
		{
			return _cameraInfos.TryGetValue(cameraId, out var info) ? info : null;
		}

		public List<CameraInfo> GetAllInfos()
		{
			return new List<CameraInfo>(_cameraInfos.Values);
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			StopAll();
			foreach (var kvp in _cameras) { try { kvp.Value.Close(); } catch { } }
			_cameras.Clear();
			Logger.Info("相机管理器已释放");
		}

		private class CameraCallback : ICamera
		{
			private readonly CameraManager _manager;
			private readonly int _cameraId;

			public CameraCallback(CameraManager manager, int cameraId)
			{
				_manager = manager;
				_cameraId = cameraId;
			}

			public void OnCameraOpen(string cameraName, string cameraKey)
			{
				if (_manager._cameraInfos.TryGetValue(_cameraId, out var info))
				{
					info.IsConnected = true;
					_manager.OnConnectionChanged?.Invoke(_cameraId, true);
					Logger.Info($"Camera{_cameraId}({info.Name}) 已连接");
				}
			}

			public void OnCameraClose(string cameraName, string cameraKey)
			{
				if (_manager._cameraInfos.TryGetValue(_cameraId, out var info))
				{
					info.IsConnected = false;
					_manager.OnConnectionChanged?.Invoke(_cameraId, false);
					Logger.Warning($"Camera{_cameraId}({info.Name}) 已断开");
				}
			}

			public void OnCameraConnectLoss(string cameraName, string cameraKey)
			{
				if (_manager._cameraInfos.TryGetValue(_cameraId, out var info))
				{
					info.IsConnected = false;
					_manager.OnConnectionChanged?.Invoke(_cameraId, false);
					Logger.Warning($"Camera{_cameraId}({info.Name}) 掉线");
				}
			}

			public void OnImage(Bitmap bitmap, string cameraName, string cameraKey)
			{
				if (bitmap != null)
				{
					_manager.OnImageReceived?.Invoke(_cameraId, bitmap);
				}
			}
		}
	}
}