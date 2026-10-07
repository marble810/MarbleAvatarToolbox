// UnityMCP execute_code 方法体（Roslyn）。依赖已打开且已选循环线的真实窗口。
// 使用独立测试窗口，只读取真实模型；导出到工程 Temp，不修改原窗口选择与 Avatar。
var type = AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => a.GetType("marble810.MarbleAvatarToolbox.NeckMaskMaker.NeckMaskMaker")).First(t => t != null);
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var original = Resources.FindObjectsOfTypeAll(type).First(w =>
    ((System.Collections.IList)type.GetField("_loops", flags).GetValue(w)).Count > 0);
var test = ScriptableObject.CreateInstance(type);
void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); }
object Call(string name, params object[] args) { return type.GetMethod(name, flags).Invoke(test,args); }
var reads = new System.Collections.Generic.List<Texture2D>();
try
{
    foreach (var field in new[] { "_body", "_bodyBase", "_loops", "_selectionSource", "_maxDistance", "_samplingMode", "_smoothCurvature", "_constantThreshold", "_dilation" })
        type.GetField(field,flags).SetValue(test,type.GetField(field,flags).GetValue(original));
    type.GetField("_textureSize",flags).SetValue(test,256);
    var body = (GameObject)type.GetField("_body",flags).GetValue(test);
    var bodyBase = (GameObject)type.GetField("_bodyBase",flags).GetValue(test);
    var objects = new[] { body, bodyBase };
    var renderers = objects.Select(o => o.GetComponent<Renderer>()).ToArray();
    var materials = renderers.Select(r => r.sharedMaterials).ToArray();
    var slots = (System.Collections.IList)Call("GetSelectedMaterialSlots");
    Check(slots.Count == 3, "真实 Avatar 两个 Body 槽与一个 Body_base 槽");
    string root = System.IO.Path.Combine(Application.dataPath,"../Temp/NeckMaskMaker/material-slots");
    System.IO.Directory.CreateDirectory(root);
    string prefix = "slot-regression-" + Guid.NewGuid().ToString("N");
    var paths = new System.Collections.Generic.List<string>();
    foreach (var slot in slots) paths.Add(System.IO.Path.Combine(root,(string)Call("MaterialSlotFileName",prefix,slot)));
    var writeArgs = new object[] { slots, paths, 0 };
    Call("WriteMaterialSlotMasks",writeArgs);
    Check((int)writeArgs[2] == 3 && paths.All(System.IO.File.Exists), "生产保存路径输出三张独立 PNG");
    var report = new System.Text.StringBuilder();
    for (int i=0;i<slots.Count;i++)
    {
        var slot = slots[i]; var st = slot.GetType();
        var target = (GameObject)st.GetField("target").GetValue(slot);
        int sub = (int)st.GetProperty("SubMeshIndex").GetValue(slot);
        var analysis = Call("EnsureAnalysis",target,false);
        var preview = (Mesh)Call("GetPreviewMesh",analysis,slot);
        var source = (Mesh)st.GetField("sourceMesh").GetValue(slot);
        Check(preview.triangles.SequenceEqual(source.GetTriangles(sub)), "真实预览只绘制对应子网格");
        var rtArgs = new object[] { slot, 0 };
        var rtRead = (Texture2D)Call("BakeMaterialSlotTexture",rtArgs); reads.Add(rtRead);
        var disk = new Texture2D(2,2,TextureFormat.RGBA32,false,true); reads.Add(disk);
        Check(disk.LoadImage(System.IO.File.ReadAllBytes(paths[i])), "真实 PNG 可读");
        Check(disk.GetPixels32().SequenceEqual(rtRead.GetPixels32()), "真实 PNG 像素与槽最终 RT 一致");
        report.AppendLine(st.GetProperty("Label").GetValue(slot) + " triangles="+preview.triangles.Length/3
            + " coverage="+rtArgs[1]+" PNG="+System.IO.Path.GetFileName(paths[i]));
    }
    for (int i=0;i<renderers.Length;i++) Check(materials[i].SequenceEqual(renderers[i].sharedMaterials),"真实材质引用不变");
    return "PASS: 真实 Avatar 三槽独立烘焙/生产保存/预览索引/PNG 与 RT 一致/材质不变\n" + report + "目录="+root;
}
finally
{
    foreach (var texture in reads) UnityEngine.Object.DestroyImmediate(texture);
    UnityEngine.Object.DestroyImmediate(test);
}
