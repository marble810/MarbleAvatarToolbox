using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace marble810.MarbleAvatarToolbox.PlayModeTools
{
    /// <summary>一次副本剔除执行的结果与日志信息。</summary>
    internal sealed class QuickPlayStripResult
    {
        public readonly Dictionary<string, int> RemovedByToolId = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly List<string> Warnings = new List<string>();

        /// <summary>本会话已验证并实际执行了抑制的工具 ID（运行 hook 只看这个集合）。</summary>
        public readonly List<string> AppliedToolIds = new List<string>();

        /// <summary>因为未安装/无证据而被忽略的已勾选工具 ID（保留偏好，但不激活任何 hook）。</summary>
        public readonly List<string> SkippedNotInstalledToolIds = new List<string>();

        public bool D4RkBlockerInstalled;
        public bool VqtMenuIconStateSuppressionRequested;

        public int TotalRemovedComponents
        {
            get
            {
                var total = 0;
                foreach (var pair in RemovedByToolId) total += pair.Value;
                return total;
            }
        }
    }

    /// <summary>
    /// 在临时副本上执行已登记工具的动作。
    /// 只做组件删除/副本限定控制组件，不删除 Mesh/Material/Texture/Animator 等共享资产。
    ///
    /// 失败闭合：任何「所选工具无法按已验证规则抑制」的情况都返回 false 并给出原因，
    /// 由准备流水线中止会话，绝不静默继续。
    /// </summary>
    internal static class QuickPlayComponentRules
    {
        private const string D4RkSerializedApplyOnUploadPath = "settings.ApplyOnUpload";
        private const string D4RkApplyOnUploadMember = "ApplyOnUpload";

        /// <summary>返回副本上属于该工具、且不在保护集合内的组件。</summary>
        public static List<Component> CollectTargets(GameObject cloneRoot, QuickPlayToolDefinition definition)
        {
            var result = new List<Component>();
            if (cloneRoot == null || definition == null) return result;

            foreach (var component in cloneRoot.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue; // 缺失脚本，保持原样
                var type = component.GetType();
                if (QuickPlayToolRegistry.IsProtected(type)) continue;
                if (!QuickPlayToolRegistry.IsTargetType(definition, type)) continue;
                result.Add(component);
            }

            return result;
        }

        /// <summary>
        /// 执行整个会话的剔除计划。未知历史 ID 与未安装工具按规格忽略；
        /// 已勾选但接口不兼容、或执行失败的工具会返回 false。
        /// </summary>
        public static bool TryApplyStripPlan(
            GameObject cloneRoot,
            QuickPlayPreferenceData snapshot,
            QuickPlayStripResult result,
            out string error)
        {
            error = string.Empty;
            result ??= new QuickPlayStripResult();

            foreach (var toolId in snapshot?.stripToolIds ?? Enumerable.Empty<string>())
            {
                var definition = QuickPlayToolRegistry.Find(toolId);
                if (definition == null)
                {
                    Debug.Log("[QuickPlay] 忽略未登记的历史工具 ID：" + toolId + "。");
                    continue;
                }

                var capability = QuickPlayToolRegistry.Evaluate(definition);
                if (capability.Availability == QuickPlayToolAvailability.NotInstalled)
                {
                    Debug.Log("[QuickPlay] " + definition.Label + " 未安装，跳过剔除。");
                    result.SkippedNotInstalledToolIds.Add(definition.Id);
                    continue;
                }

                if (capability.Availability != QuickPlayToolAvailability.Available)
                {
                    error = definition.Label + "：" + capability.Reason;
                    return false;
                }

                if (!TryApply(cloneRoot, definition, result, out var applyError))
                {
                    error = definition.Label + "：" + applyError;
                    return false;
                }

                result.AppliedToolIds.Add(definition.Id);
            }

            return true;
        }

        /// <summary>在副本上执行一个工具的动作；失败时返回 false。</summary>
        public static bool TryApply(
            GameObject cloneRoot,
            QuickPlayToolDefinition definition,
            QuickPlayStripResult result,
            out string error)
        {
            error = string.Empty;
            if (cloneRoot == null || definition == null)
            {
                error = "clone 或工具定义为空。";
                return false;
            }

            switch (definition.Action)
            {
                case QuickPlayToolAction.RemoveComponents:
                    RemoveTargetComponents(cloneRoot, definition, result);
                    return true;

                case QuickPlayToolAction.SuppressD4RkWithBlocker:
                    return TryInstallD4RkBlocker(cloneRoot, definition, result, out error);

                case QuickPlayToolAction.RemoveComponentsAndSuppressVqtMenuIcons:
                    RemoveTargetComponents(cloneRoot, definition, result);
                    return TryDisableVqtMenuIconCompressionSetting(cloneRoot, result, out error);

                default:
                    error = "未登记的工具动作。";
                    return false;
            }
        }

        /// <summary>删除副本上该工具的组件（包含 inactive 子对象）。</summary>
        public static int RemoveTargetComponents(GameObject cloneRoot, QuickPlayToolDefinition definition, QuickPlayStripResult result)
        {
            var removed = 0;
            foreach (var component in CollectTargets(cloneRoot, definition))
            {
                if (component == null) continue;
                UnityEngine.Object.DestroyImmediate(component);
                removed++;
            }

            if (removed > 0)
            {
                Accumulate(result, definition.Id, removed);
            }

            return removed;
        }

        /// <summary>
        /// d4rk：清理副本上全部 d4rk 优化器，并在副本上保留一个 ApplyOnUpload = false 的阻断组件。
        /// 只在副本上操作，不写 d4rk 全局设置；无法建立有效阻断状态时返回 false。
        /// </summary>
        public static bool TryInstallD4RkBlocker(
            GameObject cloneRoot,
            QuickPlayToolDefinition definition,
            QuickPlayStripResult result,
            out string error)
        {
            error = string.Empty;
            if (cloneRoot == null)
            {
                error = "clone 为空。";
                return false;
            }

            var optimizerType = QuickPlayToolRegistry.FindTypeByFullName(QuickPlayToolRegistry.D4RkOptimizerFullName);
            if (optimizerType == null)
            {
                error = "未找到 d4rk 优化器类型，无法建立副本限定阻断状态。";
                return false;
            }

            RemoveTargetComponents(cloneRoot, definition, result);

            var blocker = cloneRoot.GetComponent(optimizerType);
            if (blocker == null)
            {
                try
                {
                    blocker = cloneRoot.AddComponent(optimizerType);
                }
                catch (Exception exception)
                {
                    error = "无法添加 d4rk 阻断组件：" + exception.Message;
                    return false;
                }
            }

            if (blocker == null)
            {
                error = "d4rk 阻断组件创建失败（AddComponent 返回 null）。";
                return false;
            }

            if (!TrySetApplyOnUploadFalse(blocker, out var failure))
            {
                error = failure;
                return false;
            }

            // 阻断组件必须在活跃层级上，否则 d4rk 会走「无组件自动优化」分支。
            if (!blocker.gameObject.activeInHierarchy)
            {
                error = "d4rk 阻断组件位于非活跃对象上，无法证明自动优化会被阻断。";
                return false;
            }

            if (result != null) result.D4RkBlockerInstalled = true;
            return true;
        }

        /// <summary>把副本上 d4rk 优化器的 ApplyOnUpload 关闭（序列化路径优先，回退到反射成员）。</summary>
        public static bool TrySetApplyOnUploadFalse(Component optimizer, out string failure)
        {
            failure = string.Empty;
            if (optimizer == null)
            {
                failure = "d4rk 阻断组件为空。";
                return false;
            }

            try
            {
                var serialized = new SerializedObject(optimizer);
                var property = serialized.FindProperty(D4RkSerializedApplyOnUploadPath);
                if (property != null && property.propertyType == SerializedPropertyType.Boolean)
                {
                    property.boolValue = false;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    if (ReadBooleanSerializedProperty(optimizer, D4RkSerializedApplyOnUploadPath) == false)
                    {
                        return true; // 写回后读回为 false，确认生效
                    }

                    failure = "写入 d4rk ApplyOnUpload=false 后读回仍为 true。";
                    return false;
                }
            }
            catch (Exception exception)
            {
                failure = "写入 d4rk ApplyOnUpload（序列化路径）失败：" + exception.Message;
            }

            try
            {
                var type = optimizer.GetType();
                var field = type.GetField(D4RkApplyOnUploadMember,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(bool))
                {
                    field.SetValue(optimizer, false);
                    if (!(bool)field.GetValue(optimizer))
                    {
                        failure = string.Empty;
                        return true;
                    }

                    failure = "反射写入 d4rk ApplyOnUpload=false 后读回仍为 true。";
                    return false;
                }

                var property = type.GetProperty(D4RkApplyOnUploadMember,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.PropertyType == typeof(bool) && property.CanWrite)
                {
                    property.SetValue(optimizer, false);
                    if (!(bool)property.GetValue(optimizer))
                    {
                        failure = string.Empty;
                        return true;
                    }

                    failure = "反射写入 d4rk ApplyOnUpload=false 后读回仍为 true。";
                    return false;
                }
            }
            catch (Exception exception)
            {
                failure = "写入 d4rk ApplyOnUpload（反射）失败：" + exception.Message;
                return false;
            }

            failure = "未找到可写入的 d4rk ApplyOnUpload 成员，接口与已核验版本不一致。";
            return false;
        }

        /// <summary>
        /// VQT：把副本上 AvatarConverterSettings 的菜单图标压缩开关关闭。
        /// 组件保留（材质转换等仍生效）；存在组件却无法关闭时返回 false。
        /// </summary>
        public static bool TryDisableVqtMenuIconCompressionSetting(
            GameObject cloneRoot,
            QuickPlayStripResult result,
            out string error)
        {
            error = string.Empty;
            var settingsType = QuickPlayToolRegistry.FindTypeByFullName(QuickPlayToolRegistry.VqtAvatarConverterSettingsFullName);
            if (settingsType == null || cloneRoot == null)
            {
                // 没有该配置类型就没有可关闭的图标压缩来源；BuildContext 状态仍由 NDMF 步骤抑制。
                return true;
            }

            var changed = false;
            foreach (var component in cloneRoot.GetComponentsInChildren(settingsType, true))
            {
                if (component == null) continue;
                if (!TrySetBooleanSerializedField(component, QuickPlayToolRegistry.VqtCompressMenuIconsFieldName, false))
                {
                    error = "无法把副本上的 VQT 菜单图标压缩设置关闭。";
                    return false;
                }

                changed = true;
            }

            if (changed && result != null)
            {
                result.VqtMenuIconStateSuppressionRequested = true;
            }

            return true;
        }

        private static bool TrySetBooleanSerializedField(Component component, string fieldName, bool value)
        {
            try
            {
                var serialized = new SerializedObject(component);
                var property = serialized.FindProperty(fieldName);
                if (property == null || property.propertyType != SerializedPropertyType.Boolean)
                {
                    return false;
                }

                property.boolValue = value;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var verify = new SerializedObject(component).FindProperty(fieldName);
                return verify != null && verify.boolValue == value;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool? ReadBooleanSerializedProperty(Component component, string path)
        {
            try
            {
                var property = new SerializedObject(component).FindProperty(path);
                if (property == null || property.propertyType != SerializedPropertyType.Boolean) return null;
                return property.boolValue;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void Accumulate(QuickPlayStripResult result, string toolId, int removed)
        {
            if (result == null) return;
            if (!result.RemovedByToolId.TryGetValue(toolId, out var current)) current = 0;
            result.RemovedByToolId[toolId] = current + removed;
        }

        /// <summary>调试/测试辅助：列出副本上所有未受保护、且能匹配任一已登记工具的组件。</summary>
        internal static List<Component> CollectAllRegisteredTargets(GameObject cloneRoot)
        {
            var result = new List<Component>();
            if (cloneRoot == null) return result;

            foreach (var component in cloneRoot.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var type = component.GetType();
                if (QuickPlayToolRegistry.IsProtected(type)) continue;
                if (QuickPlayToolRegistry.Tools.Any(tool => QuickPlayToolRegistry.IsTargetType(tool, type)))
                {
                    result.Add(component);
                }
            }

            return result;
        }
    }
}
