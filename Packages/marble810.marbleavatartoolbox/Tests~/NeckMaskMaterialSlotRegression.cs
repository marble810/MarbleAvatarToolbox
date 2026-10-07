// UnityMCP execute_code 方法体（Roslyn）。使用生产窗口与 GPU 管线，不改现有 Avatar。
var type = AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => a.GetType("marble810.MarbleAvatarToolbox.NeckMaskMaker.NeckMaskMaker")).First(t => t != null);
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var window = ScriptableObject.CreateInstance(type);
var go = new GameObject("NeckMaskSlotRegression") { hideFlags = HideFlags.HideAndDontSave };
var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
var material = new Material(Shader.Find("Unlit/Color")) { name = "Shared_Test_Material", hideFlags = HideFlags.HideAndDontSave };
var reads = new System.Collections.Generic.List<Texture2D>();
void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); }
object Call(string name, params object[] args) { return type.GetMethod(name, flags).Invoke(window, args); }
void Set(string name, object value) { type.GetField(name, flags).SetValue(window, value); }
object Field(object value, string name) { return value.GetType().GetField(name).GetValue(value); }
object Property(object value, string name) { return value.GetType().GetProperty(name).GetValue(value); }
System.Collections.IList Slots() { return (System.Collections.IList)Call("GetSelectedMaterialSlots"); }
void InjectLoop()
{
    var loopType = type.GetNestedType("LoopGeometry", System.Reflection.BindingFlags.NonPublic);
    var loop = Activator.CreateInstance(loopType, true);
    loopType.GetField("valid").SetValue(loop, true);
    loopType.GetField("segments").SetValue(loop, new[] { new Vector3(-10,0,0), new Vector3(10,0,0) });
    Set("_loopGeometry", loop); Set("_loopDirty", false);
    Set("_geometryCheckGeneration", type.GetField("_poseCheckGeneration", flags).GetValue(window));
}
Texture2D ReadSlot(object slot)
{
    var args = new object[] { slot, 0 };
    var t = (Texture2D)Call("BakeMaterialSlotTexture", args); reads.Add(t); return t;
}
byte Red(Texture2D t) { return t.GetPixels32()[12*64+12].r; }
try
{
    mesh.vertices = new[] { Vector3.zero, Vector3.right, new Vector3(0,0,0.001f),
        new Vector3(0,2,0), new Vector3(1,2,0), new Vector3(0,2,0.001f) };
    var uvs = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.zero, Vector2.right, Vector2.up };
    mesh.uv = uvs; mesh.subMeshCount = 2;
    mesh.SetTriangles(new[] { 0,1,2 }, 0); mesh.SetTriangles(new[] { 3,4,5 }, 1);
    go.AddComponent<MeshFilter>().sharedMesh = mesh;
    var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterials = new[] { material, material };
    Set("_body", go); Set("_textureSize", 64); Set("_dilation", 0);
    var slots = Slots(); Check(slots.Count == 2, "同一 Material 的两个槽不合并，默认全选");
    var a = slots[0]; var b = slots[1];
    Check((long)Property(a,"Key") != (long)Property(b,"Key"), "槽键互不碰撞");
    var analysis = Call("EnsureAnalysis", go, false); InjectLoop();
    var prepareArgs = new object[] { null };
    Check((bool)Call("TryEnsureGpuTextures", prepareArgs), "生产 GPU 槽烘焙准备成功");
    Check(Red(ReadSlot(a)) >= 253 && Red(ReadSlot(b)) == 0, "完全重叠 UV 各槽独立，白/黑不平均成灰");
    var previewA = (Mesh)Call("GetPreviewMesh", analysis, a);
    var previewB = (Mesh)Call("GetPreviewMesh", analysis, b);
    Check(previewA != previewB && previewA.triangles.SequenceEqual(new[] {0,1,2})
        && previewB.triangles.SequenceEqual(new[] {3,4,5}), "预览只绘制本槽三角形");
    Set("_showTexturePreview", true); Call("EnsureTexturePreviews");
    var panel = (System.Collections.IList)type.GetField("_texturePreviews", flags).GetValue(window);
    Check(panel.Count == 2 && (RenderTexture)Field(panel[0],"texture") != (RenderTexture)Field(panel[1],"texture"),
        "贴图面板借用各槽独立 RT");
    var baker = type.GetField("_gpuBaker", flags).GetValue(window);
    var bakerType = baker.GetType();
    var targetA = bakerType.GetMethod("Find").Invoke(baker, new[] { Property(a,"Key") });
    var targetB = bakerType.GetMethod("Find").Invoke(baker, new[] { Property(b,"Key") });
    var rtA = Property(targetA,"Texture"); var rtB = Property(targetB,"Texture");
    var samplesA = targetA.GetType().GetField("samples", flags).GetValue(targetA);
    Set("_maxDistance", 0.05f); Call("MarkParametersDirty");
    Call("TryEnsureGpuTextures", new object[] { null });
    Check(ReferenceEquals(rtA, Property(targetA,"Texture")) && ReferenceEquals(rtB, Property(targetB,"Texture"))
        && ReferenceEquals(samplesA, targetA.GetType().GetField("samples", flags).GetValue(targetA)), "参数调整复用槽 RT 与贡献缓存");
    var pngs = (System.Collections.Generic.List<byte[]>)Call("EncodeMaterialSlotMasks", slots);
    Check(pngs.Count == 2 && pngs.All(p => p.Length > 8 && p[0] == 137 && p[1] == 80), "每槽独立 PNG 编码");
    for (int i=0;i<2;i++)
    {
        var t = new Texture2D(2,2); reads.Add(t); Check(t.LoadImage(pngs[i]), "PNG 解码");
        Check(i == 0 ? Red(t) >= 253 : Red(t) == 0, "导出与对应槽预览一致");
    }
    string nameA = (string)Call("MaterialSlotFileName", "test", a);
    string nameB = (string)Call("MaterialSlotFileName", "test", b);
    Check(nameA != nameB && nameA.Contains("slot0") && nameB.Contains("slot1"), "相同材质文件名仍唯一");
    a.GetType().GetField("enabled").SetValue(a,false); Call("ResetMaterialSlotResources");
    Check(Slots().Count == 1 && ReferenceEquals(Slots()[0],b), "未选槽排除，列表刷新保持勾选");
    Call("EnsureTexturePreviews");
    panel = (System.Collections.IList)type.GetField("_texturePreviews", flags).GetValue(window);
    Check(panel.Count == 1, "未选槽不生成面板或烘焙缓存");
    // JsonUtility 保留内存夹具 instanceID；EditorJsonUtility 的资源引用持久化会置零未保存夹具。
    var serialized = JsonUtility.ToJson(window);
    JsonUtility.FromJsonOverwrite(serialized,window);
    Check(Slots().Count == 1, "序列化重载保留选择");
    Call("SetAllMaterialSlots", false);
    var emptyArgs = new object[] { null };
    Check(Slots().Count == 0 && !(bool)Call("TryEnsureGpuTextures", emptyArgs)
        && ((string)emptyArgs[0]).Contains("勾选"), "空选择明确拒绝烘焙");
    Call("SetAllMaterialSlots", true);
    renderer.sharedMaterials = new[] { material, material, material };
    Check(Slots().Count == 3 && (int)Property(Slots()[2],"SubMeshIndex") == 1, "多余材质槽映射最后子网格");
    renderer.sharedMaterials = new[] { material, null };
    Check(Slots().Count == 1, "空材质槽不可选");
    renderer.sharedMaterials = new[] { material, material };
    mesh.SetIndices(new[] {3,4}, MeshTopology.Lines, 1);
    Check(Slots().Count == 1, "非三角形槽不可选");
    Call("InvalidateAnalysis"); Call("EnsureAnalysis",go,false); InjectLoop();
    Check((bool)Call("TryEnsureGpuTextures",new object[] { null }), "混合拓扑只处理有效三角形槽");
    mesh.SetTriangles(new[] {3,4,5},1);
    slots = Slots();
    uvs[3] += Vector2.one*2; uvs[4] += Vector2.one*2; uvs[5] += Vector2.one*2; mesh.uv = uvs;
    Call("InvalidateAnalysis"); Call("EnsureAnalysis",go,false); InjectLoop();
    bool rejected = false;
    try { Call("EncodeMaterialSlotMasks", slots); }
    catch (System.Reflection.TargetInvocationException e) { rejected = e.InnerException.Message.Contains("Body [1]"); }
    Check(rejected, "任一槽 UV 无覆盖时批量编码失败，不静默漏导出");
    Check(renderer.sharedMaterials[0] == material && renderer.sharedMaterials[1] == material
        && mesh.GetTriangles(0).SequenceEqual(new[] {0,1,2}), "原材质与网格不变");
    return "PASS: 跨槽重叠白/黑隔离、相同材质不合并、预览索引隔离、面板 RT、缓存复用、独立 PNG、勾选保持/序列化、空选择、额外槽、空材质/非三角形、批量失败拒绝、原资源不变";
}
finally
{
    foreach (var t in reads) UnityEngine.Object.DestroyImmediate(t);
    UnityEngine.Object.DestroyImmediate(window);
    UnityEngine.Object.DestroyImmediate(go);
    UnityEngine.Object.DestroyImmediate(mesh);
    UnityEngine.Object.DestroyImmediate(material);
}
