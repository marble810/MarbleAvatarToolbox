using System;
using System.Collections.Generic;
using System.Linq;

namespace marble810.MarbleAvatarToolbox.PlayModeTools
{
    /// <summary>启动前检查的输入（与 Unity 编辑器状态解耦，便于测试）。</summary>
    internal sealed class QuickPlayPreflightInput
    {
        public bool IsPlayingOrWillChangePlaymode;
        public bool IsCompilingOrUpdating;
        public bool HasActiveSession;
        public bool ApplyOnPlayEnabled;
        public QuickPlayTargetResolution Target;
        public QuickPlayPreferenceData Snapshot;
    }

    /// <summary>启动前检查结果。</summary>
    internal sealed class QuickPlayStartCheck
    {
        public QuickPlayPreflightInput Input;
        public string Error;
        public List<string> BlockedToolReasons = new List<string>();

        public bool CanStart => string.IsNullOrEmpty(Error);
    }

    /// <summary>
    /// 启动前验证：编辑器状态、重复会话、目标解析、NDMF ApplyOnPlay 与所选工具兼容性。
    /// 关闭 ApplyOnPlay 时必须在任何场景修改前拒绝，且不修改全局开关。
    /// </summary>
    internal static class QuickPlayPreflight
    {
        public const string ApplyOnPlayDisabledMessage =
            "NDMF 的 Apply on Play 已关闭，QuickPlay 无法保证 Modular Avatar 等必要处理。\n\n" +
            "请先打开 NDMF 设置中的 “Apply on Play”，再启动 QuickPlay（QuickPlay 不会自动修改该全局开关）。";

        public static QuickPlayStartCheck Evaluate(
            QuickPlayPreflightInput input,
            Func<QuickPlayToolDefinition, QuickPlayToolCapability> capabilityEvaluator = null)
        {
            var check = new QuickPlayStartCheck { Input = input };
            var evaluator = capabilityEvaluator ?? QuickPlayToolRegistry.Evaluate;

            if (input == null)
            {
                check.Error = "内部错误：启动检查参数为空。";
                return check;
            }

            if (input.IsPlayingOrWillChangePlaymode)
            {
                check.Error = "当前已经处于 Play 或进入 Play 的流程中，无法启动 QuickPlay。";
                return check;
            }

            if (input.IsCompilingOrUpdating)
            {
                check.Error = "编辑器正在编译或刷新资源，请稍后再试。";
                return check;
            }

            if (input.HasActiveSession)
            {
                check.Error = "已存在 QuickPlay 会话。请先退出当前 Play 并等待清理完成，再启动新的会话。";
                return check;
            }

            if (input.Target == null || !input.Target.Succeeded)
            {
                check.Error = input.Target?.FailureReason ?? "无法确定要使用的 Avatar。";
                return check;
            }

            if (!input.ApplyOnPlayEnabled)
            {
                check.Error = ApplyOnPlayDisabledMessage;
                return check;
            }

            foreach (var toolId in input.Snapshot?.stripToolIds ?? Enumerable.Empty<string>())
            {
                var definition = QuickPlayToolRegistry.Find(toolId);
                if (definition == null) continue; // 未知历史 ID：安全忽略，不执行匹配

                var capability = evaluator(definition);
                if (capability.Availability != QuickPlayToolAvailability.Incompatible) continue;

                check.BlockedToolReasons.Add($"{definition.Label}：{capability.Reason}");
            }

            if (check.BlockedToolReasons.Count > 0)
            {
                check.Error = "所选工具在当前工程中不兼容，无法启动 QuickPlay：\n\n" +
                              string.Join("\n", check.BlockedToolReasons) +
                              "\n\n请在 QuickPlay Settings 中取消勾选，或使用兼容版本。";
            }

            return check;
        }
    }
}
