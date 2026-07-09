import os
base = r"E:\公司-张皓茗\项目\高露洁\扬州\扬州高露洁Koch机初版"

def sr(c, o, n):
    if o in c: return c.replace(o, n, 1)
    print("  NOT FOUND: " + o[:60])
    return c

# ===== EndFace =====
print("1. EndFace...")
p = os.path.join(base, "VisionMeasure/Stations/EndFaceStationProcessor.cs")
c = open(p, encoding="utf-8").read()

# Add error log helper + consecutive tracking fields before constructor
w = """private static readonly string _efp = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "EndFace_Error.log");
        private static void WEF(string m) { try { var d = System.IO.Path.GetDirectoryName(_efp); if (!System.IO.Directory.Exists(d)) System.IO.Directory.CreateDirectory(d); System.IO.File.AppendAllText(_efp, m + Environment.NewLine, System.Text.Encoding.UTF8); } catch { } }
        private int _cmb = 0; private const int Mcm = 3;

        """
ci = c.find("public EndFaceStationProcessor(AiModelManager")
c = c[:ci] + w + c[ci:]

# P*3 -> P*30
c = sr(c, "if (productId - _firstBatchProductId > _pCount * 3)", "if (productId - _firstBatchProductId > _pCount * 30)")

# Debug throttle
c = sr(c, "Logger.Debug($", "if (count <= 2 || count >= _pCount - 1) Logger.Debug($")

# Replace missing Warning with tracked version
old = "if (missingCount > 0)"
idx = c.find(old, c.find("mergedStatus.Add"))
if idx > 0:
    end = c.find(";", idx + 30)
    new_block = """if (missingCount > 0)
                    {
                        _cmb++;
                        Logger.Warning("[EndFace] missing="+missingCount+" cons="+_cmb);
                        if (_cmb >= Mcm)
                        {
                            string al = "["+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+"] EndFace ALARM: cons="+_cmb+" miss="+missingCount;
                            Logger.Error(al); WEF(al);
                        }
                    }
                    else { _cmb = 0; }"""
    c = c[:idx] + new_block + c[end+1:]

open(p, "w", encoding="utf-8").write(c)
print("  EndFace: braces="+str(c.count("{")==c.count("}")))

# ===== Side =====
print("2. Side...")
p = os.path.join(base, "VisionMeasure/Stations/SideStationProcessor.cs")
c = open(p, encoding="utf-8").read()

# Add error log helper + re-entry guard + consecutive tracking fields after _cycleId
w2 = """private long _cycleId;
        private volatile int _fip = 0;
        private int _cmc = 0; private const int Mcm3 = 3;
        private static readonly string _sep = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "Side_Error.log");
        private static void WSE(string m) { try { var d = System.IO.Path.GetDirectoryName(_sep); if (!System.IO.Directory.Exists(d)) System.IO.Directory.CreateDirectory(d); System.IO.File.AppendAllText(_sep, m + Environment.NewLine, System.Text.Encoding.UTF8); } catch { } }"""
c = sr(c, "private long _cycleId;", w2)

# Add re-entry guard at start of FinalizeResults
c = sr(c,
    "private void FinalizeResults()
        {
            int expectedP = _sku.P;",
    "private void FinalizeResults()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _fip, 1, 0) != 0) { Logger.Warning("[Side] FinalizeResults re-entry blocked"); return; }
            try {
            int expectedP = _sku.P;")

# Add consecutive tracking before the Logger.Info complete line
c = sr(c,
    "Logger.Info($"[Side] 完成 P={p} OK={mergedStatus.Count(s => s == "OK")} NG={mergedStatus.Count(s => s != "OK")}{defStr2}");",
    "int tms = mergedStatus.Count(s => s == "缺少"); if (tms > 0) { _cmc++; Logger.Warning("[Side] missing="+tms+" cons="+_cmc); if (_cmc >= Mcm3) { string al = "["+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+"] Side ALARM: cons="+_cmc+" miss="+tms; Logger.Error(al); WSE(al); } } else { _cmc = 0; }
                Logger.Info($"[Side] 完成 P={p} OK={mergedStatus.Count(s => s == "OK")} NG={mergedStatus.Count(s => s != "OK")}{defStr2}");")

# Task.Run -> Thread in FinalizeResults stage2
c = sr(c, "Task.Run(() =>
                {
                    try
                    {
                        BuildDisplayImages(savedLeftImgs, savedRightImgs, p);",
    "new System.Threading.Thread(() => { try { BuildDisplayImages(savedLeftImgs, savedRightImgs, p);")

c = sr(c, "SaveImages(savedLeftImgs, savedRightImgs, savedMerged, savedIsOk, savedLeftRes, savedRightRes);
                    }
                    catch (Exception ex) { Logger.Error("[Side] 后台渲染异常: " + ex.Message); }
                });",
    "SaveImages(savedLeftImgs, savedRightImgs, savedMerged, savedIsOk, savedLeftRes, savedRightRes); } catch (Exception ex) { Logger.Error("[Side] render: "+ex.Message+"
"+ex.StackTrace); } }) { Name="SideRender", IsBackground=true, Priority=System.Threading.ThreadPriority.BelowNormal }.Start();")

# Add finally block before ProcessResults
c = sr(c, "OnStatusUpdate?.Invoke(new List<string>(), new List<string>(), mergedStatus, p);

                // 阶段2",
    "OnStatusUpdate?.Invoke(new List<string>(), new List<string>(), mergedStatus, p);
                } finally { System.Threading.Interlocked.Exchange(ref _fip, 0); }
                // 阶段2")

open(p, "w", encoding="utf-8").write(c)
print("  Side: braces="+str(c.count("{")==c.count("}")))

# ===== Front =====
print("3. Front...")
p = os.path.join(base, "VisionMeasure/Stations/FrontStationProcessor.cs")
c = open(p, encoding="utf-8").read()
w3 = """private static readonly string _frp = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "Front_Error.log");
        private static void WFR(string m) { try { var d = System.IO.Path.GetDirectoryName(_frp); if (!System.IO.Directory.Exists(d)) System.IO.Directory.CreateDirectory(d); System.IO.File.AppendAllText(_frp, m + Environment.NewLine, System.Text.Encoding.UTF8); } catch { } }

        """
ci = c.find("public FrontStationProcessor(AiModelManager")
c = c[:ci] + w3 + c[ci:]
open(p, "w", encoding="utf-8").write(c)
print("  Front: braces="+str(c.count("{")==c.count("}")))

# ===== Back =====
print("4. Back...")
p = os.path.join(base, "VisionMeasure/Stations/BackStationProcessor.cs")
c = open(p, encoding="utf-8").read()
w4 = """private static readonly string _bkp = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "Back_Error.log");
        private static void WBK(string m) { try { var d = System.IO.Path.GetDirectoryName(_bkp); if (!System.IO.Directory.Exists(d)) System.IO.Directory.CreateDirectory(d); System.IO.File.AppendAllText(_bkp, m + Environment.NewLine, System.Text.Encoding.UTF8); } catch { } }

        """
ci = c.find("public BackStationProcessor(AiModelManager")
c = c[:ci] + w4 + c[ci:]
open(p, "w", encoding="utf-8").write(c)
print("  Back: braces="+str(c.count("{")==c.count("}")))

print("Done.")
