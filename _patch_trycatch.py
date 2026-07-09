import os, re
base = r"E:\公司-张皓茗\项目\高露洁\扬州\扬州高露洁Koch机初版"

def wrap_body(content, method_sig, error_tag, log_func):
    idx = content.find(method_sig)
    if idx < 0: return content
    body_start = content.find("{", idx)
    depth = 0
    for i in range(body_start, len(content)):
        if content[i] == "{": depth += 1
        elif content[i] == "}":
            depth -= 1
            if depth == 0:
                body_end = i + 1
                break
    if body_end <= body_start: return content
    body = content[body_start:body_end]
    inner = body[1:-1]  # strip outer { }
    new_body = "{ try {" + inner + "} catch (Exception ex) { Logger.Error("" + error_tag + "异常: " + ex.Message + "
" + ex.StackTrace); " + log_func + " } }"
    return content[:body_start] + new_body + content[body_end:]

print("Adding try-catch to all unprotected methods...")

# SideStationProcessor
p = os.path.join(base, "VisionMeasure", "Stations", "SideStationProcessor.cs")
with open(p, encoding="utf-8") as f: c = f.read()
for sig, tag in [
    ("public void OnCam7(Bitmap", "[Side] OnCam7"),
    ("public void OnCam8(Bitmap", "[Side] OnCam8"),
]:
    c = wrap_body(c, sig, tag, "WriteSideErrorLog("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + tag + "异常: " + ex.Message);")
with open(p, "w", encoding="utf-8") as f: f.write(c)
print("  Side: OnCam7/OnCam8 wrapped")

# FrontStationProcessor
p = os.path.join(base, "VisionMeasure", "Stations", "FrontStationProcessor.cs")
with open(p, encoding="utf-8") as f: c = f.read()
for sig, tag in [
    ("public void OnCam1(Bitmap", "[Front] OnCam1"),
    ("public void OnCam2(Bitmap", "[Front] OnCam2"),
]:
    c = wrap_body(c, sig, tag, "WFrontErr("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + tag + "异常: " + ex.Message);")
with open(p, "w", encoding="utf-8") as f: f.write(c)
print("  Front: OnCam1/OnCam2 wrapped")

# BackStationProcessor
p = os.path.join(base, "VisionMeasure", "Stations", "BackStationProcessor.cs")
with open(p, encoding="utf-8") as f: c = f.read()
for sig, tag in [
    ("public void OnCam3(Bitmap", "[Back] OnCam3"),
    ("public void OnCam4(Bitmap", "[Back] OnCam4"),
]:
    c = wrap_body(c, sig, tag, "WBackErr("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + tag + "异常: " + ex.Message);")
with open(p, "w", encoding="utf-8") as f: f.write(c)
print("  Back: OnCam3/OnCam4 wrapped")

# EndFaceStationProcessor
p = os.path.join(base, "VisionMeasure", "Stations", "EndFaceStationProcessor.cs")
with open(p, encoding="utf-8") as f: c = f.read()
for sig, tag in [
    ("public void OnCam5(Bitmap", "[EndFace] OnCam5"),
    ("public void OnCam6(Bitmap", "[EndFace] OnCam6"),
]:
    c = wrap_body(c, sig, tag, "WriteEndFaceErrorLog("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + tag + "异常: " + ex.Message);")
with open(p, "w", encoding="utf-8") as f: f.write(c)
print("  EndFace: OnCam5/OnCam6 wrapped")

# MainFrm - OnStationResult(ProductResult)
p = os.path.join(base, "VisionMeasure", "From", "MainFrm.cs")
with open(p, encoding="utf-8") as f: c = f.read()
c = wrap_body(c, "private void OnStationResult(ProductResult result)", "[MainFrm] OnStationResult", "Logger.LogErrorToFile("MainFrm", "OnStationResult异常: " + ex.Message + "
" + ex.StackTrace);")
c = wrap_body(c, "private void UpdateStatistics(ProductResult result)", "[MainFrm] UpdateStatistics", "")
c = wrap_body(c, "private void OnEndFaceStatusUpdate(List<string>", "[MainFrm] OnEndFaceStatusUpdate", "")
c = wrap_body(c, "private void OnSideStatusUpdate(List<string>", "[MainFrm] OnSideStatusUpdate", "")
with open(p, "w", encoding="utf-8") as f: f.write(c)
print("  MainFrm: OnStationResult/UpdateStatistics/StatusUpdate wrapped")

# Verify all files
for fname in ["FrontStationProcessor.cs", "BackStationProcessor.cs", "EndFaceStationProcessor.cs", "SideStationProcessor.cs", "MainFrm.cs"]:
    p = os.path.join(base, "VisionMeasure", "Stations" if "Station" in fname else "VisionMeasure/From", fname)
    if not os.path.exists(p): p = os.path.join(base, "VisionMeasure/From", fname)
    with open(p, encoding="utf-8") as f: content = f.read()
    ok = content.count("{") == content.count("}")
    print(f"  {fname}: braces {'OK' if ok else 'MISMATCH!'}")

print("All done.")
