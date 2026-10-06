using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace marble810.MarbleAvatarToolbox.PlayModeTools
{
    /// <summary>
    /// Selection 的完整快照：
    /// - <see cref="DirectObjects"/> / <see cref="DirectActiveObject"/> 是同域直接引用，用于精确恢复（含资产、组件等非 GameObject 对象）；
    /// - <see cref="GlobalObjectIds"/> 是 durable 身份，用于域重载后恢复仍存在且可无损识别的对象；
    /// - <see cref="WasEmpty"/> 区分「真实空集」与「捕获失败」，不得把 null 当作工具所有权证据。
    /// </summary>
    internal sealed class QuickPlaySelectionSnapshot
    {
        /// <summary>同域直接引用（顺序与 Selection.objects 一致）。</summary>
        public readonly List<UnityEngine.Object> DirectObjects = new List<UnityEngine.Object>();

        public UnityEngine.Object DirectActiveObject;

        /// <summary>与 DirectObjects 一一对应的 durable 身份（无法捕获时为 string.Empty）。</summary>
        public readonly List<string> GlobalObjectIds = new List<string>();

        public string ActiveGlobalObjectId = string.Empty;

        /// <summary>捕获时选择集是否真的为空。</summary>
        public bool WasEmpty;

        /// <summary>是否有对象无法生成 durable 身份（重载后该条只能靠同域引用恢复）。</summary>
        public bool HadUncapturableObjects;

        public int Count => DirectObjects.Count;

        public bool IsEmpty => DirectObjects.Count == 0 && GlobalObjectIds.Count == 0;
    }

    /// <summary>
    /// 对象身份与 Selection 的 durable 支持。
    /// 身份只用 Unity 的 GlobalObjectId（或 clone 标记里的直接引用），
    /// 绝不使用名字、层级路径或兄弟序号推断对象。
    /// </summary>
    internal static class QuickPlayObjectIdentity
    {
        /// <summary>捕获对象身份；无法可靠往返时返回 false（调用方不得退化为按名字匹配）。</summary>
        public static bool TryCaptureId(UnityEngine.Object gameObject, out string globalObjectId, out string error)
        {
            globalObjectId = string.Empty;
            error = string.Empty;

            if (gameObject == null)
            {
                error = "对象为空。";
                return false;
            }

            try
            {
                var id = GlobalObjectId.GetGlobalObjectIdSlow(gameObject);
                var text = id.ToString();
                if (string.IsNullOrEmpty(text) || !GlobalObjectId.TryParse(text, out var parsed))
                {
                    error = "无法为该对象生成可往返的 GlobalObjectId（例如未保存的临时场景对象）：" + text;
                    return false;
                }

                var resolved = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed);
                if (!ReferenceEquals(resolved, gameObject))
                {
                    error = "GlobalObjectId 往返解析结果与目标对象不一致。";
                    return false;
                }

                globalObjectId = text;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        /// <summary>解析 durable 身份；身份失效（对象已被删除/替换）时返回 null，由调用方告警并跳过。</summary>
        public static UnityEngine.Object TryResolve(string globalObjectId, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrEmpty(globalObjectId)) return null;

            try
            {
                if (!GlobalObjectId.TryParse(globalObjectId, out var parsed))
                {
                    error = "GlobalObjectId 无法解析（可能是旧格式或记录损坏）。";
                    return null;
                }

                var resolved = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed);
                if (resolved == null)
                {
                    error = "原对象已不存在（身份失效）。";
                    return null;
                }

                return resolved;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return null;
            }
        }

        /// <summary>捕获当前完整 Selection（Selection.objects + activeObject，含资产/组件）。</summary>
        public static QuickPlaySelectionSnapshot CaptureSelection()
        {
            var snapshot = new QuickPlaySelectionSnapshot();

            try
            {
                var objects = Selection.objects ?? Array.Empty<UnityEngine.Object>();
                foreach (var obj in objects)
                {
                    if (obj == null)
                    {
                        // 选择集中出现已销毁对象：保留占位并标记为不可无损捕获。
                        snapshot.DirectObjects.Add(null);
                        snapshot.GlobalObjectIds.Add(string.Empty);
                        snapshot.HadUncapturableObjects = true;
                        continue;
                    }

                    snapshot.DirectObjects.Add(obj);
                    if (TryCaptureId(obj, out var id, out _))
                    {
                        snapshot.GlobalObjectIds.Add(id);
                    }
                    else
                    {
                        // 同域仍可用直接引用精确恢复；重载后无法识别该条。
                        snapshot.GlobalObjectIds.Add(string.Empty);
                        snapshot.HadUncapturableObjects = true;
                    }
                }

                var active = Selection.activeObject;
                snapshot.DirectActiveObject = active;
                if (active != null && TryCaptureId(active, out var activeId, out _))
                {
                    snapshot.ActiveGlobalObjectId = activeId;
                }

                snapshot.WasEmpty = objects.Length == 0 && active == null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[QuickPlay] 捕获 Selection 失败：" + exception.Message);
                snapshot.HadUncapturableObjects = true;
            }

            return snapshot;
        }

        /// <summary>
        /// 恢复 Selection 到快照状态：
        /// - 真实空集恢复为空集（不回退选择原对象）；
        /// - 优先使用同域直接引用，失效时回退 durable 身份；
        /// - 只恢复仍能解析的对象，其余记录告警。
        /// </summary>
        public static int RestoreSelection(QuickPlaySelectionSnapshot snapshot, out List<string> warnings)
        {
            warnings = new List<string>();
            if (snapshot == null) return 0;

            var restored = new List<UnityEngine.Object>();
            for (var i = 0; i < snapshot.DirectObjects.Count; i++)
            {
                var direct = snapshot.DirectObjects[i];
                if (direct != null)
                {
                    restored.Add(direct);
                    continue;
                }

                var id = i < snapshot.GlobalObjectIds.Count ? snapshot.GlobalObjectIds[i] : string.Empty;
                if (string.IsNullOrEmpty(id)) continue;

                var resolved = TryResolve(id, out var error);
                if (resolved == null)
                {
                    warnings.Add("Selection 中有对象无法恢复：" + error);
                    continue;
                }

                restored.Add(resolved);
            }

            UnityEngine.Object active = snapshot.DirectActiveObject;
            if (active == null && !string.IsNullOrEmpty(snapshot.ActiveGlobalObjectId))
            {
                active = TryResolve(snapshot.ActiveGlobalObjectId, out _);
            }

            if (snapshot.WasEmpty && restored.Count == 0)
            {
                // 明确恢复为空集：不回退到任何对象。
                Selection.objects = Array.Empty<UnityEngine.Object>();
                Selection.activeObject = null;
                return 0;
            }

            // Unity 的 Selection 语义（本机实测）：
            // - 赋值 activeObject 会把选择折叠成单选；
            // - 赋值 objects 会把 activeObject 设为数组首元素。
            // 因此把期望的 active 对象排到首位，才能同时恢复「多选集合」与「active 对象」。
            var activeObject = active != null && restored.Contains(active) ? active : restored.FirstOrDefault();
            var ordered = new List<UnityEngine.Object>();
            if (activeObject != null) ordered.Add(activeObject);
            foreach (var obj in restored)
            {
                if (ReferenceEquals(obj, activeObject)) continue;
                ordered.Add(obj);
            }

            Selection.objects = ordered.ToArray();

            if (restored.Count == 0 && !snapshot.WasEmpty)
            {
                warnings.Add("Selection 全部对象都无法恢复（保持当前选择）。");
            }

            return restored.Count;
        }

        /// <summary>
        /// 判断当前 Selection 是否仍等于「本工具设置的选择」。
        /// 只有在此情况下才允许恢复旧选择，避免覆盖用户在会话期间的新选择（含资产/组件/清空）。
        /// </summary>
        public static bool CurrentSelectionMatches(IReadOnlyList<UnityEngine.Object> expected, UnityEngine.Object expectedActive)
        {
            var current = Selection.objects ?? Array.Empty<UnityEngine.Object>();

            if (expected == null)
            {
                return current.Length == 0 && Selection.activeObject == null;
            }

            if (current.Length != expected.Count) return false;

            for (var i = 0; i < current.Length; i++)
            {
                if (!ReferenceEquals(current[i], expected[i])) return false;
            }

            return ReferenceEquals(Selection.activeObject, expectedActive);
        }

        /// <summary>
        /// 恢复原对象的 active 状态，并检测用户冲突：
        /// 只有当前值与「本工具写入的值」一致时才写回；否则保留用户修改并报告冲突。
        /// </summary>
        public static bool TryRestoreActiveState(
            GameObject original,
            bool restoreValue,
            bool writtenValue,
            bool weWrote,
            out string conflict)
        {
            conflict = string.Empty;
            if (original == null) return false;

            // 冲突检测必须与「本工具实际写入的值」比较，而不是与恢复目标比较；
            // 否则工具自己把对象设成 false 后，会被误判成用户修改。
            if (weWrote && original.activeSelf != writtenValue)
            {
                conflict = "原对象 active 状态已被用户改为 " + original.activeSelf +
                           "（QuickPlay 写入的是 " + writtenValue + "），已保留用户修改。";
                return false;
            }

            original.SetActive(restoreValue);
            return true;
        }

        /// <summary>仅用于日志的人员可读描述（不参与身份判断）。</summary>
        public static string Describe(GameObject gameObject)
        {
            if (gameObject == null) return "<null>";
            var scenePath = gameObject.scene.IsValid() ? gameObject.scene.path : string.Empty;
            return string.IsNullOrEmpty(scenePath)
                ? gameObject.name + "（未保存场景）"
                : scenePath + "/" + gameObject.name;
        }

        /// <summary>捕获当前所有已加载普通场景中处于干净状态的场景路径。</summary>
        public static List<string> CaptureCleanScenePaths()
        {
            var paths = new List<string>();
            foreach (var scene in QuickPlaySceneEnumerator.LoadedEditableScenes())
            {
                if (scene.isDirty) continue;
                paths.Add(string.IsNullOrEmpty(scene.path) ? string.Empty : scene.path);
            }

            return paths;
        }

        /// <summary>
        /// 找出「开始时干净、现在 dirty」的场景。QuickPlay 不清理 dirty，只报告。
        /// </summary>
        public static List<string> FindNewlyDirtyScenes(IEnumerable<string> cleanScenePathsAtStart)
        {
            var dirty = new List<string>();
            var clean = new HashSet<string>(cleanScenePathsAtStart ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

            foreach (var scene in QuickPlaySceneEnumerator.LoadedEditableScenes())
            {
                if (!scene.isDirty) continue;
                var key = string.IsNullOrEmpty(scene.path) ? string.Empty : scene.path;
                if (!clean.Contains(key)) continue;
                dirty.Add(string.IsNullOrEmpty(scene.path) ? scene.name + "（未保存场景）" : scene.path);
            }

            return dirty;
        }
    }

    /// <summary>已加载的可编辑（非预览）场景枚举，测试与生产共用。</summary>
    internal static class QuickPlaySceneEnumerator
    {
        public static List<UnityEngine.SceneManagement.Scene> LoadedEditableScenes()
        {
            var result = new List<UnityEngine.SceneManagement.Scene>();
            for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                if (EditorSceneManager.IsPreviewScene(scene)) continue;
                result.Add(scene);
            }

            return result;
        }
    }
}
