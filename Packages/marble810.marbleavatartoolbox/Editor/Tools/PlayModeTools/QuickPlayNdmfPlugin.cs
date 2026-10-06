using System;
using System.Collections;
using System.Reflection;
using nadena.dev.ndmf;
using UnityEngine;

[assembly: ExportsPlugin(typeof(marble810.MarbleAvatarToolbox.PlayModeTools.QuickPlayNdmfPlugin))]

namespace marble810.MarbleAvatarToolbox.PlayModeTools
{
    /// <summary>
    /// QuickPlay 的 NDMF 步骤：在 VRCQuestTools 的菜单图标 pass 之前，仅对已登记的 QuickPlay 目标
    /// 关闭该 BuildContext 的菜单图标压缩状态。不全局禁用 VQT，也不影响其他 Avatar。
    /// </summary>
    internal sealed class QuickPlayNdmfPlugin : Plugin<QuickPlayNdmfPlugin>
    {
        /// <summary>QuickPlay 抑制步骤的 NDMF pass key（插件内与测试 sentinel 排序共用）。</summary>
        internal const string VqtSuppressionPassKey = "QuickPlay: suppress VQT menu icon optimization";

        public override string DisplayName => "Marble Avatar Toolbox QuickPlay";

        public override string QualifiedName => "marble810.marbleavatartoolbox.quickplay";

        protected override void Configure()
        {
            InPhase(BuildPhase.Optimizing)
                .Run(VqtSuppressionPassKey, QuickPlayVqtSuppression.Execute)
                .BeforePass(QuickPlayVqtSuppression.MenuIconResizerPassFullName);
        }

        /// <summary>
        /// NDMF 的 BuildContext.RunPass 会捕获 pass 异常并调用插件自己的 OnUnhandledException，
        /// 默认实现只记录日志、随后继续执行后续 passes。
        /// 因此 QuickPlay **仅对自己的抑制失败**向外传播异常，让 RunPass 抛出并中止当前目标的后续 passes；
        /// 其它插件的异常仍交给基类默认处理，不做任何全局干预。
        /// </summary>
        protected override void OnUnhandledException(Exception e)
        {
            if (e is QuickPlaySuppressionFailure)
            {
                throw e;
            }

            base.OnUnhandledException(e);
        }
    }

    /// <summary>
    /// 仅用于标识「QuickPlay 自身抑制失败」的异常类型：
    /// 插件异常处理会把它向外传播，从而中止当前目标构建（不会影响其它插件的错误处理）。
    /// </summary>
    internal sealed class QuickPlaySuppressionFailure : Exception
    {
        public QuickPlaySuppressionFailure(string message) : base(message)
        {
        }
    }

    /// <summary>VQT 菜单图标优化的目标限定抑制。</summary>
    internal static class QuickPlayVqtSuppression
    {
        public const string MenuIconResizerPassFullName = "KRT.VRCQuestTools.Ndmf.MenuIconResizerPass";

        internal static void Execute(BuildContext context)
        {
            if (context == null) return;

            var root = context.AvatarRootObject;
            if (!QuickPlaySession.IsVqtSuppressionTarget(root)) return;

            if (TryDisableCompressionState(context, out var error))
            {
                Debug.Log("[QuickPlay] 已关闭目标副本的 VQT 菜单图标压缩。");
                return;
            }

            // 失败闭合：使用 QuickPlay 专属异常类型，插件异常处理会把它向外传播，
            // 从而在 NDMF 的 pass 循环里立即中止（后续 VQT 图标 pass 与其它 passes 都不会执行）。
            throw new QuickPlaySuppressionFailure(
                "QuickPlay 无法在目标 BuildContext 中关闭 VQT 菜单图标压缩状态（" + error +
                "），已中止本次目标处理以避免静默保留图标优化。");
        }

        /// <summary>
        /// 通过 NDMF 公共状态 API 关闭 VQT 的图标压缩状态（反射访问 internal 状态类型与字段）。
        /// </summary>
        /// <summary>测试用故障注入：非 null 时直接按该错误失败（生产代码不设置）。</summary>
        internal static Func<string> SuppressionFailureInjection;

        internal static bool TryDisableCompressionState(BuildContext context, out string error)
        {
            error = string.Empty;

            if (SuppressionFailureInjection != null)
            {
                error = SuppressionFailureInjection();
                return false;
            }

            if (context == null)
            {
                error = "BuildContext 为空。";
                return false;
            }

            var stateType = QuickPlayToolRegistry.FindTypeByFullName(QuickPlayToolRegistry.VqtNdmfStateFullName);
            if (stateType == null)
            {
                error = "未找到 VQT 的 NdmfState 类型。";
                return false;
            }

            var field = stateType.GetField(
                QuickPlayToolRegistry.VqtCompressMenuIconsFieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(bool))
            {
                error = "VQT NdmfState.compressExpressionsMenuIcons 缺失或类型不符。";
                return false;
            }

            if (!TryGetState(context, stateType, out var state) || state == null)
            {
                error = "无法获取该 BuildContext 的 VQT NdmfState 实例。";
                return false;
            }

            try
            {
                field.SetValue(state, false);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static bool TryGetState(BuildContext context, Type stateType, out object state)
        {
            state = null;

            try
            {
                var generic = typeof(BuildContext).GetMethod(
                    "GetState",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);
                if (generic != null)
                {
                    var method = generic.MakeGenericMethod(stateType);
                    state = method.Invoke(context, null);
                    if (state != null) return true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[QuickPlay] GetState 反射调用失败，尝试直接写入状态表：" + exception.Message);
            }

            try
            {
                var stateField = typeof(BuildContext).GetField(
                    "_state",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (stateField?.GetValue(context) is IDictionary table)
                {
                    if (table.Contains(stateType))
                    {
                        state = table[stateType];
                        return state != null;
                    }

                    state = Activator.CreateInstance(stateType, true);
                    table[stateType] = state;
                    return state != null;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[QuickPlay] 直接写入 BuildContext 状态表失败：" + exception.Message);
            }

            return false;
        }
    }
}
