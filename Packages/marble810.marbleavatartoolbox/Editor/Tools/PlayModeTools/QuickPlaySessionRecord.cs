using System;
using System.Collections.Generic;
using UnityEditor;

namespace marble810.MarbleAvatarToolbox.PlayModeTools
{
    /// <summary>
    /// 跨 Domain Reload 保留的最小会话记录（存于 Unity SessionState，不写入项目资产）。
    ///
    /// 身份约定：
    /// - 原对象身份只用 <see cref="originalGlobalId"/>（GlobalObjectId）与 clone 标记里的直接引用；
    ///   绝不用名字/兄弟序号推断对象身份。
    /// - Selection 与初始 dirty 场景均持久化，域重载后仍可安全恢复。
    /// - <see cref="effectiveToolIds"/> 是本会话**已验证并实际启用**的抑制计划；
    ///   <see cref="preferenceToolIds"/> 只是用户偏好快照，运行 hook 一律以有效计划为准。
    /// </summary>
    [Serializable]
    internal sealed class QuickPlaySessionRecord
    {
        public const int CurrentVersion = 3;

        /// <summary>旧版记录中 preprocessStage 的失败值（迁移用）。</summary>
        public const int LegacyFailedStage = 3;

        /// <summary>受支持的 phase 取值范围（其余值按未知记录保守处理）。</summary>
        public const int MinKnownPhase = 0;
        public const int MaxKnownPhase = 5;

        public int version = CurrentVersion;
        public string sessionId;
        public int phase = (int)QuickPlaySessionPhase.Idle;
        public string cloneName;

        /// <summary>原对象的 GlobalObjectId 字符串（唯一 durable 身份依据）。</summary>
        public string originalGlobalId;

        /// <summary>仅用于日志/错误信息，不用于身份判断。</summary>
        public string originalName;

        public bool originalActiveSelf;

        /// <summary>true 表示本工具确实写入过原对象的 active 状态（用于冲突检测）。</summary>
        public bool originalActiveWritten;

        /// <summary>进入会话前的完整选择集（GlobalObjectId，按原顺序；仅能无损捕获的对象）。</summary>
        public List<string> selectionGlobalIds = new List<string>();

        /// <summary>选择集中的 active 对象（GlobalObjectId，可为空）。</summary>
        public string selectionActiveGlobalId;

        /// <summary>进入会话时选择集是否为空（用于区分「真实空集」与「捕获失败」）。</summary>
        public bool selectionWasEmpty;

        /// <summary>选择集中有对象无法生成 durable 身份时置位（重载后只能部分恢复）。</summary>
        public bool selectionHadUncapturableObjects;

        /// <summary>会话开始前处于「干净」状态的场景路径（相对项目根）。</summary>
        public List<string> cleanScenePaths = new List<string>();

        /// <summary>用户偏好快照（原样保留，含未安装工具的 ID）。</summary>
        public List<string> preferenceToolIds = new List<string>();

        /// <summary>本会话已验证并实际生效的抑制计划（hooks 只看这个）。</summary>
        public List<string> effectiveToolIds = new List<string>();

        /// <summary>本会话是否实际建立了 d4rk 阻断状态。</summary>
        public bool d4RkSuppressionActive;

        /// <summary>本会话是否实际启用了 VQT 菜单图标抑制。</summary>
        public bool vqtSuppressionActive;

        /// <summary>准备失败时仍有无法定位（无标记）的残留对象存在；为 true 时禁止自动清理记录或启动新会话。</summary>
        public bool residualOwnershipUncertain;

        /// <summary>本工具已知失败是否已持久化；为 true 时会话继续收敛到退出与清理。</summary>
        public bool sessionFailed;

        /// <summary>本工具已知失败原因（仅 sessionFailed=true 时有意义）。</summary>
        public string failureReason = string.Empty;

        // ---- 旧版只读字段（兼容读取，本版本不再写入）----

        /// <summary>
        /// 旧版 preprocess 观察阶段：0=无、1=已进入、2=已收尾、3=失败。
        /// 基础版不再据 1/2 推断成功或失败；仅在值为 3 且带原因时迁移为 sessionFailed。
        /// </summary>
        public int preprocessStage;

        /// <summary>旧版失败原因（迁移到 failureReason 后仅保留原值供诊断）。</summary>
        public string preprocessFailureReason = string.Empty;

        public bool HasResidualRisk => residualOwnershipUncertain;

        public QuickPlaySessionRecord Clone()
        {
            return new QuickPlaySessionRecord
            {
                version = version,
                sessionId = sessionId,
                phase = phase,
                cloneName = cloneName,
                originalGlobalId = originalGlobalId,
                originalName = originalName,
                originalActiveSelf = originalActiveSelf,
                originalActiveWritten = originalActiveWritten,
                selectionGlobalIds = new List<string>(selectionGlobalIds ?? new List<string>()),
                selectionActiveGlobalId = selectionActiveGlobalId,
                selectionWasEmpty = selectionWasEmpty,
                selectionHadUncapturableObjects = selectionHadUncapturableObjects,
                cleanScenePaths = new List<string>(cleanScenePaths ?? new List<string>()),
                preferenceToolIds = new List<string>(preferenceToolIds ?? new List<string>()),
                effectiveToolIds = new List<string>(effectiveToolIds ?? new List<string>()),
                d4RkSuppressionActive = d4RkSuppressionActive,
                vqtSuppressionActive = vqtSuppressionActive,
                residualOwnershipUncertain = residualOwnershipUncertain,
                sessionFailed = sessionFailed,
                failureReason = failureReason ?? string.Empty,
                preprocessStage = preprocessStage,
                preprocessFailureReason = preprocessFailureReason ?? string.Empty,
            };
        }

        /// <summary>
        /// 校验记录是否可安全使用。未知 version/phase/stage 一律保守拒绝（返回 false + 原因），
        /// 由调用方保留原文并停止自动恢复，避免把未知状态当成已知状态处理。
        /// 同时把旧版记录的失败标记迁移到 <see cref="sessionFailed"/>（只迁移「本工具已知失败」，
        /// 不把旧版观察值 1/2 当作成功或失败）。
        /// </summary>
        public bool TryValidate(out string error)
        {
            error = string.Empty;

            if (string.IsNullOrEmpty(sessionId))
            {
                error = "记录缺少 sessionId。";
                return false;
            }

            if (version <= 0 || version > CurrentVersion)
            {
                error = $"记录版本 {version} 不受支持（当前支持 1..{CurrentVersion}）。";
                return false;
            }

            if (phase < MinKnownPhase || phase > MaxKnownPhase)
            {
                error = $"记录 phase {phase} 未知。";
                return false;
            }

            if (preprocessStage < 0 || preprocessStage > LegacyFailedStage)
            {
                error = $"记录 preprocessStage {preprocessStage} 未知。";
                return false;
            }

            // 旧版记录迁移：preprocessStage=3 是旧版状态机写入的「本工具已知失败」。
            if (!sessionFailed && preprocessStage == LegacyFailedStage)
            {
                sessionFailed = true;
                failureReason = string.IsNullOrEmpty(preprocessFailureReason)
                    ? "旧版记录标记的失败（原因缺失）。"
                    : preprocessFailureReason;
            }

            if (sessionFailed && string.IsNullOrEmpty(failureReason))
            {
                failureReason = string.IsNullOrEmpty(preprocessFailureReason)
                    ? "未知失败。"
                    : preprocessFailureReason;
            }

            return true;
        }
    }

    /// <summary>会话记录的持久化后端（可注入以便测试保存失败）。</summary>
    internal interface IQuickPlaySessionStore
    {
        bool TrySave(QuickPlaySessionRecord record, out string error);

        /// <summary>读取记录；返回 null 表示没有可用记录（不存在、损坏或不受支持的版本）。</summary>
        QuickPlaySessionRecord Load(out string error);

        void Clear();
    }

    /// <summary>基于 UnityEditor.SessionState 的持久化实现（不写项目资产，编辑器重启即失效）。</summary>
    internal sealed class QuickPlaySessionStateStore : IQuickPlaySessionStore
    {
        internal const string SessionStateKey = "marble810.MarbleAvatarToolbox.QuickPlay.SessionRecord";

        public bool TrySave(QuickPlaySessionRecord record, out string error)
        {
            error = string.Empty;
            if (record == null)
            {
                error = "会话记录为空。";
                return false;
            }

            try
            {
                var json = UnityEngine.JsonUtility.ToJson(record);
                if (string.IsNullOrEmpty(json))
                {
                    error = "会话记录序列化结果为空。";
                    return false;
                }

                SessionState.SetString(SessionStateKey, json);

                // 写入读回必须**内容一致**，而不只是非空。
                var verify = SessionState.GetString(SessionStateKey, string.Empty);
                if (!string.Equals(json, verify, StringComparison.Ordinal))
                {
                    error = "会话记录写入后读回内容与写入内容不一致。";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        public QuickPlaySessionRecord Load(out string error)
        {
            error = string.Empty;
            try
            {
                var json = SessionState.GetString(SessionStateKey, string.Empty);
                if (string.IsNullOrEmpty(json)) return null;

                var record = UnityEngine.JsonUtility.FromJson<QuickPlaySessionRecord>(json);
                if (record == null)
                {
                    error = "会话记录无法反序列化。";
                    return null;
                }

                if (!record.TryValidate(out var validationError))
                {
                    // 保守处理：不把无法识别的记录当作可用会话，但保留原文供人工检查。
                    error = validationError;
                    return null;
                }

                return record;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return null;
            }
        }

        public void Clear()
        {
            try
            {
                SessionState.EraseString(SessionStateKey);
            }
            catch (Exception)
            {
                // 忽略：清理失败不应抛出到调用方
            }
        }
    }
}
