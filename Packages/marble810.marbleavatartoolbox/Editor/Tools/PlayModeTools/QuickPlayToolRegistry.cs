using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace marble810.MarbleAvatarToolbox.PlayModeTools
{
    /// <summary>
    /// QuickPlay 对某个登记工具采取的动作类型。
    /// </summary>
    internal enum QuickPlayToolAction
    {
        /// <summary>仅在临时副本上删除该工具的配置组件。</summary>
        RemoveComponents,

        /// <summary>删除重复配置并在副本上保留一个阻断用 d4rk 控制组件（ApplyOnUpload = false）。</summary>
        SuppressD4RkWithBlocker,

        /// <summary>删除菜单图标配置组件，并在目标 BuildContext 上抑制转换派生的图标压缩状态。</summary>
        RemoveComponentsAndSuppressVqtMenuIcons,
    }

    /// <summary>工具在当前工程中的可用性。</summary>
    internal enum QuickPlayToolAvailability
    {
        /// <summary>没有任何安装证据（类型/程序集）。</summary>
        NotInstalled,

        /// <summary>类型与所需接口均已加载，且接口与已核验规则一致。</summary>
        Available,

        /// <summary>有安装证据但接口/来源/版本与已核验规则不一致；被勾选时必须拒绝启动。</summary>
        Incompatible,
    }

    /// <summary>登记表中的一个工具定义。</summary>
    internal sealed class QuickPlayToolDefinition
    {
        public QuickPlayToolDefinition(
            string id,
            string label,
            string tooltip,
            QuickPlayToolAction action,
            bool defaultEnabled,
            string[] exactTypeFullNames,
            string[] baseTypeFullNames,
            string[] assemblyPrefixes,
            string[] evidenceTypeFullNames = null,
            string[] assemblyEvidencePrefixes = null)
        {
            Id = id;
            Label = label;
            Tooltip = tooltip;
            Action = action;
            DefaultEnabled = defaultEnabled;
            ExactTypeFullNames = exactTypeFullNames ?? Array.Empty<string>();
            BaseTypeFullNames = baseTypeFullNames ?? Array.Empty<string>();
            AssemblyPrefixes = assemblyPrefixes ?? Array.Empty<string>();
            EvidenceTypeFullNames = evidenceTypeFullNames ?? Array.Empty<string>();
            AssemblyEvidencePrefixes = assemblyEvidencePrefixes ?? Array.Empty<string>();
        }

        /// <summary>持久化使用的稳定 ID，绝不随显示名变化。</summary>
        public string Id { get; }

        public string Label { get; }

        public string Tooltip { get; }

        public QuickPlayToolAction Action { get; }

        /// <summary>首次使用（无已保存配置）时的默认勾选状态。</summary>
        public bool DefaultEnabled { get; }

        /// <summary>准确匹配的组件类型全名。</summary>
        public string[] ExactTypeFullNames { get; }

        /// <summary>用于识别整个组件系列的已知基类全名（必须同时满足程序集来源）。</summary>
        public string[] BaseTypeFullNames { get; }

        /// <summary>允许的声明程序集名前缀（大小写不敏感）。</summary>
        public string[] AssemblyPrefixes { get; }

        /// <summary>能证明该工具已安装的独立类型（用于区分「未安装」与「已安装但接口不匹配」）。</summary>
        public string[] EvidenceTypeFullNames { get; }

        /// <summary>能证明该工具已安装的独立程序集前缀（不得使用 Assembly-CSharp 等通用程序集）。</summary>
        public string[] AssemblyEvidencePrefixes { get; }

        public bool AcceptsAssembly(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName)) return false;
            foreach (var prefix in AssemblyPrefixes)
            {
                if (assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }

    /// <summary>一次可用性/兼容性检测结果。</summary>
    internal sealed class QuickPlayToolCapability
    {
        public QuickPlayToolCapability(
            QuickPlayToolDefinition definition,
            QuickPlayToolAvailability availability,
            string reason,
            IReadOnlyList<Type> resolvedTypes)
        {
            Definition = definition;
            Availability = availability;
            Reason = reason ?? string.Empty;
            ResolvedTypes = resolvedTypes ?? Array.Empty<Type>();
        }

        public QuickPlayToolDefinition Definition { get; }

        public QuickPlayToolAvailability Availability { get; }

        public string Reason { get; }

        public IReadOnlyList<Type> ResolvedTypes { get; }

        public bool Usable => Availability == QuickPlayToolAvailability.Available;
    }

    /// <summary>
    /// QuickPlay 的固定工具登记表。
    /// 这里集中稳定 ID、显示名、准确类型全名/基类、程序集来源、安装证据与动作；GUI 与执行逻辑共用同一份定义。
    /// 不引用任何第三方目标程序集，只依据可选类型元数据（反射）读取。
    /// </summary>
    internal static class QuickPlayToolRegistry
    {
        public const string VrcFuryId = "vrcfury";
        public const string AaoId = "aao";
        public const string D4RkId = "d4rk-avatar-optimizer";
        public const string MeshiaId = "meshia";
        public const string LacId = "lac";
        public const string OverallId = "overall-mesh-simplifier";
        public const string MantisId = "mantis-ndmf";
        public const string LilId = "lil-ndmf-mesh-simplifier";
        public const string TttId = "ttt-optimization";
        public const string VqtId = "vqt-menu-icons";

        // 需要由其它类型引用的内部全名（能力检测用）。
        internal const string D4RkOptimizerFullName = "d4rkpl4y3r.AvatarOptimizer.d4rkAvatarOptimizer";
        internal const string D4RkBuildHookFullName = "d4rkpl4y3r.AvatarOptimizer.AvatarBuildHook";
        internal const string D4RkSettingsTypeFullName = "d4rkpl4y3r.AvatarOptimizer.AvatarOptimizerSettings";
        internal const string D4RkApplyOnUploadFieldName = "ApplyOnUpload";
        internal const string D4RkSettingsFieldName = "settings";
        internal const string VqtMenuIconResizerFullName = "KRT.VRCQuestTools.Components.MenuIconResizer";
        internal const string VqtNdmfStateFullName = "KRT.VRCQuestTools.Ndmf.NdmfState";
        internal const string VqtMenuIconPassFullName = "KRT.VRCQuestTools.Ndmf.MenuIconResizerPass";
        internal const string VqtAvatarConverterSettingsFullName = "KRT.VRCQuestTools.Components.AvatarConverterSettings";
        internal const string VqtCompressMenuIconsFieldName = "compressExpressionsMenuIcons";

        private const string AaoTagComponentFullName = "Anatawa12.AvatarOptimizer.AvatarTagComponent";
        private const string VrcFuryComponentBaseFullName = "VF.Component.VRCFuryComponent";
        private const string VrcFuryPlayComponentBaseFullName = "VF.Component.VRCFuryPlayComponent";
        private const string MantisPluginFullName = "MantisLODEditor.ndmf.MantisLODEditorNDMF";
        private const string NdmfPassOpenGenericFullName = "nadena.dev.ndmf.Pass`1";

        private static readonly QuickPlayToolDefinition[] ToolList =
        {
            new QuickPlayToolDefinition(
                VrcFuryId,
                "VRCFury",
                "剔除 VRCFury 及其 Haptic 等标记组件；VRCFury 自身的构建功能也会被跳过，预览结果不等同于最终上传。",
                QuickPlayToolAction.RemoveComponents,
                defaultEnabled: true,
                exactTypeFullNames: new[]
                {
                    "VF.Model.VRCFury",
                    "VF.Model.VRCFuryDebugInfo",
                    "VF.Model.VRCFuryTest",
                    "VF.Component.VRCFuryGlobalCollider",
                    "VF.Component.VRCFuryHapticPlug",
                    "VF.Component.VRCFuryHapticSocket",
                    "VF.Component.VRCFuryHapticTouchReceiver",
                    "VF.Component.VRCFuryHapticTouchSender",
                },
                baseTypeFullNames: new[] { VrcFuryComponentBaseFullName, VrcFuryPlayComponentBaseFullName },
                assemblyPrefixes: new[] { "VRCFury" },
                assemblyEvidencePrefixes: new[] { "VRCFury" }),

            new QuickPlayToolDefinition(
                AaoId,
                "AAO (Avatar Optimizer)",
                "剔除 AAO 全系列标记组件（Trace And Optimize、单体组件、旧版/隐藏标记）。同时跳过删面、BlendShape 重命名、MakeChildren、网格合并等优化，预览可能明显不同。",
                QuickPlayToolAction.RemoveComponents,
                defaultEnabled: false,
                exactTypeFullNames: Array.Empty<string>(),
                baseTypeFullNames: new[] { AaoTagComponentFullName },
                assemblyPrefixes: new[] { "com.anatawa12.avatar-optimizer" },
                assemblyEvidencePrefixes: new[] { "com.anatawa12.avatar-optimizer" }),

            new QuickPlayToolDefinition(
                D4RkId,
                "d4rk Avatar Optimizer",
                "仅在临时副本上阻止 d4rk 自动优化，包括无配置组件时的默认优化；不修改全局设置。",
                QuickPlayToolAction.SuppressD4RkWithBlocker,
                defaultEnabled: false,
                exactTypeFullNames: new[] { D4RkOptimizerFullName },
                baseTypeFullNames: Array.Empty<string>(),
                assemblyPrefixes: new[] { "d4rk", "Assembly-CSharp", "Assembly-CSharp-Editor" },
                evidenceTypeFullNames: new[] { D4RkBuildHookFullName, D4RkSettingsTypeFullName }),

            new QuickPlayToolDefinition(
                MeshiaId,
                "Meshia Mesh Simplification",
                "剔除 Meshia 单网格与 Cascading 减面配置组件；不删除原网格或其他工具的配置。",
                QuickPlayToolAction.RemoveComponents,
                defaultEnabled: false,
                exactTypeFullNames: new[]
                {
                    "Meshia.MeshSimplification.Ndmf.MeshiaMeshSimplifier",
                    "Meshia.MeshSimplification.Ndmf.MeshiaCascadingAvatarMeshSimplifier",
                },
                baseTypeFullNames: Array.Empty<string>(),
                assemblyPrefixes: new[] { "Meshia.MeshSimplification" },
                assemblyEvidencePrefixes: new[] { "Meshia.MeshSimplification" }),

            new QuickPlayToolDefinition(
                LacId,
                "Avatar Compressor (LAC)",
                "剔除 LAC 的 TextureCompressor 配置组件；保留输入纹理、材质与动画引用。",
                QuickPlayToolAction.RemoveComponents,
                defaultEnabled: false,
                exactTypeFullNames: new[] { "dev.limitex.avatar.compressor.TextureCompressor" },
                baseTypeFullNames: Array.Empty<string>(),
                assemblyPrefixes: new[] { "dev.limitex.avatar-compressor" },
                assemblyEvidencePrefixes: new[] { "dev.limitex.avatar-compressor" }),

            new QuickPlayToolDefinition(
                OverallId,
                "Overall NDMF Mesh Simplifier",
                "剔除 Overall NDMF Mesh Simplifier 配置组件；不删除原网格。",
                QuickPlayToolAction.RemoveComponents,
                defaultEnabled: false,
                exactTypeFullNames: new[] { "com.aoyon.OverallNDMFMeshSimplifier.OverallNdmfMeshSimplifier" },
                baseTypeFullNames: Array.Empty<string>(),
                assemblyPrefixes: new[] { "com.aoyon.overall-ndmf-mesh-simplifier" },
                assemblyEvidencePrefixes: new[] { "com.aoyon.overall-ndmf-mesh-simplifier" }),

            new QuickPlayToolDefinition(
                MantisId,
                "NDMF Mantis LOD Editor",
                "剔除 NDMF Mantis LOD Editor 组件；不影响原版 Mantis 工具、原网格与工程资产。",
                QuickPlayToolAction.RemoveComponents,
                defaultEnabled: false,
                exactTypeFullNames: new[] { "MantisLODEditor.ndmf.NDMFMantisLODEditor" },
                baseTypeFullNames: Array.Empty<string>(),
                assemblyPrefixes: new[] { "MantisLODEditor", "Assembly-CSharp", "Assembly-CSharp-Editor" },
                evidenceTypeFullNames: new[] { MantisPluginFullName },
                assemblyEvidencePrefixes: new[] { "MantisLODEditor" }),

            new QuickPlayToolDefinition(
                LilId,
                "lilNDMFMeshSimplifier（旧版）",
                "剔除旧版 lilNDMFMeshSimplifier 组件；不删除原网格。",
                QuickPlayToolAction.RemoveComponents,
                defaultEnabled: false,
                exactTypeFullNames: new[] { "jp.lilxyzw.ndmfmeshsimplifier.runtime.NDMFMeshSimplifier" },
                baseTypeFullNames: Array.Empty<string>(),
                assemblyPrefixes: new[] { "jp.lilxyzw.ndmfmeshsimplifier" },
                assemblyEvidencePrefixes: new[] { "jp.lilxyzw.ndmfmeshsimplifier" }),

            new QuickPlayToolDefinition(
                TttId,
                "TexTransTool（仅贴图优化）",
                "仅剔除 TexTransTool 的 AtlasTexture 与 TextureConfigurator；保留贴花、PSD、颜色/纹理合成、作用域与其他未登记行为。",
                QuickPlayToolAction.RemoveComponents,
                defaultEnabled: false,
                exactTypeFullNames: new[]
                {
                    "net.rs64.TexTransTool.TextureAtlas.AtlasTexture",
                    "net.rs64.TexTransTool.TextureConfigurator",
                },
                baseTypeFullNames: Array.Empty<string>(),
                assemblyPrefixes: new[] { "net.rs64.tex-trans-tool" },
                assemblyEvidencePrefixes: new[] { "net.rs64.tex-trans-tool" }),

            new QuickPlayToolDefinition(
                VqtId,
                "VRCQuestTools（仅菜单图标优化）",
                "仅抑制 VRCQuestTools 的菜单图标尺寸/压缩优化；保留材质转换、平台过滤、NetworkID 与其他平台功能。",
                QuickPlayToolAction.RemoveComponentsAndSuppressVqtMenuIcons,
                defaultEnabled: false,
                exactTypeFullNames: new[] { VqtMenuIconResizerFullName },
                baseTypeFullNames: Array.Empty<string>(),
                assemblyPrefixes: new[] { "VRCQuestTools" },
                assemblyEvidencePrefixes: new[] { "VRCQuestTools" }),
        };

        private static readonly Dictionary<string, QuickPlayToolDefinition> ToolsById = BuildToolsById();
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly object TypeCacheLock = new object();

        /// <summary>固定十项工具，顺序即 GUI 顺序。</summary>
        public static IReadOnlyList<QuickPlayToolDefinition> Tools => ToolList;

        public static QuickPlayToolDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return ToolsById.TryGetValue(id, out var definition) ? definition : null;
        }

        public static bool IsKnownId(string id)
        {
            return Find(id) != null;
        }

        /// <summary>
        /// 解析准确类型全名。只按全名精确匹配（含 namespace），不做名称包含或通配。
        /// 结果缓存到当前 Domain；安装/卸载 Package 会触发 Domain Reload 从而刷新缓存。
        /// </summary>
        public static Type FindTypeByFullName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;

            lock (TypeCacheLock)
            {
                if (TypeCache.TryGetValue(fullName, out var cached)) return cached;
            }

            Type resolved = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                resolved = FindTypeInAssembly(assembly, fullName);
                if (resolved != null) break;
            }

            lock (TypeCacheLock)
            {
                TypeCache[fullName] = resolved;
            }

            return resolved;
        }

        /// <summary>是否存在名字以指定前缀开头的已加载程序集（安装证据，不参与目标命中）。</summary>
        public static bool HasLoadedAssemblyWithPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return false;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = SafeAssemblyName(assembly);
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        /// <summary>
        /// 依据准确类型/基类与程序集来源判断组件是否属于该工具。
        /// </summary>
        public static bool IsTargetType(QuickPlayToolDefinition definition, Type type)
        {
            if (definition == null || type == null) return false;
            if (!definition.AcceptsAssembly(SafeAssemblyName(type.Assembly))) return false;

            var fullName = type.FullName;
            if (!string.IsNullOrEmpty(fullName))
            {
                foreach (var candidate in definition.ExactTypeFullNames)
                {
                    if (string.Equals(candidate, fullName, StringComparison.Ordinal)) return true;
                }
            }

            foreach (var baseName in definition.BaseTypeFullNames)
            {
                if (HasBaseTypeFromSource(type, baseName, definition)) return true;
            }

            return false;
        }

        /// <summary>
        /// 保护集合：Transform、Unity 运行组件、VRChat SDK、NDMF、Modular Avatar、本工具包自身与其他非目标类型。
        /// 先检查保护集合，再执行剔除规则。
        /// </summary>
        public static bool IsProtected(Type type)
        {
            if (type == null) return true;
            if (typeof(Transform).IsAssignableFrom(type)) return true;

            var assemblyName = SafeAssemblyName(type.Assembly);
            if (IsProtectedAssembly(assemblyName)) return true;

            var ns = type.Namespace ?? string.Empty;
            if (ns.StartsWith("UnityEngine", StringComparison.Ordinal)) return true;
            if (ns.StartsWith("UnityEditor", StringComparison.Ordinal)) return true;
            if (ns.StartsWith("System", StringComparison.Ordinal)) return true;
            if (ns.StartsWith("VRC.", StringComparison.Ordinal)) return true;
            if (ns.StartsWith("VRC.SDK", StringComparison.Ordinal)) return true;
            if (ns.StartsWith("nadena.dev.modular_avatar", StringComparison.Ordinal)) return true;
            if (ns.StartsWith("nadena.dev.ndmf", StringComparison.Ordinal)) return true;
            if (ns.StartsWith("marble810.MarbleAvatarToolbox", StringComparison.Ordinal)) return true;
            if (ns.StartsWith("Microsoft", StringComparison.Ordinal)) return true;
            if (ns.StartsWith("JetBrains", StringComparison.Ordinal)) return true;
            if (ns.StartsWith("Newtonsoft", StringComparison.Ordinal)) return true;

            return false;
        }

        /// <summary>
        /// 判定工具可用性：
        /// - 无安装证据 → NotInstalled（规格允许忽略）；
        /// - 有安装证据但登记类型缺失或接口/版本与已核验规则不一致 → Incompatible（被勾选时必须拒绝）。
        /// </summary>
        public static QuickPlayToolCapability Evaluate(QuickPlayToolDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            var resolved = new List<Type>();
            foreach (var fullName in definition.ExactTypeFullNames)
            {
                var type = FindTypeByFullName(fullName);
                if (type != null) resolved.Add(type);
            }

            foreach (var baseName in definition.BaseTypeFullNames)
            {
                var baseType = FindTypeByFullName(baseName);
                if (baseType == null) continue;

                foreach (var type in FindTypesInBaseAssemblies(baseType, definition))
                {
                    if (!resolved.Contains(type)) resolved.Add(type);
                }
            }

            var installationEvidence = DescribeInstallationEvidence(definition, resolved);

            if (resolved.Count == 0)
            {
                if (installationEvidence.Count > 0)
                {
                    return new QuickPlayToolCapability(
                        definition,
                        QuickPlayToolAvailability.Incompatible,
                        "检测到该工具已安装（" + string.Join("；", installationEvidence) +
                        "），但没有加载到登记的目标组件类型，接口与已核验版本不一致，QuickPlay 保守拒绝该选项。",
                        resolved);
                }

                return new QuickPlayToolCapability(
                    definition,
                    QuickPlayToolAvailability.NotInstalled,
                    "未检测到安装证据：" + string.Join("、", definition.ExactTypeFullNames.Length > 0
                        ? definition.ExactTypeFullNames
                        : definition.BaseTypeFullNames),
                    resolved);
            }

            var problems = new List<string>();

            foreach (var type in resolved)
            {
                if (!definition.AcceptsAssembly(SafeAssemblyName(type.Assembly)))
                {
                    problems.Add($"{type.FullName} 来自未登记程序集 {SafeAssemblyName(type.Assembly)}");
                }

                if (!typeof(MonoBehaviour).IsAssignableFrom(type))
                {
                    problems.Add($"{type.FullName} 不是 MonoBehaviour");
                }
            }

            switch (definition.Action)
            {
                case QuickPlayToolAction.SuppressD4RkWithBlocker:
                    ValidateD4Rk(problems);
                    break;
                case QuickPlayToolAction.RemoveComponentsAndSuppressVqtMenuIcons:
                    ValidateVqt(problems);
                    break;
            }

            if (problems.Count > 0)
            {
                return new QuickPlayToolCapability(
                    definition,
                    QuickPlayToolAvailability.Incompatible,
                    string.Join("；", problems) + "（与已核验版本不一致，QuickPlay 保守拒绝该选项）",
                    resolved);
            }

            return new QuickPlayToolCapability(definition, QuickPlayToolAvailability.Available, string.Empty, resolved);
        }

        internal static List<string> DescribeInstallationEvidence(QuickPlayToolDefinition definition, List<Type> resolvedTargets = null)
        {
            var evidence = new List<string>();

            if (resolvedTargets != null && resolvedTargets.Count > 0)
            {
                evidence.Add("目标类型 " + resolvedTargets[0].FullName);
            }

            foreach (var typeName in definition.EvidenceTypeFullNames)
            {
                if (FindTypeByFullName(typeName) != null)
                {
                    evidence.Add("关联类型 " + typeName);
                }
            }

            foreach (var prefix in definition.AssemblyEvidencePrefixes)
            {
                if (HasLoadedAssemblyWithPrefix(prefix))
                {
                    evidence.Add("程序集 " + prefix + "*");
                }
            }

            return evidence;
        }

        /// <summary>
        /// d4rk 能力校验：组件必须提供可写的 ApplyOnUpload 序列化路径；SDK 回调必须是
        /// 已核验的 AvatarBuildHook，且回调顺序只能是已核验的两种取值；自动补组件分支必须存在。
        /// 任何一项不满足都视为「已安装但接口不兼容」，由预检拒绝。
        /// </summary>
        private static void ValidateD4Rk(List<string> problems)
        {
            var optimizer = FindTypeByFullName(D4RkOptimizerFullName);
            if (optimizer == null)
            {
                problems.Add("未找到 d4rk 优化器类型");
            }
            else if (!HasSerializedApplyOnUploadPath(optimizer))
            {
                problems.Add($"{D4RkOptimizerFullName} 未提供可写的 {D4RkSettingsFieldName}.{D4RkApplyOnUploadFieldName} 序列化路径");
            }

            var hook = FindTypeByFullName(D4RkBuildHookFullName);
            if (hook == null)
            {
                problems.Add("未找到已核验的 d4rk SDK 回调 " + D4RkBuildHookFullName);
            }
            else
            {
                if (!typeof(IVRCSDKPreprocessAvatarCallback).IsAssignableFrom(hook))
                {
                    problems.Add(D4RkBuildHookFullName + " 不再是 SDK preprocess 回调");
                }
                else if (!TryReadCallbackOrder(hook, out var order))
                {
                    problems.Add(D4RkBuildHookFullName + " 的 callbackOrder 无法读取");
                }
                else if (order != -15 && order != -1025)
                {
                    problems.Add(D4RkBuildHookFullName + " 的 callbackOrder=" + order +
                                 " 与已核验取值（-15 或 -1025）不一致，无法保证阻断状态在优化分支前生效");
                }
            }

            var settings = FindTypeByFullName(D4RkSettingsTypeFullName);
            if (settings == null)
            {
                problems.Add("未找到 d4rk 全局设置类型 " + D4RkSettingsTypeFullName + "，无法确认「无组件自动优化」分支存在");
            }
            else if (settings.GetProperty("DoOptimizeWithDefaultSettingsWhenNoComponent",
                         BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) == null)
            {
                problems.Add(D4RkSettingsTypeFullName + " 缺少 DoOptimizeWithDefaultSettingsWhenNoComponent，接口与已核验版本不一致");
            }
        }

        /// <summary>
        /// VQT 能力校验：图标组件、状态字段、状态访问 API、转换设置字段，以及用于排序约束的 pass 身份。
        /// </summary>
        private static void ValidateVqt(List<string> problems)
        {
            var resizer = FindTypeByFullName(VqtMenuIconResizerFullName);
            if (resizer == null || !typeof(MonoBehaviour).IsAssignableFrom(resizer))
            {
                problems.Add($"{VqtMenuIconResizerFullName} 不是可用的组件类型");
            }

            var stateType = FindTypeByFullName(VqtNdmfStateFullName);
            if (stateType == null)
            {
                problems.Add($"未找到 {VqtNdmfStateFullName}，无法抑制转换派生的菜单图标压缩状态");
            }
            else
            {
                var field = stateType.GetField(VqtCompressMenuIconsFieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null || field.FieldType != typeof(bool))
                {
                    problems.Add($"{VqtNdmfStateFullName}.{VqtCompressMenuIconsFieldName} 缺失或类型不是 bool");
                }
            }

            if (!HasUsableBuildContextStateAccess(out var accessError))
            {
                problems.Add("NDMF BuildContext 状态访问 API 不可用：" + accessError);
            }

            var settingsType = FindTypeByFullName(VqtAvatarConverterSettingsFullName);
            if (settingsType == null)
            {
                problems.Add("未找到 " + VqtAvatarConverterSettingsFullName + "，无法确认菜单图标压缩状态的来源");
            }
            else
            {
                var field = settingsType.GetField(VqtCompressMenuIconsFieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null || field.FieldType != typeof(bool))
                {
                    problems.Add($"{VqtAvatarConverterSettingsFullName}.{VqtCompressMenuIconsFieldName} 缺失或类型不是 bool");
                }
            }

            var pass = FindTypeByFullName(VqtMenuIconPassFullName);
            if (pass == null)
            {
                problems.Add($"未找到 {VqtMenuIconPassFullName}，无法在图标 pass 前插入目标限定抑制步骤");
            }
            else if (!TryReadPassQualifiedName(pass, out var qualifiedName, out var passError))
            {
                problems.Add("无法确认 " + VqtMenuIconPassFullName + " 的 NDMF pass 身份：" + passError);
            }
            else if (!string.Equals(qualifiedName, VqtMenuIconPassFullName, StringComparison.Ordinal))
            {
                problems.Add("VQT 图标 pass 的 QualifiedName=" + qualifiedName + " 与登记值不一致，排序约束会失效");
            }
        }

        private static bool HasSerializedApplyOnUploadPath(Type optimizerType)
        {
            var settingsField = optimizerType.GetField(D4RkSettingsFieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (settingsField != null)
            {
                var settingsType = settingsField.FieldType;
                var boolField = settingsType?.GetField(D4RkApplyOnUploadFieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (boolField != null && boolField.FieldType == typeof(bool)) return true;
            }

            var directField = optimizerType.GetField(D4RkApplyOnUploadFieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (directField != null && directField.FieldType == typeof(bool)) return true;

            var directProperty = optimizerType.GetProperty(D4RkApplyOnUploadFieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return directProperty != null && directProperty.PropertyType == typeof(bool) && directProperty.CanWrite;
        }

        private static bool TryReadCallbackOrder(Type callbackType, out int order)
        {
            order = 0;
            try
            {
                var instance = Activator.CreateInstance(callbackType, true);
                var callback = instance as IVRCSDKPreprocessAvatarCallback;
                if (callback == null) return false;
                order = callback.callbackOrder;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryReadPassQualifiedName(Type passType, out string qualifiedName, out string error)
        {
            qualifiedName = string.Empty;
            error = string.Empty;

            try
            {
                var baseType = passType.BaseType;
                while (baseType != null)
                {
                    if (baseType.IsGenericType &&
                        baseType.GetGenericTypeDefinition().FullName == NdmfPassOpenGenericFullName)
                    {
                        var instanceProperty = baseType.GetProperty("Instance",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        var instance = instanceProperty?.GetValue(null);
                        if (instance == null)
                        {
                            error = "Pass<T>.Instance 为 null";
                            return false;
                        }

                        var qualified = baseType.GetProperty("QualifiedName",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        qualifiedName = qualified?.GetValue(instance) as string ?? string.Empty;
                        if (string.IsNullOrEmpty(qualifiedName))
                        {
                            error = "QualifiedName 为空";
                            return false;
                        }

                        return true;
                    }

                    baseType = baseType.BaseType;
                }

                error = "不是 NDMF Pass<T> 派生类型";
                return false;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private static bool HasUsableBuildContextStateAccess(out string error)
        {
            error = string.Empty;
            var context = typeof(nadena.dev.ndmf.BuildContext);

            if (context.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Any(method => method.Name == "GetState" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0))
            {
                return true;
            }

            if (context.GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic) != null)
            {
                return true;
            }

            error = "既没有 GetState<T>() 也没有 _state 字段";
            return false;
        }

        private static bool HasBaseTypeFromSource(Type type, string baseFullName, QuickPlayToolDefinition definition)
        {
            var current = type.BaseType;
            while (current != null)
            {
                if (string.Equals(current.FullName, baseFullName, StringComparison.Ordinal) &&
                    definition.AcceptsAssembly(SafeAssemblyName(current.Assembly)))
                {
                    return true;
                }

                current = current.BaseType;
            }

            return false;
        }

        private static IEnumerable<Type> FindTypesInBaseAssemblies(Type baseType, QuickPlayToolDefinition definition)
        {
            var results = new List<Type>();
            foreach (var type in SafeGetTypes(baseType.Assembly))
            {
                if (type == null || type.IsAbstract || type.IsInterface) continue;
                if (!typeof(MonoBehaviour).IsAssignableFrom(type)) continue;
                if (!HasBaseTypeFromSource(type, baseType.FullName, definition)) continue;
                results.Add(type);
            }

            return results;
        }

        private static Type FindTypeInAssembly(Assembly assembly, string fullName)
        {
            if (assembly == null) return null;
            foreach (var type in SafeGetTypes(assembly))
            {
                if (type != null && string.Equals(type.FullName, fullName, StringComparison.Ordinal))
                {
                    return type;
                }
            }

            return null;
        }

        private static Type[] SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                var loaded = new List<Type>();
                foreach (var type in exception.Types)
                {
                    if (type != null) loaded.Add(type);
                }

                return loaded.ToArray();
            }
            catch (Exception)
            {
                return Array.Empty<Type>();
            }
        }

        private static string SafeAssemblyName(Assembly assembly)
        {
            try
            {
                return assembly?.GetName().Name ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static bool IsProtectedAssembly(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName)) return true;

            if (assemblyName.StartsWith("UnityEngine", StringComparison.Ordinal)) return true;
            if (assemblyName.StartsWith("UnityEditor", StringComparison.Ordinal)) return true;
            if (assemblyName.StartsWith("System", StringComparison.Ordinal)) return true;
            if (assemblyName.StartsWith("mscorlib", StringComparison.Ordinal)) return true;
            if (assemblyName.StartsWith("netstandard", StringComparison.Ordinal)) return true;
            if (assemblyName.StartsWith("VRC.", StringComparison.Ordinal)) return true;
            if (assemblyName.StartsWith("VRCSDK", StringComparison.Ordinal)) return true;
            if (assemblyName.StartsWith("VRCCore", StringComparison.Ordinal)) return true;
            if (assemblyName.StartsWith("VRC.Dynamics", StringComparison.Ordinal)) return true;
            if (assemblyName.StartsWith("nadena.dev.modular-avatar", StringComparison.Ordinal)) return true;
            if (assemblyName.StartsWith("nadena.dev.ndmf", StringComparison.Ordinal)) return true;
            if (assemblyName.StartsWith("marbleavatartoolbox", StringComparison.Ordinal)) return true;
            if (assemblyName.StartsWith("marble810", StringComparison.Ordinal)) return true;

            return false;
        }

        private static Dictionary<string, QuickPlayToolDefinition> BuildToolsById()
        {
            var map = new Dictionary<string, QuickPlayToolDefinition>(StringComparer.Ordinal);
            foreach (var tool in ToolList)
            {
                map[tool.Id] = tool;
            }

            return map;
        }
    }
}
