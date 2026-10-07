// UnityMCP execute_code 的方法体（Roslyn）；只读文案表与源码，不修改场景或资产。
// 将本文件全文作为 code 执行。测试生产 NeckMaskLoc 文案表与界面语言切换契约。
var loc = AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => a.GetType("marble810.MarbleAvatarToolbox.NeckMaskMaker.NeckMaskLoc"))
    .FirstOrDefault(t => t != null);
if (loc == null) return "FAIL: 找不到 NeckMaskLoc（脚本未编译）";

const string prefsKey = "MarbleAvatarToolbox.NeckMaskMaker.Language";
var any = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
var rows = (string[,])loc.GetField("Rows", any).GetValue(null);
var names = (string[])loc.GetField("LanguageNames", any).GetValue(null);
var languageProperty = loc.GetProperty("Language", any);
var translate = loc.GetMethod("T", any);
var format = loc.GetMethod("F", any);
Func<string, string> T = key => (string)translate.Invoke(null, new object[] { key });
Func<string, object[], string> F = (key, args) => (string)format.Invoke(null, new object[] { key, args });
Func<int> current = () => Convert.ToInt32(languageProperty.GetValue(null));
void Check(bool ok, string name)
{
    if (!ok) throw new Exception("FAIL: " + name);
}

// 文案表结构：四列、键与中文一致、三语非空、键不重复。
Check(rows.GetLength(1) == 4, "文案表必须是 4 列");
Check(rows.GetLength(0) >= 60, "文案表条目数量异常: " + rows.GetLength(0));
var keys = new System.Collections.Generic.HashSet<string>();
for (int i = 0; i < rows.GetLength(0); i++)
{
    string key = rows[i, 0];
    Check(!string.IsNullOrWhiteSpace(key), "第 " + i + " 行键为空");
    Check(keys.Add(key), "重复键: " + key);
    Check(key == rows[i, 1], "键必须等于中文原文: " + key);
    for (int column = 1; column < 4; column++)
        Check(!string.IsNullOrWhiteSpace(rows[i, column]), "空翻译: " + key + " 列 " + column);
}
Check(names.Length == 3 && names[0] == "ZH" && names[1] == "JP" && names[2] == "EN", "语言身份名必须是 ZH/JP/EN");
var labels = (string[])loc.GetField("LanguageLabels", any).GetValue(null);
Check(labels.Length == names.Length, "LanguageLabels 必须与 LanguageNames 等长");
Check(labels[0] == "中文" && labels[1] == "日本語" && labels[2] == "English", "下拉框必须用各自语言显示：中文/日本語/English");

// 三语切换、EditorPrefs 记忆、缺键回退与占位符格式化。
int original = current();
try
{
    var samples = new[]
    {
        new[] { "预览", "预览", "プレビュー", "Preview" },
        new[] { "刷新网格", "刷新网格", "メッシュ更新", "Refresh Mesh" },
        new[] { "全不选", "全不选", "すべて解除", "Deselect All" },
    };
    for (int column = 0; column < names.Length; column++)
    {
        int value = Convert.ToInt32(Enum.Parse(languageProperty.PropertyType, names[column]));
        languageProperty.SetValue(null, Enum.Parse(languageProperty.PropertyType, names[column]));
        Check(value == column, "语言值必须是列索引: " + names[column]);
        Check(current() == column, "Language 未切换: " + names[column]);
        Check(EditorPrefs.GetInt(prefsKey, -1) == column, "语言未写入 EditorPrefs: " + names[column]);
        foreach (var sample in samples)
            Check(T(sample[0]) == sample[column + 1], names[column] + " 文案不符: " + sample[0]);
        Check(T("__不存在的键__") == "__不存在的键__", names[column] + " 缺键必须回退键本身");
    }

    languageProperty.SetValue(null, Enum.Parse(languageProperty.PropertyType, "EN"));
    Check(F("已确认 {0} 条循环线。", new object[] { 7 }) == "Confirmed 7 loop(s).", "F() 英文格式化");
    languageProperty.SetValue(null, Enum.Parse(languageProperty.PropertyType, "JP"));
    Check(F("已确认 {0} 条循环线。", new object[] { 7 }) == "7 本のループを確定しました。", "F() 日文格式化");
    Check(F("{0}：{1}", new object[] { "Body [0] mat", "err" }) == "Body [0] mat：err", "F() 两参数键");
}
finally
{
    languageProperty.SetValue(null, Enum.Parse(languageProperty.PropertyType, names[original]));
}
Check(current() == original, "测试必须恢复原语言");

// 源码级检查：三个代码文件里不应再有文案表之外的直接中文字面量。
string folder = System.IO.Path.Combine(Application.dataPath,
    "../Packages/marble810.marbleavatartoolbox/Editor/Tools/NeckMaskMaker");
var leaked = new System.Collections.Generic.List<string>();
if (System.IO.Directory.Exists(folder))
{
    // 只扫中文字符串字面量；#region 名与注释允许是中文。
    var chinese = new System.Text.RegularExpressions.Regex("[\u4e00-\u9fff]");
    var literal = new System.Text.RegularExpressions.Regex("\"[^\"]*\"");
    var call = new System.Text.RegularExpressions.Regex("NeckMaskLoc\\.(T|F)\\([^()]*\\)");
    foreach (var name in new[] { "NeckMaskMaker.cs", "NeckMaskMaterialSlots.cs", "NeckMaskGpuBaker.cs", "NeckMaskSampling.cs" })
    {
        string path = System.IO.Path.Combine(folder, name);
        if (!System.IO.File.Exists(path)) { leaked.Add(name + "(缺失)"); continue; }
        string text = call.Replace(System.IO.File.ReadAllText(path), "LOC");
        foreach (var rawLine in text.Split('\n'))
        {
            string code = rawLine.Split(new[] { "//" }, 2, StringSplitOptions.None)[0];
            foreach (System.Text.RegularExpressions.Match match in literal.Matches(code))
                if (chinese.IsMatch(match.Value)) leaked.Add(name + ": " + match.Value);
        }
    }
}
Check(leaked.Count == 0, "仍有未走文案表的中文字面量: " + string.Join(", ", leaked));
return "PASS: 文案表 " + rows.GetLength(0) + " 条 / 三语切换 / 缺键回退 / F() 格式化 / EditorPrefs 记忆 / 源码无裸文案";
