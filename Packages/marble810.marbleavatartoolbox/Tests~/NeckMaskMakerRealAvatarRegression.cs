// UnityMCP execute_code 的方法体；需要本次复现所用 Body_Base 与已打开的工具窗口。
// 只检查当前选择的种子边，不替换选择、不修改场景。
var w = Resources.FindObjectsOfTypeAll<EditorWindow>().First(x => x.GetType().Name == "NeckMaskMaker");
var t = w.GetType(); var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var go = (GameObject)t.GetField("_selectionSource", f).GetValue(w);
var a = t.GetMethod("EnsureAnalysis", f).Invoke(w, new object[]{go,false});
var at = a.GetType(); var af = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
var ea = (int[])at.GetField("edgeA",af).GetValue(a); var eb = (int[])at.GetField("edgeB",af).GetValue(a);
var counts = (byte[])at.GetField("edgeFaceCount",af).GetValue(a);
var world = (Vector3[])at.GetField("worldVertices",af).GetValue(a);
var ls = (System.Collections.IList)t.GetField("_loops", f).GetValue(w);
var loop = (System.Collections.Generic.List<int>)ls[0].GetType().GetField("vertices").GetValue(ls[0]);
var edges = Enumerable.Range(0,ea.Length).Where(e=>ea[e]==Math.Min(loop[0],loop[1]) && eb[e]==Math.Max(loop[0],loop[1])).ToArray();

var seed=edges[0];
var actual=(System.Collections.Generic.List<int>)t.GetMethod("BuildLoop", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,new object[]{a,seed});
float span=actual.Max(v=>world[v].y)-actual.Min(v=>world[v].y);
if(span>0.05f || actual[0]!=actual[actual.Count-1]) throw new Exception("顶部边循环泄漏到身体：seed="+seed+", vertices="+actual.Count+", heightSpan="+span);
return "PASS vertices="+actual.Count+", heightSpan="+span;