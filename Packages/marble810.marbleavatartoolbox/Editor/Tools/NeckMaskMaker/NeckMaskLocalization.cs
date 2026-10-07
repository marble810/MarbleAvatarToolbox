using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;

namespace marble810.MarbleAvatarToolbox.NeckMaskMaker
{
    /// <summary>界面语言。数值同时是文案表的列索引与 EditorPrefs 中的持久化值，不要调整既有顺序。</summary>
    internal enum NeckMaskLanguage
    {
        ZH = 0,
        JP = 1,
        EN = 2,
    }

    /// <summary>
    /// Neck Mask Maker 的界面文案表（ZH / JP / EN）。
    ///
    /// 以中文原文作为键：查表失败时回退到键本身，漏翻的文案仍显示中文，不会出现空串或内部 key。
    /// 语言选择保存在 EditorPrefs，跨会话与窗口记忆。
    /// 所有用户可见文案（界面、Tooltip、状态、对话框与异常）都必须经过 <see cref="T"/> / <see cref="F"/>，
    /// 不要在业务代码里直接写中文字面量。
    /// </summary>
    internal static class NeckMaskLoc
    {
        private const string LanguageKey = "MarbleAvatarToolbox.NeckMaskMaker.Language";

        /// <summary>语言的身份名，与 <see cref="NeckMaskLanguage"/> 一致，用于枚举解析与回归脚本。</summary>
        internal static readonly string[] LanguageNames = { "ZH", "JP", "EN" };

        /// <summary>下拉框显示文本：每种语言用自身语言书写；与 <see cref="LanguageNames"/> 等长且顺序一致。</summary>
        internal static readonly string[] LanguageLabels = { "中文", "日本語", "English" };

        /// <summary>文案表：第一列为键（中文原文），其后依次为简体中文、日本語、English。</summary>
        private static readonly string[,] Rows =
        {
            // 窗口与语言
            { "由 Deepseek V4.1 Flash 翻译", "由 Deepseek V4.1 Flash 翻译", "Deepseek V4.1 Flash による翻訳", "Translated by Deepseek V4.1 Flash" },

            // 目标对象
            { "目标对象", "目标对象", "対象オブジェクト", "Target Objects" },
            { "完整身体（含头部）的网格对象。", "完整身体（含头部）的网格对象。", "頭を含む全身のメッシュオブジェクト。", "Mesh object of the full body including the head." },
            { "去掉头部的身体基底，颈部循环线从这里拾取。", "去掉头部的身体基底，颈部循环线从这里拾取。", "頭を除いた身体ベース。ネックの境界線はここから取得します。", "Body base without the head. The neck boundary loop is picked from here." },
            { "刷新网格", "刷新网格", "メッシュ更新", "Refresh Mesh" },
            { "已重新读取网格数据。", "已重新读取网格数据。", "メッシュデータを再読み込みしました。", "Mesh data reloaded." },
            { "请先指定 Body 与 Body_base。", "请先指定 Body 与 Body_base。", "先に Body と Body_base を指定してください。", "Specify Body and Body_base first." },
            { "未指定 Body_base：颈部边界线将改为从 Body 拾取（通常需要内部循环选择）。", "未指定 Body_base：颈部边界线将改为从 Body 拾取（通常需要内部循环选择）。", "Body_base が未指定です。ネック境界線は Body から取得します（多くの場合、内側のループ選択が必要です）。", "Body_base is not set: the neck boundary loop will be picked from Body instead (usually requires selecting an inner loop)." },
            { "Body 上找不到可用的 MeshRenderer / SkinnedMeshRenderer。", "Body 上找不到可用的 MeshRenderer / SkinnedMeshRenderer。", "Body に使用可能な MeshRenderer / SkinnedMeshRenderer がありません。", "No usable MeshRenderer / SkinnedMeshRenderer found on Body." },
            { "Body_base 上找不到可用的 MeshRenderer / SkinnedMeshRenderer。", "Body_base 上找不到可用的 MeshRenderer / SkinnedMeshRenderer。", "Body_base に使用可能な MeshRenderer / SkinnedMeshRenderer がありません。", "No usable MeshRenderer / SkinnedMeshRenderer found on Body_base." },

            // 目标材质槽
            { "目标材质槽", "目标材质槽", "対象マテリアルスロット", "Target Material Slots" },
            { "全选", "全选", "すべて選択", "Select All" },
            { "全不选", "全不选", "すべて解除", "Deselect All" },
            { "没有可用的材质槽，请先设置 Body 与 Body_base。", "没有可用的材质槽，请先设置 Body 与 Body_base。", "使用可能なマテリアルスロットがありません。先に Body と Body_base を設定してください。", "No usable material slots. Set Body and Body_base first." },
            { "无可用槽", "无可用槽", "スロットなし", "No slots" },
            { "此槽无有效材质或三角形，不能烘焙。", "此槽无有效材质或三角形，不能烘焙。", "このスロットには有効なマテリアルまたは三角形がないためベイクできません。", "This slot has no valid material or triangles and cannot be baked." },
            { "请至少勾选一个有效材质槽。", "请至少勾选一个有效材质槽。", "有効なマテリアルスロットを 1 つ以上選択してください。", "Select at least one valid material slot." },
            { "选择目标材质槽", "选择目标材质槽", "対象マテリアルスロットを選択", "Select Target Material Slot" },
            { "空材质", "空材质", "マテリアルなし", "No Material" },

            // 颈部边界线
            { "颈部边界线", "颈部边界线", "ネック境界線", "Neck Boundary Loop" },
            { "选择颈部边界线", "选择颈部边界线", "ネック境界線を選択", "Select Neck Boundary Loop" },
            { "结束选择（Enter）", "结束选择（Enter）", "選択を終了（Enter）", "Finish Selection (Enter)" },
            { "清除", "清除", "クリア", "Clear" },
            { "没有可拾取的对象，请先设置 Body_base 或 Body。", "没有可拾取的对象，请先设置 Body_base 或 Body。", "取得できるオブジェクトがありません。先に Body_base または Body を設定してください。", "No object to pick from. Set Body_base or Body first." },
            { "点击“选择颈部边界线”后，在 Scene 视图中点击 Body_base 颈部的任意一条边，工具会自动吸附为整条循环线。Shift + 左键可追加循环线，Enter 确认，Esc 取消。", "点击“选择颈部边界线”后，在 Scene 视图中点击 Body_base 颈部的任意一条边，工具会自动吸附为整条循环线。Shift + 左键可追加循环线，Enter 确认，Esc 取消。", "「ネック境界線を選択」を押したあと、Scene ビューで Body_base のネック部分の辺をクリックすると、ループ全体が自動的に選択されます。Shift + 左クリックでループを追加、Enter で確定、Esc でキャンセル。", "After clicking \"Select Neck Boundary Loop\", click any edge around the neck of Body_base in the Scene view and the whole loop is snapped automatically. Shift + Left Click appends another loop, Enter confirms, Esc cancels." },
            { "已选择 {0} 条循环线，共 {1} 个顶点", "已选择 {0} 条循环线，共 {1} 个顶点", "{0} 本のループ、合計 {1} 頂点を選択中", "{0} loop(s) selected, {1} vertices in total" },
            { "颈部边界线选择模式", "颈部边界线选择模式", "ネック境界線の選択モード", "Neck Boundary Loop Selection Mode" },
            { "左键点击边线自动吸附整条循环 · Shift + 左键追加 · Enter 确认 · Esc 取消", "左键点击边线自动吸附整条循环 · Shift + 左键追加 · Enter 确认 · Esc 取消", "左クリックで辺をループ全体にスナップ · Shift + 左クリックで追加 · Enter で確定 · Esc でキャンセル", "Left click an edge to snap the whole loop · Shift + Left Click to append · Enter to confirm · Esc to cancel" },

            // Mask 参数
            { "Mask 参数", "Mask 参数", "Mask 設定", "Mask Settings" },
            { "边界线到 Mask 完全消失处的距离（单位：米，按世界空间计算）。", "边界线到 Mask 完全消失处的距离（单位：米，按世界空间计算）。", "境界線から Mask が完全に消える位置までの距離（単位：メートル、ワールド空間基準）。", "Distance from the boundary loop to where the mask fully fades out (meters, world space)." },
            { "距离衰减方式：Linear 线性、Smooth 平滑（曲率可调）、Constant 按阈值二值化。", "距离衰减方式：Linear 线性、Smooth 平滑（曲率可调）、Constant 按阈值二值化。", "距離の減衰方式：Linear は線形、Smooth は平滑（曲率を調整可）、Constant はしきい値による二値化。", "Distance falloff mode: Linear, Smooth (adjustable curvature), or Constant (threshold binarization)." },
            { "平滑衰减的曲率。1 = 标准 smoothstep；越大 Mask 越饱满，越小越贴近边界线。", "平滑衰减的曲率。1 = 标准 smoothstep；越大 Mask 越饱满，越小越贴近边界线。", "平滑減衰の曲率。1 = 標準 smoothstep。大きいほど Mask は広がり、小さいほど境界線に密着します。", "Curvature of the smooth falloff. 1 = standard smoothstep; larger keeps the mask fuller, smaller hugs the boundary loop." },
            { "二值化阈值（0–1，相对 Max Distance）：归一化距离不超过该值时为 1，超过则为 0。", "二值化阈值（0–1，相对 Max Distance）：归一化距离不超过该值时为 1，超过则为 0。", "二値化しきい値（0–1、Max Distance に対する比率）：正規化距離がこの値以下なら 1、超えると 0。", "Binarization threshold (0-1, relative to Max Distance): normalized distance at or below it is 1, beyond it is 0." },
            { "输出贴图尺寸。", "输出贴图尺寸。", "出力テクスチャのサイズ。", "Output texture size." },
            { "烘焙后向 UV 岛外扩张的像素数，用于避免采样时出现接缝。", "烘焙后向 UV 岛外扩张的像素数，用于避免采样时出现接缝。", "ベイク後に UV アイランドの外側へ広げるピクセル数。サンプリング時の継ぎ目を防ぎます。", "Pixels to expand beyond UV islands after baking, to avoid seams when sampling." },

            // 预览
            { "预览", "预览", "プレビュー", "Preview" },
            { "表面预览", "表面预览", "サーフェスプレビュー", "Surface Preview" },
            { "在 Scene 视图中以红色叠加显示所选材质槽的 Mask 范围。", "在 Scene 视图中以红色叠加显示所选材质槽的 Mask 范围。", "Scene ビューに選択中マテリアルスロットの Mask 範囲を赤く重ねて表示します。", "Overlay the mask range of the selected material slots in red in the Scene view." },
            { "贴图预览", "贴图预览", "テクスチャプレビュー", "Texture Preview" },
            { "在 Scene 视图中叠加显示每个材质槽的 Mask 贴图面板。", "在 Scene 视图中叠加显示每个材质槽的 Mask 贴图面板。", "Scene ビューに各マテリアルスロットの Mask テクスチャパネルを重ねて表示します。", "Overlay a mask texture panel for each material slot in the Scene view." },
            { "刷新预览", "刷新预览", "プレビュー更新", "Refresh Preview" },
            { "关闭贴图预览", "关闭贴图预览", "テクスチャプレビューを閉じる", "Close Texture Preview" },
            { "没有可预览的网格。", "没有可预览的网格。", "プレビューできるメッシュがありません。", "No mesh available to preview." },
            { "无法生成预览贴图，请检查网格与 UV。", "无法生成预览贴图，请检查网格与 UV。", "プレビューテクスチャを生成できません。メッシュと UV を確認してください。", "Failed to generate the preview texture. Check the mesh and UVs." },

            // 选择状态与提示
            { "没有可拾取的对象。", "没有可拾取的对象。", "取得できるオブジェクトがありません。", "No object to pick from." },
            { "左键点击颈部边界线，Enter 确认，Esc 取消。", "左键点击颈部边界线，Enter 确认，Esc 取消。", "左クリックでネック境界線を選択、Enter で確定、Esc でキャンセル。", "Left click the neck boundary loop, Enter to confirm, Esc to cancel." },
            { "未选择任何边界线。", "未选择任何边界线。", "境界線が選択されていません。", "No boundary loop selected." },
            { "已确认 {0} 条循环线。", "已确认 {0} 条循环线。", "{0} 本のループを確定しました。", "Confirmed {0} loop(s)." },
            { "已取消选择。", "已取消选择。", "選択をキャンセルしました。", "Selection cancelled." },
            { "该位置附近没有找到边线，请点击颈部边界附近。", "该位置附近没有找到边线，请点击颈部边界附近。", "この付近に辺が見つかりません。ネックの境界付近をクリックしてください。", "No edge found near this position. Click near the neck boundary." },
            { "无法从该边线推导出循环线。", "无法从该边线推导出循环线。", "この辺からループを特定できません。", "Could not derive a loop from that edge." },
            { "已选中一条循环线（{0} 个顶点）。Shift + 左键可继续追加，Enter 确认。", "已选中一条循环线（{0} 个顶点）。Shift + 左键可继续追加，Enter 确认。", "ループを 1 本選択しました（{0} 頂点）。Shift + 左クリックで追加、Enter で確定。", "Selected one loop ({0} vertices). Shift + Left Click to append more, Enter to confirm." },
            { "蒙皮 Bake 失败：", "蒙皮 Bake 失败：", "スキンメッシュのベイクに失敗：", "Skinned mesh bake failed: " },

            // 保存与导出
            { "保存所选材质槽 Mask", "保存所选材质槽 Mask", "選択スロットの Mask を保存", "Save Mask for Selected Slots" },
            { "请至少勾选一个有效目标材质槽。", "请至少勾选一个有效目标材质槽。", "有効な対象マテリアルスロットを 1 つ以上選択してください。", "Select at least one valid target material slot." },
            { "选择保存目录与文件名前缀（每槽独立 PNG）", "选择保存目录与文件名前缀（每槽独立 PNG）", "保存先とファイル名プレフィックスを選択（スロットごとに個別 PNG）", "Choose the save folder and file name prefix (separate PNG per slot)" },
            { "覆盖已有遮罩？", "覆盖已有遮罩？", "既存の Mask を上書きしますか？", "Overwrite existing masks?" },
            { "以下目标文件中有同名文件：\n", "以下目标文件中有同名文件：\n", "以下の保存先に同名のファイルがあります：\n", "The following target files already exist:\n" },
            { "覆盖", "覆盖", "上書き", "Overwrite" },
            { "取消", "取消", "キャンセル", "Cancel" },
            { "已保存 {0} 张独立材质槽 Mask：\n", "已保存 {0} 张独立材质槽 Mask：\n", "{0} 枚のマテリアルスロット Mask を保存しました：\n", "Saved {0} per-slot mask file(s):\n" },
            { "保存失败（已写入 {0} 张）：{1}", "保存失败（已写入 {0} 张）：{1}", "保存に失敗しました（{0} 枚書き込み済み）：{1}", "Save failed ({0} file(s) written): {1}" },

            // GPU 烘焙与逐槽错误
            { "还没有可用的颈部循环线。", "还没有可用的颈部循环线。", "使用可能なネックループがまだありません。", "No neck loop available yet." },
            { "{0}：目标没有有效网格或 UV0。", "{0}：目标没有有效网格或 UV0。", "{0}：対象に有効なメッシュまたは UV0 がありません。", "{0}: the target has no valid mesh or UV0." },
            { "{0}：{1}", "{0}：{1}", "{0}：{1}", "{0}: {1}" },
            { "{0}：没有可导出的遮罩。", "{0}：没有可导出的遮罩。", "{0}：エクスポートできる Mask がありません。", "{0}: no mask available to export." },
            { "{0}：未烘焙。", "{0}：未烘焙。", "{0}：ベイクされていません。", "{0}: not baked." },
            { "目标槽与输出路径不匹配。", "目标槽与输出路径不匹配。", "対象スロットと出力パスの数が一致しません。", "Target slots and output paths do not match." },
            { "当前设备不支持 GPU Mask 所需的 Compute / RT 格式，Mask 预览与导出不可用。", "当前设备不支持 GPU Mask 所需的 Compute / RT 格式，Mask 预览与导出不可用。", "このデバイスは GPU Mask に必要な Compute / RT フォーマットに未対応のため、Mask のプレビューと書き出しは使用できません。", "This device does not support the Compute / RT formats required for GPU masking; mask preview and export are unavailable." },
            { "找不到 NeckMaskBake.compute，请刷新 Package。", "找不到 NeckMaskBake.compute，请刷新 Package。", "NeckMaskBake.compute が見つかりません。Package を再読み込みしてください。", "NeckMaskBake.compute was not found. Refresh the Package." },
            { "没有可烘焙的目标网格。", "没有可烘焙的目标网格。", "ベイク対象のメッシュがありません。", "No target mesh to bake." },
            { "目标网格没有有效 UV0。", "目标网格没有有效 UV0。", "対象メッシュに有効な UV0 がありません。", "The target mesh has no valid UV0." },
            { "UV 重叠贡献超过 GPU 缓存安全上限，请降低 Texture Size。", "UV 重叠贡献超过 GPU 缓存安全上限，请降低 Texture Size。", "UV の重なりによる寄与が GPU キャッシュの安全上限を超えました。Texture Size を下げてください。", "UV overlap contributions exceed the GPU cache safety limit. Reduce Texture Size." },
            { "UV 烘焙覆盖为空，请确认 UV0 位于 0–1 范围内。", "UV 烘焙覆盖为空，请确认 UV0 位于 0–1 范围内。", "UV のベイク範囲が空です。UV0 が 0–1 の範囲内にあるか確認してください。", "UV bake coverage is empty. Make sure UV0 stays within 0–1." },
            { "UV 三角形分桶超过安全上限，请降低 Texture Size 或检查重叠 UV。", "UV 三角形分桶超过安全上限，请降低 Texture Size 或检查重叠 UV。", "UV 三角形のバケット数が安全上限を超えました。Texture Size を下げるか、UV の重なりを確認してください。", "UV triangle bucketing exceeds the safety limit. Reduce Texture Size or check for overlapping UVs." },
            { "没有有效颈部线段。", "没有有效颈部线段。", "有効なネック線分がありません。", "No valid neck segments." },
            { "无法创建 GPU Mask RenderTexture，请降低 Texture Size。", "无法创建 GPU Mask RenderTexture，请降低 Texture Size。", "GPU Mask 用の RenderTexture を作成できません。Texture Size を下げてください。", "Failed to create the GPU mask RenderTexture. Reduce Texture Size." },
        };

        private static readonly Dictionary<string, string[]> Table = BuildTable();

        private static NeckMaskLanguage _language = ResolveStoredLanguage();

        /// <summary>当前界面语言。赋值时写入 EditorPrefs，跨会话记忆。</summary>
        internal static NeckMaskLanguage Language
        {
            get => _language;
            set
            {
                if (_language == value) return;
                _language = value;
                EditorPrefs.SetInt(LanguageKey, (int)value);
            }
        }

        /// <summary>取键对应的当前语言文案；键缺失时回退为键本身（即中文原文）。</summary>
        internal static string T(string key)
        {
            return Table.TryGetValue(key, out string[] row) ? row[(int)_language] : key;
        }

        /// <summary>取带占位符的文案并格式化；占位符与 <see cref="string.Format(string, object[])"/> 一致。</summary>
        internal static string F(string key, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, T(key), args);
        }

        private static NeckMaskLanguage ResolveStoredLanguage()
        {
            // 语言枚举值即文案表的列索引，越界的旧偏好直接退回中文。
            int stored = EditorPrefs.GetInt(LanguageKey, (int)NeckMaskLanguage.ZH);
            return stored >= 0 && stored < LanguageNames.Length ? (NeckMaskLanguage)stored : NeckMaskLanguage.ZH;
        }

        private static Dictionary<string, string[]> BuildTable()
        {
            var table = new Dictionary<string, string[]>(Rows.GetLength(0), StringComparer.Ordinal);
            for (int i = 0; i < Rows.GetLength(0); i++)
            {
                table[Rows[i, 0]] = new[] { Rows[i, 1], Rows[i, 2], Rows[i, 3] };
            }
            return table;
        }
    }
}
