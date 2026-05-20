using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMeasure.Class
{


	/// <summary>
	/// 结果追踪记录（用于验证数据是否正确）
	/// </summary>
	public class ResultTraceRecord
	{
		public DateTime Timestamp { get; set; }
		public long BatchId { get; set; }           // 批次ID（三个工件一批）
		public long SequenceId { get; set; }        // 相机原始序列号
		public int Offset { get; set; }             // 偏移量
		public long ProductId { get; set; }         // 实际产品ID（SequenceId - Offset）
		public int StationIndex { get; set; }       // 工位索引（0-4）
		public string CameraName { get; set; }      // 相机名称
		public bool Result { get; set; }            // 检测结果
		public DateTime ProcessStartTime { get; set; }
		public DateTime ProcessEndTime { get; set; }
		public long ProcessingTimeMs { get; set; }

		public override string ToString()
		{
			return $"[{Timestamp:HH:mm:ss.fff}] 相机={CameraName}, 批次ID={BatchId}, 产品ID={ProductId}, " +
				   $"原始ID={SequenceId}, Offset={Offset}, 结果={(Result ? "OK" : "NG")}, " +
				   $"耗时={ProcessingTimeMs}ms";
		}
	}

	/// <summary>
	/// 批次结果记录（三个工件一组）
	/// </summary>
	public class BatchResultRecord
	{
		public DateTime Timestamp { get; set; }
		public long BatchId { get; set; }
		public long[] ProductIds { get; set; } = new long[3];  // 三个产品的ID
		public bool[] Results { get; set; } = new bool[3];     // 三个产品的结果
		public bool FinalResult { get; set; }                  // 最终结果（AND逻辑）
		public string MatchStatus { get; set; }                // 匹配状态：Success/Missing/OutOfOrder
		public string MissingCameras { get; set; }             // 缺失的相机
		public long MatchTimeMs { get; set; }                  // 匹配耗时

		public override string ToString()
		{
			return $"[{Timestamp:HH:mm:ss.fff}] 批次={BatchId}, 产品ID=[{string.Join(",", ProductIds)}], " +
				   $"结果=[{string.Join(",", Results.Select(r => r ? "OK" : "NG"))}], " +
				   $"最终={(FinalResult ? "OK" : "NG")}, 状态={MatchStatus}";
		}
	}
}
