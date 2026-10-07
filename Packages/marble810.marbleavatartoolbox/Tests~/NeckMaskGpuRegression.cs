// UnityMCP execute_code 的方法体（Roslyn）。合成 GPU 资源验证，不修改场景，不做测速。
var type = AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => a.GetType("marble810.MarbleAvatarToolbox.NeckMaskMaker.NeckMaskGpuBaker"))
    .First(t => t != null);
var modeType = AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => a.GetType("marble810.MarbleAvatarToolbox.NeckMaskMaker.NeckMaskSamplingMode"))
    .First(t => t != null);
var samplingType = AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => a.GetType("marble810.MarbleAvatarToolbox.NeckMaskMaker.NeckMaskSampling"))
    .First(t => t != null);
var baker = Activator.CreateInstance(type, new object[] { 64 });
var textures = new System.Collections.Generic.List<Texture2D>();
var prepare = type.GetMethod("Prepare");
var privateFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); }
object Prepare(int id, Vector2[] uv, int[] indices, Vector3[] vertices,
    float distance = 1, string mode = "Smooth", float parameter = 1, int dilation = 0, int geometry = 1)
{
    return prepare.Invoke(baker, new object[] { id, uv, indices, vertices,
        new[] { new Vector3(-10, 0, 0), new Vector3(10, 0, 0) },
        geometry, distance, Enum.Parse(modeType, mode), parameter, dilation });
}
RenderTexture RT(object target) { return (RenderTexture)target.GetType().GetProperty("Texture").GetValue(target); }
Texture2D Read(RenderTexture rt)
{
    var texture = (Texture2D)type.GetMethod("Readback").Invoke(null, new object[] { rt });
    textures.Add(texture);
    return texture;
}
byte Red(Texture2D texture, int x = 12, int y = 12) { return texture.GetPixels32()[y * 64 + x].r; }
try
{
    var windowType = AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType("marble810.MarbleAvatarToolbox.NeckMaskMaker.NeckMaskMaker")).First(x => x != null);
    var allPrivate = privateFlags | System.Reflection.BindingFlags.Static;
    Check(new[] { "_surfaceSampling", "_smoothIterations", "_masks", "_smoothness" }.All(n => windowType.GetField(n, allPrivate) == null),
        "旧版模式和 Smoothness 字段已移除");
    Check(new[] { "_samplingMode", "_smoothCurvature", "_constantThreshold" }
        .All(n => windowType.GetField(n, allPrivate) != null), "衰减模式与逐模式参数字段存在");
    Check(new[] { "ComputeMask", "SmoothMask", "BakeTextureForTargets", "RasterizeMesh", "Dilate", "DrawLegacyMaskPreview" }
        .All(n => windowType.GetMethod(n, allPrivate) == null), "旧版计算和 CPU 回退已移除");
    var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/marble810.marbleavatartoolbox/Shaders/NeckMaskBake.compute");
    Check(!compute.HasKernel("MapLegacy"), "旧版 GPU 插值内核已移除");
    Check(Shader.Find("Hidden/MarbleAvatarToolbox/NeckMaskPreview").FindPropertyIndex("_UseTexture") == -1,
        "顶点色预览切换已移除");
    // 参数契约：模式名与枚举顺序一致，按模式收敛，线性无参数。
    var modeNames = (string[])samplingType.GetField("ModeNames", allPrivate).GetValue(null);
    Check(modeNames.SequenceEqual(new[] { "Linear", "Smooth", "Constant" })
        && modeNames.SequenceEqual(Enum.GetNames(modeType)), "模式名与枚举顺序一致");
    var clamp = samplingType.GetMethod("ClampParameter", allPrivate);
    Check((float)clamp.Invoke(null, new object[] { Enum.Parse(modeType, "Smooth"), 100f }) == 4f
        && (float)clamp.Invoke(null, new object[] { Enum.Parse(modeType, "Smooth"), 0.01f }) == 0.25f
        && (float)clamp.Invoke(null, new object[] { Enum.Parse(modeType, "Constant"), 1.5f }) == 1f
        && (float)clamp.Invoke(null, new object[] { Enum.Parse(modeType, "Linear"), 0.7f }) == 0f,
        "按模式收敛参数");
    var uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.zero, Vector2.right, Vector2.up };
    var indices = new[] { 0, 1, 2, 3, 4, 5 };
    // 同 UV 对应两个表面，距离分别为 0 与 0.75。逐贡献求值再平均，不平均距离。
    var vertices = new[] { Vector3.zero, Vector3.right, Vector3.right * 2,
        new Vector3(0, 0, 0.75f), new Vector3(1, 0, 0.75f), new Vector3(2, 0, 0.75f) };
    var low = Prepare(1, uv, indices, vertices);
    var targetType = low.GetType();
    var fields = targetType.GetField("fields", privateFlags).GetValue(low);
    var samples = targetType.GetField("samples", privateFlags).GetValue(low);
    var originalRT = RT(low);
    Check(Math.Abs(Red(Read(originalRT)) - 147) <= 1, "重叠 UV 分别求值后平均");
    low = Prepare(1, uv, indices, vertices, distance: 2);
    Check(Math.Abs(Red(Read(RT(low))) - 215) <= 1, "扩大 Max Distance 不缺距离数据");
    Check(ReferenceEquals(fields, targetType.GetField("fields", privateFlags).GetValue(low)), "参数拖动复用距离 Buffer");
    Check(ReferenceEquals(samples, targetType.GetField("samples", privateFlags).GetValue(low)), "参数拖动复用 UV 贡献映射");
    Check(RT(low) == originalRT, "参数拖动复用最终 RT");

    // 三种衰减模式：同像素为距离 0 与 0.75 两个贡献的平均值（Max Distance = 1）。
    var linear = Prepare(6, uv, indices, vertices, mode: "Linear");
    Check(Math.Abs(Red(Read(RT(linear))) - 159) <= 1, "Linear 为原版线性衰减");
    var smooth = Prepare(7, uv, indices, vertices, mode: "Smooth", parameter: 2);
    Check(Math.Abs(Red(Read(RT(smooth))) - 179) <= 1, "Smooth 曲率改变曲线弯曲程度");
    var tight = Prepare(8, uv, indices, vertices, mode: "Smooth", parameter: 0.5f);
    Check(Math.Abs(Red(Read(RT(tight))) - 134) <= 1, "Smooth 曲率越小越快脱离边界线");
    var clamped = Prepare(9, uv, indices, vertices, mode: "Smooth", parameter: 100f);
    Check(Math.Abs(Red(Read(RT(clamped))) - 225) <= 1, "Smooth 曲率超范围时按上限计算");
    var constant = Prepare(10, uv, indices, vertices, mode: "Constant", parameter: 0.5f);
    Check(Math.Abs(Red(Read(RT(constant))) - 128) <= 1, "Constant 阈值之外归零，只有 0/1 两档");
    var binarized = Prepare(11, uv, indices, vertices, mode: "Constant", parameter: 0.8f);
    Check(Red(Read(RT(binarized))) == 255, "Constant 阈值以内整体为 1");
    var switched = Prepare(6, uv, indices, vertices, mode: "Constant", parameter: 0.5f);
    Check(ReferenceEquals(RT(linear), RT(switched)) && Math.Abs(Red(Read(RT(switched))) - 128) <= 1,
        "切换模式复用最终 RT 但重算 Mask");

    var singleUv = new[] { Vector2.zero, Vector2.right, Vector2.up };
    var singleIndices = new[] { 0, 1, 2 };
    var far = new[] { new Vector3(0, 0, 2), new Vector3(1, 0, 2), new Vector3(2, 0, 2) };
    var high = Prepare(2, singleUv, singleIndices, far);
    var composeArgs = new object[] { low, high, 0, 0 };
    var merged = (RenderTexture)type.GetMethod("Compose").Invoke(baker, composeArgs);
    Check(Red(Read(merged)) == 0 && (int)composeArgs[3] > 0, "高优先级 0 Mask 仍覆盖低优先级");

    var onLine = new[] { Vector3.zero, Vector3.right, Vector3.right * 2 };
    var edge = Prepare(3, singleUv, singleIndices, onLine);
    Check(Red(Read(RT(edge)), 40, 24) == 0, "三角形外未覆盖像素为 0");
    edge = Prepare(3, singleUv, singleIndices, onLine, dilation: 1);
    Check(Red(Read(RT(edge)), 40, 24) == 255, "GPU 一轮邻居平均外扩填充边缘");
    edge = Prepare(3, singleUv, singleIndices, far, geometry: 2);
    var empty = Read(RT(edge)).GetPixels32()[12 * 64 + 12];
    Check(empty.r == 0 && empty.a == 0, "零 Mask 的 Alpha 与 RGB 一致");
    var varying = new[] { Vector3.zero, new Vector3(1, 0, 1), Vector3.right * 2 };
    var precise = Prepare(4, singleUv, singleIndices, varying);
    float t = 12.5f / 64f;
    int exact = Mathf.RoundToInt((1 - t * t * (3 - 2 * t)) * 255);
    Check(Math.Abs(Red(Read(RT(precise))) - exact) <= 1, "稀疏三角形内先重建位置再计算非线性衰减");

    var ramp = Prepare(5, singleUv, singleIndices, varying, mode: "Linear", dilation: 8);
    var raw = (RenderTexture)ramp.GetType().GetField("raw", privateFlags).GetValue(ramp);
    var previous = RenderTexture.active;
    var rawRead = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
    textures.Add(rawRead);
    try
    {
        RenderTexture.active = raw;
        rawRead.ReadPixels(new Rect(0, 0, 64, 64), 0, 0, false);
        rawRead.Apply(false, false);
    }
    finally { RenderTexture.active = previous; }
    var rawPixels = rawRead.GetPixels();
    var values = rawPixels.Select(p => p.r).ToArray();
    var covered = rawPixels.Select(p => (byte)(p.g > 0 ? 1 : 0)).ToArray();
    // 测试专用参考：只验证逐轮邻居平均外扩，不在生产代码保留 CPU Mask 回退。
    for (int iteration = 0; iteration < 8; iteration++)
    {
        var next = (float[])values.Clone();
        var nextCovered = (byte[])covered.Clone();
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            int pixel = y * 64 + x;
            if (covered[pixel] != 0) continue;
            float sum = 0; int count = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= 64 || ny < 0 || ny >= 64 || covered[ny * 64 + nx] == 0) continue;
                sum += values[ny * 64 + nx]; count++;
            }
            if (count > 0) { next[pixel] = sum / count; nextCovered[pixel] = 1; }
        }
        values = next; covered = nextCovered;
    }
    var padded = Read(RT(ramp)).GetPixels32();
    int maxDifference = 0;
    for (int p = 0; p < values.Length; p++)
        maxDifference = Math.Max(maxDifference, Math.Abs(padded[p].r - Mathf.RoundToInt(Mathf.Clamp01(values[p]) * 255)));
    Check(maxDifference <= 1, "GPU 八轮外扩与测试专用邻居平均参考一致");
    return "PASS: 三种衰减模式 / 参数收敛 / 重叠 UV / 扩大距离 / 缓存复用 / 零值优先覆盖 / Dilation 参考对比 / Alpha / 旧版移除";
}
finally
{
    foreach (var texture in textures) UnityEngine.Object.DestroyImmediate(texture);
    ((IDisposable)baker).Dispose();
}
