// UnityMCP execute_code 的方法体（Roslyn）；只创建内存对象，不修改场景。
// 将本文件全文作为 code 执行。测试生产 BuildTopology / BuildLoop，而不是复制算法。
var tool = AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => a.GetType("marble810.MarbleAvatarToolbox.NeckMaskMaker.NeckMaskMaker"))
    .First(t => t != null);
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
var analysisType = tool.GetNestedType("MeshAnalysis", System.Reflection.BindingFlags.NonPublic);
object Analyze(Vector3[] vertices, int[] triangles)
{
    var a = Activator.CreateInstance(analysisType, true);
    analysisType.GetField("localVertices").SetValue(a, vertices);
    analysisType.GetField("worldVertices").SetValue(a, vertices);
    analysisType.GetField("triangles").SetValue(a, triangles);
    tool.GetMethod("BuildTopology", flags).Invoke(null, new object[] { a });
    return a;
}
int FindEdge(object a, int v0, int v1)
{
    var ea = (int[])analysisType.GetField("edgeA").GetValue(a);
    var eb = (int[])analysisType.GetField("edgeB").GetValue(a);
    return Enumerable.Range(0, ea.Length).First(e => ea[e] == Math.Min(v0, v1) && eb[e] == Math.Max(v0, v1));
}
System.Collections.Generic.List<int> Loop(object a, int edge)
{
    return (System.Collections.Generic.List<int>)tool.GetMethod("BuildLoop", flags).Invoke(null, new object[] { a, edge });
}
void Check(bool ok, string name)
{
    if (!ok) throw new Exception("FAIL: " + name);
}
const int segments = 12;
var vertices = new System.Collections.Generic.List<Vector3>();
var triangles = new System.Collections.Generic.List<int>();
for (int row = 0; row < 3; row++)
for (int i = 0; i < segments; i++)
{
    float angle = i * Mathf.PI * 2 / segments;
    vertices.Add(new Vector3(Mathf.Cos(angle), row, Mathf.Sin(angle)));
}
for (int row = 0; row < 2; row++)
for (int i = 0; i < segments; i++)
{
    int a = row * segments + i;
    int b = row * segments + (i + 1) % segments;
    triangles.AddRange(new[] { a, a + segments, b, b, a + segments, b + segments });
}
var regular = Analyze(vertices.ToArray(), triangles.ToArray());
foreach (int row in new[] { 0, 1, 2 })
{
    var loop = Loop(regular, FindEdge(regular, row * segments, row * segments + 1));
    Check(loop.Count == segments + 1 && loop[0] == loop[loop.Count - 1], "圆柱闭环 row=" + row);
    Check(loop.All(v => Mathf.Abs(vertices[v].y - row) < 1e-6f), "不能跳到相邻环 row=" + row);
}
// 每个三角形独立顶点：最强拆点案例，所有索引边都是假边界。
var split = triangles.Select(v => vertices[v]).ToArray();
var seam = Analyze(split, Enumerable.Range(0, split.Length).ToArray());
var representatives = (int[])analysisType.GetField("topologyVertices").GetValue(seam);
int top0 = Array.FindIndex(split, p => p == vertices[2 * segments]);
int top1 = Array.FindIndex(split, p => p == vertices[2 * segments + 1]);
var seamLoop = Loop(seam, FindEdge(seam, representatives[top0], representatives[top1]));
Check(seamLoop.Count == segments + 1 && seamLoop[0] == seamLoop[seamLoop.Count - 1], "拆点圆柱完整闭环");
Check(seamLoop.All(v => Mathf.Abs(split[v].y - 2) < 1e-6f), "拆点边界不能沿接缝向下走");
Check(split.Length == triangles.Count, "原始顶点数量保持不变");
// 闭合四价扇面：旧代码把闭合判断写反，无法返回对侧。
var octa = Analyze(new[] { Vector3.up, Vector3.down, Vector3.right, Vector3.forward, Vector3.left, Vector3.back },
    new[] { 0,2,3, 0,3,4, 0,4,5, 0,5,2, 1,3,2, 1,4,3, 1,5,4, 1,2,5 });
int opposite = (int)tool.GetMethod("OppositeEdgeAtVertex", flags).Invoke(null, new object[] { octa, 0, FindEdge(octa, 0, 2) });
Check(opposite == FindEdge(octa, 0, 4), "闭合四价扇面的对侧边");
var nonManifold = Analyze(new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward, Vector3.down },
    new[] { 0,1,2, 1,0,3, 0,1,4 });
Check(Loop(nonManifold, FindEdge(nonManifold, 0, 1)).Count == 0, "三面共边必须拒绝推导");
var shader = Shader.Find("Hidden/MarbleAvatarToolbox/NeckMaskPreview");
Check(shader != null && shader.isSupported && !UnityEditor.ShaderUtil.ShaderHasError(shader), "预览 Shader 编译支持");
return "PASS: 圆柱三环 / 全拆点接缝 / 闭合四价扇面 / 非流形边 / Shader";
