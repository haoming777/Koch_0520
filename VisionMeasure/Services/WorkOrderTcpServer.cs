using CommonLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace VisionMeasure.Services
{
    /// <summary>协议校验通过的工单TCP包</summary>
    public class WorkOrderPacket
    {
        /// <summary>机台号(已去除右侧补空格)</summary>
        public string MachineId;
        /// <summary>订单号(已去除右侧补空格)</summary>
        public string OrderNo;
        /// <summary>提取出的SKU(已去除右侧补空格)</summary>
        public string Sku;
        /// <summary>客户端端点(IP:端口)</summary>
        public string Endpoint;
        /// <summary>61字节原始报文十六进制(空格分隔)</summary>
        public string RawHex;

        /// <summary>包内容可读化摘要(控制字符已替换为.)</summary>
        public string DecodedText => $"机台号:{MachineId} | 订单号:{OrderNo} | SKU:{Sku}";
    }

    /// <summary>
    /// TCP工单接收服务端 — 监听10001端口, 接收上游系统61字节固定长工单包
    /// 协议: 包头AA55 + 命令01 + 长度0032 + 内容50B(机台号10B+订单号20B+SKU20B) + CRC32大端4B + 包尾0D0A
    /// 本类只负责监听/收包/粘包剥离/协议校验, 业务由PacketReceived事件回调处理
    /// 注意: 事件在后台线程触发, 调用方需自行封送到UI线程
    /// 健壮性: 客户端断线重连、垃圾数据、程序退出等异常均内部消化, 不外抛
    /// </summary>
    public class WorkOrderTcpServer : IDisposable
    {
        public const int PacketLength = 61;
        private const int Port = 10001;
        /// <summary>收包缓冲上限: 长时间收不到有效包头(垃圾数据)时清空, 防止内存无限增长</summary>
        private const int MaxCacheBytes = 8192;

        private Socket _listener;
        private volatile bool _running;
        private readonly ConcurrentDictionary<Socket, byte> _clients = new ConcurrentDictionary<Socket, byte>();

        /// <summary>客户端连接数变化(后台线程触发)</summary>
        public event Action<int> ClientCountChanged;
        /// <summary>收到一个协议校验通过的工单包(后台线程触发)</summary>
        public event Action<WorkOrderPacket> PacketReceived;
        /// <summary>监听socket意外失效, 开始自动重建监听(后台线程触发)</summary>
        public event Action ServerRecovering;
        /// <summary>监听自动重建成功, 服务端已恢复(后台线程触发)</summary>
        public event Action ServerRecovered;

        public int ClientCount => _clients.Count;
        public bool IsRunning => _running;

        public void Start()
        {
            if (_running) return;
            try
            {
                _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _listener.Bind(new IPEndPoint(IPAddress.Any, Port));
                _listener.Listen(10);
                _running = true;
            }
            catch
            {
                try { _listener?.Close(); } catch { }
                _listener = null;
                throw; // 端口被占用等 → 由调用方记录"启动失败"并提示
            }

            var acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "WorkOrderTcpAccept" };
            acceptThread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Close(); } catch { }
            foreach (var kv in _clients)
            {
                try { kv.Key.Shutdown(SocketShutdown.Both); } catch { }
                TryClose(kv.Key);
            }
            _clients.Clear();
        }

        public void Dispose() => Stop();

        private void AcceptLoop()
        {
            while (_running)
            {
                Socket client;
                try { client = _listener.Accept(); }
                catch
                {
                    if (!_running) break; // Stop()关闭监听 → 正常退出

                    // 运行中Accept异常 = 监听socket意外失效: 自动重建监听, 重试直到成功或程序退出
                    ServerRecovering?.Invoke();
                    try { _listener?.Close(); } catch { }
                    _listener = null;
                    if (!TryRebindListener()) break; // _running=false才会到这里
                    ServerRecovered?.Invoke();
                    continue;
                }

                if (!_running) { TryClose(client); break; }

                _clients.TryAdd(client, 0); // 用Socket作key: 同一端点重连也不会串号
                OnClientCountChanged();
                string peer = "";
                try { peer = client.RemoteEndPoint?.ToString() ?? "?"; } catch { peer = "?"; }
                Logger.Info($"[SKU-TCP] 客户端连接: {peer}, 当前连接数:{_clients.Count}");
                SkuTcpLogger.Info($"[客户端连接] 对端:{peer}, 当前连接数:{_clients.Count}");
                var recvThread = new Thread(() => ReceiveLoop(client)) { IsBackground = true, Name = "WorkOrderTcpRecv" };
                recvThread.Start();
            }
        }

        private void ReceiveLoop(Socket client)
        {
            var cache = new List<byte>(PacketLength * 2);
            var buffer = new byte[1024];
            string endpoint = "";
            try { endpoint = client.RemoteEndPoint?.ToString() ?? "?"; } catch { endpoint = "?"; }

            while (_running)
            {
                int n;
                try { n = client.Receive(buffer); }
                catch { break; } // 客户端断开或Stop()
                if (n <= 0) break; // 对端正常关闭

                cache.AddRange(new ArraySegment<byte>(buffer, 0, n));
                if (cache.Count > MaxCacheBytes) TrimCacheToTrailingHeaderByte(cache);

                while (true)
                {
                    int header = FindHeaderIndex(cache);
                    if (header < 0) { TrimCacheToTrailingHeaderByte(cache); break; }
                    if (header > 0) cache.RemoveRange(0, header); // 剔除包头前废数据
                    if (cache.Count < PacketLength) break;       // 半包, 等下一段数据

                    var frame = new byte[PacketLength];
                    cache.CopyTo(0, frame, 0, PacketLength);
                    cache.RemoveRange(0, PacketLength);

                    if (WorkOrderPacketParser.TryParse(frame, out string machineId, out string orderNo, out string sku, out string rawHex))
                    {
                        var packet = new WorkOrderPacket { MachineId = machineId, OrderNo = orderNo, Sku = sku, Endpoint = endpoint, RawHex = rawHex };
                        Logger.Info($"[SKU-TCP] 收到工单包: {packet.DecodedText}, 端点:{endpoint}");
                        OnPacketReceived(packet);
                    }
                    else
                    {
                        // 校验不通过(坏包/错帧)丢弃并记录原始报文, 继续向后同步下一帧
                        string badHex = BitConverter.ToString(frame).Replace("-", " ");
                        Logger.Warning($"[SKU-TCP] 收到无效工单包(校验失败), 已丢弃: {badHex}, 端点:{endpoint}");
                        SkuTcpLogger.Warn($"[TCP] 收到无效工单包(校验失败), 已丢弃: {badHex}, 端点:{endpoint}");
                    }
                }
            }

            byte _;
            _clients.TryRemove(client, out _);
            TryClose(client);
            OnClientCountChanged();
            Logger.Info($"[SKU-TCP] 客户端断开: {endpoint}, 当前连接数:{_clients.Count}");
            SkuTcpLogger.Info($"[客户端断开] 对端:{endpoint}, 当前连接数:{_clients.Count}");
        }

        /// <summary>重建监听socket: 每2s重试一次直到成功或Stop(); 返回false=程序已停止</summary>
        private bool TryRebindListener()
        {
            while (_running)
            {
                Socket s = null;
                try
                {
                    s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    s.Bind(new IPEndPoint(IPAddress.Any, Port));
                    s.Listen(10);
                    _listener = s;
                    return true;
                }
                catch
                {
                    try { s?.Close(); } catch { }
                    Thread.Sleep(2000); // 端口暂未释放等, 2s后重试
                }
            }
            return false;
        }

        /// <summary>在缓存中搜索包头AA 55, 返回起始索引; 未找到返回-1</summary>
        private static int FindHeaderIndex(List<byte> cache)
        {
            for (int i = 0; i <= cache.Count - 2; i++)
                if (cache[i] == 0xAA && cache[i + 1] == 0x55) return i;
            return -1;
        }

        /// <summary>清空缓存, 仅保留末尾孤立0xAA(包头可能被TCP拆段: AA已到、55未到)</summary>
        private static void TrimCacheToTrailingHeaderByte(List<byte> cache)
        {
            if (cache.Count > 0 && cache[cache.Count - 1] == 0xAA) cache.RemoveRange(0, cache.Count - 1);
            else cache.Clear();
        }

        private static void TryClose(Socket s)
        {
            try { s.Close(); } catch { }
        }

        private void OnClientCountChanged() => ClientCountChanged?.Invoke(ClientCount);

        private void OnPacketReceived(WorkOrderPacket packet) => PacketReceived?.Invoke(packet);
    }

    /// <summary>
    /// 61字节工单包解析器 — 纯静态, 校验失败返回false, 绝不抛异常
    /// 校验: 包头AA55 | 包尾0D0A | 长度字段=50 | CRC32(偏移2~54共53字节, 标准CRC-32/ISO-HDLC, 与Java java.util.zip.CRC32一致)
    /// 自测向量(技术文档示例包): AA 55 01 00 32 ... 86 ED 4A 1B 0D 0A → SKU=61054721
    /// </summary>
    public static class WorkOrderPacketParser
    {
        private const int SkuOffset = 35;
        private const int SkuLength = 20;

        public static bool TryParse(byte[] frame, out string machineId, out string orderNo, out string sku, out string rawHex)
        {
            machineId = "";
            orderNo = "";
            sku = "";
            rawHex = "";
            try
            {
                if (frame == null || frame.Length != WorkOrderTcpServer.PacketLength) return false;
                if (frame[0] != 0xAA || frame[1] != 0x55) return false;
                if (frame[59] != 0x0D || frame[60] != 0x0A) return false;
                if (frame[3] != 0x00 || frame[4] != 0x32) return false; // 长度字段必须=50

                uint calculated = Crc32(frame, 2, 53);
                uint received = ((uint)frame[55] << 24) | ((uint)frame[56] << 16) | ((uint)frame[57] << 8) | frame[58];
                if (calculated != received) return false;

                machineId = DecodeField(frame, 5, 10);
                orderNo = DecodeField(frame, 15, 20);
                sku = DecodeField(frame, SkuOffset, SkuLength);
                rawHex = BitConverter.ToString(frame).Replace("-", " ");
                return true;
            }
            catch { return false; } // 任何解析异常不外抛
        }

        /// <summary>UTF8解码字段并去右补空格; 控制字符替换为.(防止垃圾数据污染日志)</summary>
        private static string DecodeField(byte[] frame, int offset, int length)
        {
            string s = Encoding.UTF8.GetString(frame, offset, length).TrimEnd(' ');
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append(c < 0x20 ? '.' : c);
            return sb.ToString();
        }

        // ===== 标准CRC-32 (ISO-HDLC, 反射型查表, 与Java java.util.zip.CRC32一致) =====
        private static readonly uint[] CrcTable = BuildTable();

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            const uint poly = 0xEDB88320;
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                    crc = (crc & 1) == 1 ? (crc >> 1) ^ poly : crc >> 1;
                table[i] = crc;
            }
            return table;
        }

        private static uint Crc32(byte[] data, int offset, int length)
        {
            uint result = 0xFFFFFFFF;
            for (int i = offset; i < offset + length; i++)
            {
                byte index = (byte)((result & 0xFF) ^ data[i]);
                result = (result >> 8) ^ CrcTable[index];
            }
            return ~result;
        }
    }
}
