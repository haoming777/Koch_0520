using cszmcaux;
using Littleluck.Class;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using XL.Tool;
using static CommonLib.Class_Config;
using static cszmcaux.zmcaux;

namespace CommonLib
{
	public class takephotoVm
	{
		XLToolClass zhm = new XLToolClass();
		ControlParms parms = new ControlParms();
		IntPtr handle;


		ControlParms AxisParms1 = new ControlParms()
		{
			Units = 2500,
			Speed = 1,
			Accel = 10000,
			Decel = 10000,
			Sramp = 0,
			Lspeed = 0
		};

		ControlParms AxisParms_Init = new ControlParms()
		{
			Units = 2500,
			Speed = 1,
			Accel = 10000,
			Decel = 10000,
			Sramp = 0,
			Lspeed = 0
		};

		ControlParms AxisParms_Temp = new ControlParms()
		{
			Units = 2500,
			Speed = 1,
			Accel = 10000,
			Decel = 10000,
			Sramp = 0,
			Lspeed = 0
		};

		/// <summary>
		/// 连接运动控制卡
		/// </summary>
		/// <param name="g_handle"></param>
		/// <param name="ip"></param>
		/// <returns></returns>
		public bool Connect(ref IntPtr g_handle, string ip)
		{

			try
			{
				handle = g_handle;

				if (g_handle == (IntPtr)0)
				{
					//ZAux_SearchEthlist();
					int res = ZAux_FastOpen(2, ip, 1000, out g_handle);//连接ETH网口类型的

					if (res != 0)
					{
						zhm.SaveLog($"控制器连接失败 错误码：{res}");
						return false;
					}
				}
				return true;
			}
			catch (Exception ex)
			{
				zhm.SaveLog($"连接运控卡出现异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
				return false;
			}


		}
		/// <summary>
		/// 关闭控制器连接
		/// </summary>
		/// <param name="g_handle"></param>
		/// <returns>状态码</returns>
		public int CloseConnect(IntPtr g_handle)
		{
			int res = 0;
			res = ZAux_Close(g_handle);
			g_handle = (IntPtr)0;
			return res;
		}
		/// <summary>
		/// 轴初始化函数
		/// </summary>
		/// <param name="g_handle"></param>
		public void Init(IntPtr g_handle)
		{
			if (g_handle == (IntPtr)0)
				return;

			ControlParms temp0 = new ControlParms()
			{
				Units = Convert.ToSingle(_Config.axis0_Units),
				Speed = Convert.ToSingle(_Config.axis0_Speed),
				Accel = Convert.ToSingle(_Config.axis0_Accel),
				Decel = Convert.ToSingle(_Config.axis0_Decel),
				Sramp = Convert.ToSingle(_Config.axis0_Sramp),
				Lspeed = Convert.ToSingle(_Config.axis0_Lspeed)
			};

			SetParse(g_handle, 0, temp0);//现在初始化X轴

			ControlParms temp1 = new ControlParms()
			{
				Units = Convert.ToSingle(_Config.axis1_Units),
				Speed = Convert.ToSingle(_Config.axis1_Speed),
				Accel = Convert.ToSingle(_Config.axis1_Accel),
				Decel = Convert.ToSingle(_Config.axis1_Decel),
				Sramp = Convert.ToSingle(_Config.axis1_Sramp),
				Lspeed = Convert.ToSingle(_Config.axis1_Lspeed)
			};

			SetParse(g_handle, 1, temp1);//现在初始化X轴



			ControlParms temp2 = new ControlParms()
			{
				Units = Convert.ToSingle(_Config.axis2_Units),
				Speed = Convert.ToSingle(_Config.axis2_Speed),
				Accel = Convert.ToSingle(_Config.axis2_Accel),
				Decel = Convert.ToSingle(_Config.axis2_Decel),
				Sramp = Convert.ToSingle(_Config.axis2_Sramp),
				Lspeed = Convert.ToSingle(_Config.axis2_Lspeed)
			};

			SetParse(g_handle, 2, temp2);//现在初始化X轴

			AxisParms_Init = new ControlParms()
			{
				Units = Convert.ToSingle(_Config.axis_Units_Init),
				Speed = Convert.ToSingle(_Config.axis_Speed_Init),
				Accel = Convert.ToSingle(_Config.axis_Accel_Init),
				Decel = Convert.ToSingle(_Config.axis_Decel_Init),
				Sramp = Convert.ToSingle(_Config.axis_Sramp_Init),
				Lspeed = Convert.ToSingle(_Config.axis_Lspeed_Init)
			};



			//if (IFInitMethod(g_handle))
			//{
			//	if (IFAutoMethod(g_handle))
			//	{
			//		SetGreenLight(g_handle);
			//	}
			//	else
			//	{
			//		SetYellowLight(g_handle);
			//	}
			//}
			//else
			//{
			//	SetRedLight(g_handle,false);
			//}
		}

		/// <summary>
		/// 设置轴参数
		/// </summary>
		/// <param name="g_handle"></param>
		/// <param name="axis"></param>
		/// <param name="parms"></param>
		/// <returns></returns>
		public int SetParse(IntPtr g_handle, int axis, ControlParms parms)
		{
			if (g_handle == (IntPtr)0)
				return -1;
			int DatumIn = _Config.axis0_Datum;
			int FwdIn = _Config.axis0_Fwd;
			int RevIn = _Config.axis0_Rev;

			int res = ZAux_Direct_SetUnits(g_handle, axis, Convert.ToSingle(parms.Units));
			res = ZAux_Direct_SetSpeed(g_handle, axis, Convert.ToSingle(parms.Speed));
			res = ZAux_Direct_SetAccel(g_handle, axis, Convert.ToSingle(parms.Accel));
			res = ZAux_Direct_SetDecel(g_handle, axis, Convert.ToSingle(parms.Decel));
			res = ZAux_Direct_SetSramp(g_handle, axis, Convert.ToSingle(parms.Sramp));
			res = ZAux_Direct_SetLspeed(g_handle, axis, Convert.ToSingle(parms.Lspeed));
			res = ZAux_Direct_SetAtype(g_handle, axis, 1);

			switch (axis)
			{
				case 0:
					DatumIn = _Config.axis0_Datum;
					FwdIn = _Config.axis0_Fwd;
					RevIn = _Config.axis0_Rev;

					break;
				case 1:
					DatumIn = _Config.axis1_Datum;
					FwdIn = _Config.axis1_Fwd;
					RevIn = _Config.axis1_Rev;
					break;
				case 2:
					DatumIn = _Config.axis2_Datum;
					FwdIn = _Config.axis2_Fwd;
					RevIn = _Config.axis2_Rev;
					break;
				default:
					break;
			}
			res += ZAux_Direct_SetDatumIn(g_handle, axis, DatumIn);
			res += ZAux_Direct_SetFwdIn(g_handle, axis, FwdIn);
			res += ZAux_Direct_SetRevIn(g_handle, axis, RevIn);
			return res;
		}

		/// <summary>
		/// 获取轴参数
		/// </summary>
		/// <param name="axis">轴号</param>
		/// <returns>返回 ControlParms类型的参数集合</returns>
		public ControlParms GetParms(IntPtr g_handle, int axis, out int res)
		{
			if (g_handle == (IntPtr)0)
			{

				res = 2;
				return null;
			}

			// MessageBox.Show($"获取参数时的句柄 {g_handle}");
			ControlParms parms = new ControlParms();
			res = ZAux_Direct_GetUnits(g_handle, axis, ref parms.Units);
			res += ZAux_Direct_GetSpeed(g_handle, axis, ref parms.Speed);
			res += ZAux_Direct_GetAccel(g_handle, axis, ref parms.Accel);
			res += ZAux_Direct_GetDecel(g_handle, axis, ref parms.Decel);
			res += ZAux_Direct_GetSramp(g_handle, axis, ref parms.Sramp);
			res += ZAux_Direct_GetLspeed(g_handle, axis, ref parms.Lspeed);

			return parms;
		}


		/// <summary>
		/// 轴连续运动
		/// </summary>
		/// <param name="g_handle"></param>
		/// <param name="axis">轴号</param>
		/// <param name="fangxiang">方向  正:1   负:-1</param>
		/// <returns></returns>
		public bool Vmove(IntPtr g_handle, int axis, int fangxiang)
		{
			if (g_handle == (IntPtr)0)
			{
				zhm.SaveLog($"g_handle: {g_handle}");
				return false;
			}
			//if (IFAutoMethod(g_handle) || IFStopMethod(g_handle))
			//{
			//	zhm.SaveLog($"IFAuto: {IFAutoMethod(g_handle)}");
			//	zhm.SaveLog($"IFStop: {IFStopMethod(g_handle)}");
			//	return false;
			//}
			if (axis == 0)
			{
				SetOut(g_handle, 6, 1);

			}
			else if (axis == 1)
			{
				SetOut(g_handle, 7, 1);
			}
			else if (axis == 2)
			{
				SetOut(g_handle, 8, 1);
			}
			Thread.Sleep(100);
			int res = -1;
			try
			{
				res = ZAux_Direct_SetAtype(g_handle, axis, 1);//设置轴类型是什么玩意？？？应该只设置一次就可以了
				res += ZAux_Direct_Single_Vmove(g_handle, axis, fangxiang);
			}
			catch (Exception ex)
			{
				zhm.SaveLog("轴连续运动出错！");
				res = 1;

				Thread.Sleep(100);
				if (axis == 0)
				{
					SetOut(g_handle, 6, 0);
				}
				else if (axis == 1)
				{
					SetOut(g_handle, 7, 0);
				}
				else if (axis == 2)
				{
					SetOut(g_handle, 8, 0);
				}
			}
			return res == 0 ? true : false;
		}

		/// <summary>
		/// 轴寸动运动，相对运动
		/// </summary>
		/// <param name="g_handle"></param>
		/// <param name="axis">轴号</param>
		/// <param name="distance">距离  大于0:正方向   小于0:负方向</param>
		/// <returns></returns>
		public bool Move(IntPtr g_handle, int axis, float distance)
		{
			if (g_handle == (IntPtr)0)
				return false;

			int res = -1;
			try
			{
				res = ZAux_Direct_SetAtype(g_handle, axis, 1);
				res += ZAux_Direct_Single_Move(g_handle, axis, distance);

			}
			catch (Exception ex)
			{
				zhm.SaveLog("ERR:轴寸动运动出错！");
				res = 1;
			}
			return res == 0 ? true : false;
		}
		/// <summary>
		/// 绝对位置运动
		/// </summary>
		/// <param name="g_handle">运动卡句柄</param>
		/// <param name="axis">轴</param>
		/// <param name="postion">要移动到的位置</param>
		public void MoveAbs(IntPtr g_handle, int axis, float postion)
		{
			if (g_handle == (IntPtr)0)
				return;

			if (postion == GetLocation(g_handle, 0))
				return;

			int res = -1;
			res += ZAux_Direct_Single_MoveAbs(g_handle, (int)axis, postion);
		}

		/// <summary>
		/// 定点移动
		/// </summary>
		/// <param name="g_handle"></param>
		/// <param name="Position"></param>
		public void GoPosition(IntPtr g_handle, int axis, float Position)
		{
			//Task.Run(() => {
			try
			{
				if (g_handle == (IntPtr)0)
					return;
				//if (IFAutoMethod(g_handle) || IFStopMethod(g_handle))
				//{
				//	zhm.SaveLog($"IFAuto: {IFAutoMethod(g_handle)}");
				//	zhm.SaveLog($"IFStop: {IFStopMethod(g_handle)}");
				//	return;
				//}

				GoP:
				if (axis == 0)
				{
					SetOut(g_handle, 6, 1);

				}
				else if (axis == 1)
				{
					SetOut(g_handle, 7, 1);
				}
				else if (axis == 2)
				{
					SetOut(g_handle, 8, 1);
				}
				MoveAbs(g_handle, axis, Position);
				while (GetAxisIdle(g_handle, axis) == 0)
				{
					if (GetModbusValue(g_handle, 1) == 1 || GetModbusValue(g_handle, 4) == 1)
					{
						StopMove(g_handle, axis);
						return;
					}
					Thread.Sleep(5);
				}
				//if (GetLocation(g_handle, 0) != Position)
				//{
				//	goto GoP;
				//}
				if (IFInMotionsMethod(g_handle, axis))
				{
					goto GoP;
				}

				if (axis == 0)
				{
					SetOut(g_handle, 6, 0);

				}
				else if (axis == 1)
				{
					SetOut(g_handle, 7, 0);
				}
				else if (axis == 2)
				{
					SetOut(g_handle, 8, 0);
				}
			}
			catch (Exception ex)
			{
				zhm.SaveLog($"自动流程出现异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
				if (axis == 0)
				{
					SetOut(g_handle, 6, 0);

				}
				else if (axis == 1)
				{
					SetOut(g_handle, 7, 0);
				}
				else if (axis == 2)
				{
					SetOut(g_handle, 8, 0);
				}
			}
			//});

		}

		/// <summary>
		/// 定点移动
		/// </summary>
		/// <param name="g_handle"></param>
		/// <param name="Position"></param>
		public bool GoPosition_Auto(IntPtr g_handle, int axis, float Position)
		{
			try
			{
				if (g_handle == (IntPtr)0)
					return false;
				//if (!IFAutoMethod(g_handle) || IFStopMethod(g_handle))
				//{
				//	zhm.SaveLog($"IFAuto: {IFAutoMethod(g_handle)}");
				//	zhm.SaveLog($"IFStop: {IFStopMethod(g_handle)}");
				//	return false;
				//}

				GoP:
				MoveAbs(g_handle, axis, Position);
				while (GetAxisIdle(g_handle, axis) == 0)
				{
					if (GetModbusValue(g_handle, 1) == 1 || GetModbusValue(g_handle, 4) == 0)
					{
						StopMove(g_handle, axis);
						return false;
					}
					Thread.Sleep(5);
				}
				//if (GetLocation(g_handle, 0) != Position)
				//{
				//	goto GoP;
				//}
				if (IFInMotionsMethod(g_handle, axis))
				{
					goto GoP;
				}
				return true;

			}
			catch (Exception ex)
			{
				zhm.SaveLog($"自动流程出现异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
				return false;
			}
		}


		/// <summary>
		/// 急停
		/// </summary>
		/// <param name="g_handle"></param>
		/// <param name="axis"></param>
		/// <returns></returns>
		public bool StopMethod(IntPtr g_handle, int axis)
		{
			try
			{
				SetRedLight(g_handle, true);
				StopMove(g_handle, axis);
				return true;
			}
			catch (Exception ex)
			{
				zhm.SaveLog($"自动流程出现异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
				return false;
			}

		}

		/// <summary>
		/// 轴停止运动
		/// </summary>
		/// <param name="g_handle"></param>
		/// <param name="axis"></param>
		/// <returns></returns>
		public bool StopMove(IntPtr g_handle, int axis)
		{
			if (g_handle == (IntPtr)0)
				return false;

			bool res = 0 == ZAux_Direct_Single_Cancel(g_handle, axis, 2);

			Thread.Sleep(100);
			if (axis == 0)
			{
				SetOut(g_handle, 6, 0);

			}
			else if (axis == 1)
			{
				SetOut(g_handle, 7, 0);
			}
			else if (axis == 2)
			{
				SetOut(g_handle, 8, 0);
			}
			return res;
		}
		/// <summary>
		/// 获取坐标
		/// </summary>
		/// <param name="g_handle"></param>
		/// <param name="axis">轴号</param>
		/// <returns></returns>
		public float GetLocation(IntPtr g_handle, int axis)
		{
			try
			{

				if (g_handle == (IntPtr)0)
					return -1;

				float mingLocation = 0;
				int res = ZAux_Direct_GetDpos(g_handle, axis, ref mingLocation);
				return mingLocation;

			}
			catch (Exception ex)
			{

				zhm.SaveLog($"自动流程出现异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
				return -99999;
			}

		}
		/// <summary>
		/// 找到可以连接得控制器
		/// </summary>
		/// <param name="stringArr">返回所有得ip</param>
		/// <returns></returns>
		public bool IP_SCAN(ref string[] stringArr)
		{
			StringBuilder buffer = new StringBuilder(10240);
			string buff = "";
			int res = ZAux_SearchEthlist(buffer, 10230, 200);
			buff += buffer;
			stringArr = buff.Split(' ');
			return res == 0;
		}

		/// <summary>
		/// 轴回零5
		/// </summary>
		/// <param name="g_handle"></param>
		/// <param name="axis">轴号</param>
		/// <param name="DatumIn">原点</param>
		/// <param name="FwdIn">正限位</param>
		/// <param name="RevIn">负限位</param>
		/// <returns></returns>
		public int GoHome(IntPtr g_handle, int axis, int DatumIn, int FwdIn, int RevIn, float CreepSpeed)
		{
			if (g_handle == (IntPtr)0)
				return -1;
			//if (IFAutoMethod(g_handle) || IFStopMethod(g_handle))
			//	return -1;

			StopMove(g_handle, axis);

			int res = 0;

			AxisParms_Temp = GetParms(g_handle, 0, out res);        // 先将初始化前的参数保存下来，初始化后重改回该参数

			AxisParms_Init = new ControlParms()
			{
				Units = Convert.ToSingle(_Config.axis_Units_Init),
				Speed = Convert.ToSingle(_Config.axis_Speed_Init),
				Accel = Convert.ToSingle(_Config.axis_Accel_Init),
				Decel = Convert.ToSingle(_Config.axis_Decel_Init),
				Sramp = Convert.ToSingle(_Config.axis_Sramp_Init),
				Lspeed = Convert.ToSingle(_Config.axis_Lspeed_Init)
			};
			SetParse(g_handle, axis, AxisParms_Init);


			res += ZAux_Direct_SetDatumIn(g_handle, axis, DatumIn);
			res += ZAux_Direct_SetFwdIn(g_handle, axis, FwdIn);
			res += ZAux_Direct_SetRevIn(g_handle, axis, RevIn);
			res += ZAux_Direct_SetCreep(g_handle, axis, CreepSpeed);

			if (axis == 0)
			{
				SetOut(g_handle, 6, 1);

			}
			else if (axis == 1)
			{
				SetOut(g_handle, 7, 1);
			}
			else if (axis == 2)
			{
				SetOut(g_handle, 8, 1);
			}
			Thread.Sleep(100);
			res += ZAux_Direct_Single_Datum(g_handle, axis, 14);

			while (IFInMotionsMethod(g_handle, axis))
			{


			}
			if (res == 0)
			{
				SetParse(g_handle, axis, AxisParms_Temp);
			}
			else
			{
				zhm.SaveLog("回零执行失败！");
			}
			Thread.Sleep(100);
			if (axis == 0)
			{
				SetOut(g_handle, 6, 0);

			}
			else if (axis == 1)
			{
				SetOut(g_handle, 7, 0);
			}
			else if (axis == 2)
			{
				SetOut(g_handle, 8, 0);
			}
			return res;
		}



		/// <summary>
		/// 获取轴状态
		/// </summary>
		/// <param name="g_handle"></param>
		/// <param name="axis">轴号</param>
		/// <returns>0：运动中  1：未运动</returns>
		public int GetAxisIdle(IntPtr g_handle, int axis)
		{
			if (g_handle == (IntPtr)0)
				return -1;

			int result = -1;
			ZAux_Direct_GetIfIdle(g_handle, (int)axis, ref result);
			return result;
		}

		/// <summary>
		/// 0 初始化		（1：初始化完成	0：未初始化）
		/// 1 急停		（1：急停按下		0：未初始化）
		/// 2 回零
		/// 3 暂停
		/// 4 手/自动		（1：自动			0：手动)
		/// </summary>
		public void SetModbusValue(IntPtr g_handle, int addr, int value)
		{
			if (g_handle == (IntPtr)0)
				return;

			ushort[] arr = { 0 };
			arr[0] = (ushort)value;
			ZAux_Modbus_Set4x(g_handle, (ushort)addr, 1, arr);
		}
		/// <summary>
		/// 0 初始化		（1：初始化完成	0：未初始化）
		/// 1 急停		（1：急停按下		0：未初始化）
		/// 2 回零
		/// 3 暂停
		/// 4 手/自动		（1：自动			0：手动)
		/// </summary>
		public int GetModbusValue(IntPtr g_handle, int addr)
		{
			if (g_handle == (IntPtr)0)
				return -1;

			ushort[] arr = { 0 };
			ZAux_Modbus_Get4x(g_handle, (ushort)addr, 1, arr);
			return arr[0];
		}
		/// <summary>
		/// 开启绿灯
		/// </summary>
		/// <param name="g_handle"></param>
		/// <returns></returns>
		public int SetGreenLight(IntPtr g_handle)
		{
			if (g_handle == (IntPtr)0)
				return -1;

			int res = ZAux_Direct_SetOp(g_handle, 0, 0);
			res += ZAux_Direct_SetOp(g_handle, 1, 0);
			res += ZAux_Direct_SetOp(g_handle, 2, 1);
			ZAux_Direct_SetOp(g_handle, 3, 0);

			return res;
		}

		public bool GoHomePlus(IntPtr intPtr)
		{

			try
			{
				if (intPtr == (IntPtr)0)
					return false;

				SetModbusValue(intPtr, 0, 0); //将 初始化状态重置
				int datum0 = _Config.axis0_Datum;
				int fwd0 = _Config.axis0_Fwd;
				int rev0 = _Config.axis0_Rev;

				int datum1 = _Config.axis1_Datum;
				int fwd1 = _Config.axis1_Fwd;
				int rev1 = _Config.axis1_Rev;

				int datum2 = _Config.axis2_Datum;
				int fwd2 = _Config.axis2_Fwd;
				int rev2 = _Config.axis2_Rev;

				StopMove(intPtr, 0);
				StopMove(intPtr, 1);
				StopMove(intPtr, 2);



				zhm.SaveLog($"\r\n轴1开始初始化,\r\naxis1_Datum: {datum1},\r\naxis1_Fwd: {fwd1},\r\naxis1_Rev: {rev1}");
				int res = GoHome(intPtr, 1, datum1, fwd1, rev1, 1);
				if (res == 0)
				{
					zhm.SaveLog("轴1初始化完成");
				}
				else
				{
					zhm.SaveLog($"轴1初始化失败：res：{res}");
					return false;
				}

				zhm.SaveLog($"\r\n轴0开始初始化,\r\naxis0_Datum: {datum0},\r\naxis0_Fwd: {fwd0},\r\axis0_Rev: {rev0}");
				res += GoHome(intPtr, 0, datum0, fwd0, rev0, 1);
				if (res == 0)
				{
					zhm.SaveLog("轴0初始化完成");
				}
				else
				{
					zhm.SaveLog($"轴0初始化失败：res：{res}");
					return false;
				}

				zhm.SaveLog($"\r\n轴2开始初始化,\r\naxis2_Datum: {datum2},\r\naxis2_Fwd: {fwd2},\r\axis2_Rev: {rev2}");
				res += GoHome(intPtr, 2, datum2, fwd2, rev2, 1);
				if (res == 0)
				{
					zhm.SaveLog("轴2初始化完成");
				}
				else
				{
					zhm.SaveLog($"轴2初始化失败：res：{res}");
					return false;
				}


				SetModbusValue(intPtr, 0, 1);
				zhm.SaveLog($"SetModbusValue(intPtr, 0, 1): {GetModbusValue(intPtr, 0)}");

				return true;
			}
			catch (Exception ex)
			{
				return false;
			}
		}


		/// <summary>
		/// 开启黄灯
		/// </summary>
		/// <param name="g_handle"></param>
		/// <returns></returns>
		public int SetYellowLight(IntPtr g_handle)
		{
			if (g_handle == (IntPtr)0)
				return -1;

			int res = ZAux_Direct_SetOp(g_handle, 0, 0);
			res += ZAux_Direct_SetOp(g_handle, 1, 1);
			res += ZAux_Direct_SetOp(g_handle, 2, 0);

			return res;
		}

		/// <summary>
		/// 开启红灯 （报警）
		/// </summary>
		/// <param name="g_handle"></param>
		/// <param name="mingBool">true: 开启蜂鸣器  false: 不开启蜂鸣器</param>
		/// <returns></returns>
		public int SetRedLight(IntPtr g_handle, bool mingBool)
		{
			if (g_handle == (IntPtr)0)
				return -1;

			int res = ZAux_Direct_SetOp(g_handle, 0, 1);
			res += ZAux_Direct_SetOp(g_handle, 1, 0);
			res += ZAux_Direct_SetOp(g_handle, 2, 0);

			if (mingBool)
			{
				ZAux_Direct_SetOp(g_handle, 3, 1);
			}

			return res;
		}

		/// <summary>
		/// 复位
		/// </summary>
		/// <param name="g_handle"></param>
		public void ResetAlarm(IntPtr g_handle)
		{
			if (g_handle == (IntPtr)0)
				return;

			if (IFInitMethod(g_handle))
			{
				if (IFAutoMethod(g_handle))
				{
					SetGreenLight(g_handle);
				}
				else
				{
					SetYellowLight(g_handle);
				}
			}
			else
			{
				SetRedLight(g_handle, false);
			}


			ZAux_Direct_SetOp(g_handle, 3, 0);
		}
		/// <summary>
		/// 扫码完成时蜂鸣器长鸣2秒
		/// </summary>
		/// <param name="g_handle"></param>
		public void ScanComplete(IntPtr g_handle)
		{
			if (g_handle == (IntPtr)0)
				return;

			ZAux_Direct_SetOp(g_handle, 3, 1);
			Thread.Sleep(2000);
			ZAux_Direct_SetOp(g_handle, 3, 0);
		}
		/// <summary>
		/// 检测完成后绿灯闪烁蜂鸣器交替蜂鸣
		/// </summary>
		/// <param name="g_handle"></param>
		public void TakePhotoComplete(IntPtr g_handle)
		{
			Task.Run(() =>
			{

				ZAux_Direct_SetOp(g_handle, 2, 1);//绿灯亮
				ZAux_Direct_SetOp(g_handle, 3, 1);//蜂鸣器响
				Thread.Sleep(500);
				ZAux_Direct_SetOp(g_handle, 2, 0);//绿灯灭
				ZAux_Direct_SetOp(g_handle, 3, 0);//蜂鸣器不响
				Thread.Sleep(500);

				ZAux_Direct_SetOp(g_handle, 2, 1);//绿灯亮
				ZAux_Direct_SetOp(g_handle, 3, 1);//蜂鸣器响
				Thread.Sleep(500);
				ZAux_Direct_SetOp(g_handle, 2, 0);//绿灯灭
				ZAux_Direct_SetOp(g_handle, 3, 0);//蜂鸣器不响
				Thread.Sleep(500);

				ZAux_Direct_SetOp(g_handle, 2, 1);//绿灯亮
				ZAux_Direct_SetOp(g_handle, 3, 1);//蜂鸣器响
				Thread.Sleep(500);
				ZAux_Direct_SetOp(g_handle, 3, 0);//蜂鸣器不响

			});
		}
		public void WarnMethod(IntPtr g_handle)
		{
			Task.Run(() =>
			{
				ZAux_Direct_SetOp(g_handle, 3, 1);//蜂鸣器响
				Thread.Sleep(500);
				ZAux_Direct_SetOp(g_handle, 3, 0);//蜂鸣器不响
			});

		}

		public void SetLightMethod(IntPtr g_handle)
		{

			try
			{
				if (g_handle == (IntPtr)0)
					return;

				if (IFInitMethod(g_handle))
				{
					if (IFAutoMethod(g_handle))
					{
						SetGreenLight(g_handle);
					}
					else
					{
						SetYellowLight(g_handle);
					}
				}
				else
				{
					SetRedLight(g_handle, false);
				}
			}
			catch (Exception ex)
			{
				zhm.SaveLog($"SetLightMethod异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}

		}

		/// <summary>
		/// 读输入信号
		/// </summary>
		/// <param name="handle"></param>
		/// <param name="num"></param>
		/// <param name="value"></param>
		/// <returns></returns>
		public int ReadIn(IntPtr handle, int num, uint value)
		{

			if (handle == (IntPtr)0)
			{
				return -1;
			}
			//uint value = 0;
			int res = -1;
			res = ZAux_Direct_GetIn(handle, num, ref value);
			return res;
		}
		/// <summary>
		/// 设置输出信号
		/// </summary>
		/// <param name="handle">io卡句柄</param>
		/// <param name="ionum">端口号</param>
		/// <param name="value">值：0无输出  1有输出</param>
		public bool SetOut(IntPtr handle, int ionum, uint value)
		{
			if (handle == (IntPtr)0)
			{
				zhm.SaveLog($"SetOut运控卡断连 Handle: {handle}");
				return false;
			}
			int res = ZAux_Direct_SetOp(handle, ionum, value);

			if (res == 0)
			{
				return true;
			}
			else
			{
				return false;
			}
		}

		/// <summary>
		/// 获取输出信号
		/// </summary>
		/// <param name="handle">io卡句柄</param>
		/// <param name="ionum">端口号</param>
		/// <param name="value">值：0无输出  1有输出</param>
		public bool GetOut(IntPtr handle, int ionum, ref uint value)
		{
			if (handle == (IntPtr)0)
			{
				zhm.SaveLog($"GetOut运控卡断连 Handle: {handle}");
				return false;
			}
			int res = ZAux_Direct_GetOp(handle, ionum, ref value);

			if (res == 0)
			{
				return true;
			}
			else
			{
				return false;
			}
		}

		/// <summary>
		/// 获取输出信号
		/// </summary>
		/// <param name="handle">io卡句柄</param>
		/// <param name="ionum">端口号</param>
		/// <param name="value">值：0无输出  1有输出</param>
		public bool GetIn(IntPtr handle, int ionum, ref uint value)
		{
			try
			{
				if (handle == (IntPtr)0)
				{
					zhm.SaveLog($"GetIn运控卡断连 Handle: {handle}");
					return false;
				}
				int res = ZAux_Direct_GetIn(handle, ionum, ref value);

				if (res == 0)
				{
					return true;
				}
				else
				{
					return false;
				}
			}
			catch (Exception ex)
			{
				zhm.SaveLog($"自动流程出现异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
				return false;
			}

		}

		/// <summary>
		/// 是否为自动模式
		/// </summary>
		/// <returns>true：自动	false：手动</returns>
		public bool IFAutoMethod(IntPtr g_handle)
		{
			try
			{
				uint value = 100;
				if (GetIn(g_handle, 2, ref value))
				{
					return (value == 1);
				}
				return false;

			}
			catch (Exception ex)
			{
				zhm.SaveLog("检查状态时发生异常..." + "\n\r" + ex.Message);
				return false;
			}
		}

		/// <summary>
		/// 是否为急停模式
		/// </summary>
		/// <returns>true：急停	false：非急停</returns>
		public bool IFStopMethod(IntPtr g_handle)
		{
			try
			{
				uint value = 100;
				if (GetIn(g_handle, 3, ref value))
				{
					return true;
					//return false;
					//return (value == 0);
				}
				return false;

			}
			catch (Exception ex)
			{
				zhm.SaveLog("检查状态时发生异常..." + "\n\r" + ex.Message);
				return false;
			}
		}

		/// <summary>
		/// 轴是否在运动
		/// </summary>
		/// <returns>true：运动中		false：停止状态</returns>
		public bool IFInMotionsMethod(IntPtr g_handle, int axis)
		{
			try
			{
				int value = -1;

				if (ZAux_Direct_GetIfIdle(g_handle, axis, ref value) == 0)
				{
					return (value == 0);
				}
				return false;

			}
			catch (Exception ex)
			{
				zhm.SaveLog("检查状态时发生异常..." + "\n\r" + ex.Message);
				return false;
			}
		}

		/// <summary>
		/// 是否初始化
		/// </summary>
		/// <returns>true：初始化完成	false：未初始化</returns>
		public bool IFInitMethod(IntPtr g_handle)
		{
			try
			{
				if (g_handle == (IntPtr)0)
					return false;


				int value = -1;
				value = GetModbusValue(g_handle, 0);
				return (value == 1);


			}
			catch (Exception ex)
			{
				zhm.SaveLog("检查状态时发生异常..." + "\n\r" + ex.Message);
				return false;
			}
		}
	}
}
