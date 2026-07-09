import os, re
base = r"E:\公司-张皓茗\项目\高露洁\扬州\扬州高露洁Koch机初版"
def sr(c,o,n): return c.replace(o,n,1) if o in c else c
def now(): return "DateTime.Now.ToString(\"yyyy-MM-dd HH:mm:ss\")"

# EndFaceStationProcessor
p = os.path.join(base, "VisionMeasure/Stations/EndFaceStationProcessor.cs")
with open(p, encoding="utf-8") as f: c = f.read()

c = sr(c, "if (productId - _firstBatchProductId > _pCount * 3)", "if (productId - _firstBatchProductId > _pCount * 30)")
c = sr(c, "while (_upperQueue.TryDequeue(out var _)) _upperCount--;", "int du=0; while (_upperQueue.TryDequeue(out var _)) { _upperCount--; du++; }")
c = sr(c, "while (_lowerQueue.TryDequeue(out var _)) _lowerCount--;", "int dl=0; while (_lowerQueue.TryDequeue(out var _)) { _lowerCount--; dl++; } if(du>0||dl>0) Logger.Warning(\"[EndFace] dropped U=\"+du+\" L=\"+dl);")
c = sr(c, "Logger.Debug($\"[EndFace] {name}入队 ProductId={productId}, Upper={_upperCount}/{_pCount}, Lower={_lowerCount}/{_pCount}\");", "if(count<=2||count>=_pCount-1) Logger.Debug(\"[EndFace] \"+name+\" enq pid=\"+productId+\" U=\"+_upperCount+\"/\"+_pCount+\" L=\"+_lowerCount+\"/\"+_pCount);")
c = sr(c, "queue.Enqueue(ctx);", "int ad=Math.Abs(_upperCount-_lowerCount); if(ad>2&&(_upperCount>0||_lowerCount>0)) Logger.Warning(\"[EndFace] asym U=\"+_upperCount+\" L=\"+_lowerCount+\" d=\"+ad); queue.Enqueue(ctx);")
idx = c.find("if (missingCount > 0)")
if idx > 0:
    # Insert error log writer fields before constructor
    ci = c.find("public EndFaceStationProcessor(")
    if ci > 0:
        w = "private static readonly string _efp = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, \"Logs\", \"EndFace_Error.log\"); private static void WEF(string m) { try { string d = System.IO.Path.GetDirectoryName(_efp); if (!System.IO.Directory.Exists(d)) System.IO.Directory.CreateDirectory(d); System.IO.File.AppendAllText(_efp, m + Environment.NewLine, System.Text.Encoding.UTF8); } catch { } } private int _cmb = 0; private const int Mcm = 3; "
        c = c[:ci] + w + c[ci:]
    # Replace missing warning with tracked version
    old = c[idx:c.find(";", idx)+1]
    ns = "if (missingCount > 0) { _cmb++; Logger.Warning(\"[EndFace] missing=\"+missingCount+\" cons=\"+_cmb); if (_cmb >= Mcm) { string al = \"[\"+DateTime.Now.ToString(\"yyyy-MM-dd HH:mm:ss\")+\"] EndFace ALARM: cons=\"+_cmb+\" miss=\"+missingCount+\" P=\"+_pCount; Logger.Error(al); WEF(al); } } else { _cmb = 0; }"
    c = c[:idx] + ns + c[c.find(";", idx)+1:]
c = sr(c, "Logger.Error($\"端面批处理异常: {ex.Message}\");", "Logger.Error(\"[EndFace] ProcessBatch: \"+ex.Message+\"
\"+ex.StackTrace);")
c = sr(c, "Logger.Error($\"端面工位处理异常: {ex.Message}\");", "Logger.Error(\"[EndFace] ProcessLoop: \"+ex.Message+\"
\"+ex.StackTrace);")

with open(p, "w", encoding="utf-8") as f: f.write(c)
print("EndFace: braces=" + str(c.count("{")==c.count("}")))