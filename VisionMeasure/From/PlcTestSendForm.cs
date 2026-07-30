using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Config;
using Hardware;

namespace VisionMeasure.From
{
    /// <summary>
    /// PLC测试发送窗口: 推理完成后弹出, 逐盒勾选剔除/不剔除, 点击发送写入PLC
    /// 用途: 不启动产线也能测试与PLC之间的DB47通讯
    /// </summary>
    public partial class PlcTestSendForm : Form
    {
        private readonly StationType _station;
        private readonly List<string> _statusList;
        private readonly PlcResultService _plcService;
        private readonly int _pCount;
        private readonly CheckBox[] _boxes;
        private readonly Label _lblStopLevel;
        private readonly NumericUpDown _nudStopLevel;

        public PlcTestSendForm(StationType station, List<string> statusList,
            PlcResultService plcService, int pCount, int defaultStopLevel = 0,
            ushort defaultRejectBits = 0)
        {
            _station = station;
            _plcService = plcService;
            _pCount = Math.Max(1, Math.Min(pCount, 16));

            // 确保 statusList 至少有 _pCount 个条目，不够的补 "OK"
            _statusList = new List<string>(statusList ?? new List<string>());
            while (_statusList.Count < _pCount)
                _statusList.Add("OK");

            int cols = 2;
            int rows = (_pCount + cols - 1) / cols;
            int formWidth = 540;
            int formHeight = 130 + rows * 32;
            Text = $"PLC测试发送 — {station} (P={_pCount})";
            Size = new Size(formWidth, formHeight);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(240, 242, 245);
            Font = new Font("微软雅黑", 9F);

            // 顶部: 推理结果摘要
            int rejectCount = 0;
            for (int b = 0; b < _pCount; b++)
                if ((defaultRejectBits & (1 << b)) != 0) rejectCount++;
            int ngCount = _statusList.Count(s => s != "OK");
            int okCount = _pCount - rejectCount;
            var lblSummary = new Label
            {
                Text = $"推理结果: P={_pCount}  缺陷={ngCount}盒  剔除={rejectCount}盒  停机={defaultStopLevel}",
                Location = new Point(16, 12),
                Size = new Size(formWidth - 40, 22),
                ForeColor = Color.FromArgb(38, 38, 38)
            };
            Controls.Add(lblSummary);

            // 逐盒勾选（双列布局）— 默认勾选=该位将被剔除(IsReject=true)
            int colW = (formWidth - 48) / cols;
            _boxes = new CheckBox[_pCount];
            for (int i = 0; i < _pCount; i++)
            {
                bool willReject = (defaultRejectBits & (1 << i)) != 0;
                bool hasDefect = _statusList[i] != "OK";
                string label = hasDefect
                    ? $"盒{i + 1}: {_statusList[i]}" + (willReject ? " [剔除]" : " [保留]")
                    : $"盒{i + 1}: OK";

                var cb = new CheckBox
                {
                    Text = label,
                    Checked = willReject,
                    Location = new Point(16 + (i % cols) * colW, 40 + (i / cols) * 32),
                    Size = new Size(colW - 16, 24),
                    ForeColor = willReject ? Color.FromArgb(231, 76, 60) : (hasDefect ? Color.FromArgb(245, 158, 11) : Color.FromArgb(39, 174, 96)),
                    Font = new Font("微软雅黑", 8.5F)
                };
                _boxes[i] = cb;
                Controls.Add(cb);
            }

            int rowBase = 40 + rows * 32 + 10;

            // 停机标识
            var lblStop = new Label
            {
                Text = "停机标识(0-4):",
                Location = new Point(16, rowBase),
                Size = new Size(100, 25),
                TextAlign = ContentAlignment.MiddleRight
            };
            Controls.Add(lblStop);

            _nudStopLevel = new NumericUpDown
            {
                Minimum = 0, Maximum = 4, Value = Math.Max(0, Math.Min(4, defaultStopLevel)),
                Location = new Point(120, rowBase),
                Size = new Size(50, 25)
            };
            Controls.Add(_nudStopLevel);

            // 发送按钮
            var btnSend = new Button
            {
                Text = "发送 PLC",
                Location = new Point(200, rowBase),
                Size = new Size(90, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(24, 144, 255),
                ForeColor = Color.White
            };
            btnSend.Click += BtnSend_Click;
            Controls.Add(btnSend);

            // 取消
            var btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(300, rowBase),
                Size = new Size(80, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 200, 200),
                ForeColor = Color.FromArgb(38, 38, 38)
            };
            btnCancel.Click += (s, e) => Close();
            Controls.Add(btnCancel);
        }

        private void BtnSend_Click(object sender, EventArgs e)
        {
            if (_plcService == null)
            {
                MessageBox.Show("PlcResultService 未初始化(模拟模式)", "无法发送",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 构建剔除位
            ushort rejectBits = 0;
            for (int i = 0; i < _pCount; i++)
            {
                if (_boxes[i].Checked)
                    rejectBits |= (ushort)(1 << i);
            }
            int stopLevel = (int)_nudStopLevel.Value;

            // 标记: 手动测试
            CommonLib.Logger.Info($"【手动测试】========================================");
            CommonLib.Logger.Info($"【手动测试】{_station} 工位 P={_pCount}");
            var ngBoxes = new System.Collections.Generic.List<string>();
            for (int b = 0; b < _pCount; b++)
                if ((rejectBits & (1 << b)) != 0) ngBoxes.Add((b + 1).ToString());
            string ngDesc = ngBoxes.Count > 0 ? $"盒{string.Join(",", ngBoxes)}" : "无";
            CommonLib.Logger.Info($"【手动测试】剔除位: 0x{rejectBits:X4} → {ngDesc} (bit0=盒1, bit{_pCount-1}=盒{_pCount})");
            CommonLib.Logger.Info($"【手动测试】停机标识: {stopLevel}");
            CommonLib.PlcLogger.Info($"【手动测试】{_station} → DB47 写入开始");

            var overridden = new List<string>();
            for (int i = 0; i < _pCount; i++)
            {
                bool wasNg = i < _statusList.Count && _statusList[i] != "OK";
                if (_boxes[i].Checked != wasNg)
                    overridden.Add($"盒{i + 1}:{(_statusList.Count > i ? _statusList[i] : "?")}→{(_boxes[i].Checked ? "剔除" : "OK")}");
            }
            if (overridden.Count > 0)
                CommonLib.Logger.Info($"【手动测试】覆盖项: {string.Join(", ", overridden)}");

            // 发送
            bool ok1 = _plcService.SendStationResult(_station, rejectBits, stopLevel, _pCount);
            bool ok2 = _plcService.SendStationComplete(_station);

            CommonLib.PlcLogger.Info($"【手动测试】{_station} 发送完成: ok1={ok1} ok2={ok2}");
            CommonLib.Logger.Info($"【手动测试】发送结果: {(ok1 && ok2 ? "成功" : "失败")}");
            CommonLib.Logger.Info($"【手动测试】========================================");

            string msg = $"已发送 {_station}:\n"
                + $"剔除: {ngDesc}\n"
                + $"停机标识: {stopLevel}\n"
                + $"结果: {(ok1 ? "剔除/停机✓" : "剔除/停机✗")}  {(ok2 ? "完成信号✓" : "完成信号✗")}";

            MessageBox.Show(msg, "PLC 发送结果", MessageBoxButtons.OK,
                ok1 && ok2 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            if (ok1 && ok2) Close();
        }
    }
}
