using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace marble810.MarbleAvatarToolbox.PlayModeTools
{
    /// <summary>准备请求。</summary>
    internal sealed class QuickPlayPrepareRequest
    {
        public GameObject Original;
        public QuickPlayPreferenceData Snapshot;
        public string SessionId;
        public double RequestedAt;

        /// <summary>同一次捕获的 Selection 快照（由会话提供，保证 record 与同域恢复使用同一份数据）。</summary>
        public QuickPlaySelectionSnapshot SelectionSnapshot;
    }

    /// <summary>准备结果。</summary>
    internal sealed class QuickPlayPrepareResult
    {
        public bool Success;
        public string Error = string.Empty;
        public string FailedStep = string.Empty;
        public GameObject Clone;
        public QuickPlaySessionRecord Record;
        public QuickPlayStripResult StripResult;
        public bool OriginalDeactivated;

        /// <summary>
        /// 失败后仍存活、且由本次准备创建的临时对象（可能没有会话标记）。
        /// 调用方（会话）必须接管这些引用并按引用继续销毁，不能丢弃。
        /// </summary>
        public readonly List<GameObject> ResidualObjects = new List<GameObject>();

        /// <summary>true 表示存在无法确认已销毁的残留（禁止自动清记录/启动新会话）。</summary>
        public bool ResidualOwnershipUncertain;
    }

    /// <summary>准备过程涉及的 Unity 操作（可注入，便于故障注入测试）。</summary>
    internal interface IQuickPlayPrepareOperations
    {
        GameObject Instantiate(GameObject original);

        /// <summary>保持目标场景、父节点与 local TRS 语义。失败时返回原因。</summary>
        bool TryPlaceClone(GameObject clone, GameObject original, out string error);

        bool TryAttachMarker(GameObject clone, string sessionId, GameObject original, out string error);

        /// <summary>执行剔除计划；任何所选工具无法按已验证规则抑制时必须失败。</summary>
        bool TryApplyStripPlan(GameObject clone, QuickPlayPreferenceData snapshot, QuickPlayStripResult result, out string error);

        void DestroyOwned(GameObject owned);

        void SetOriginalActive(GameObject original, bool active);

        void SelectClone(GameObject clone);
    }

    /// <summary>
    /// 副本准备流水线（纯逻辑 + 可注入操作）。
    ///
    /// 安全顺序：
    /// 1. 只在所有可失败步骤成功后才修改原对象（禁用/选择）。
    /// 2. Instantiate 之后立即登记所有权，任何异常都按对象引用销毁（不按名字）。
    /// 3. durable 记录写入失败一律中止，不继续修改场景。
    /// 4. 中止时只有确认清理成功才清除 durable 记录（留下唯一恢复依据否则不抹）。
    /// </summary>
    internal static class QuickPlayPreparePipeline
    {
        public static QuickPlayPrepareResult Execute(
            QuickPlayPrepareRequest request,
            IQuickPlayPrepareOperations operations,
            IQuickPlaySessionStore store)
        {
            var result = new QuickPlayPrepareResult();
            var owned = new List<GameObject>();

            if (request?.Original == null)
            {
                result.Error = "准备失败：目标 Avatar 为空。";
                return result;
            }

            if (operations == null || store == null)
            {
                result.Error = "准备失败：内部依赖缺失。";
                return result;
            }

            var sessionId = string.IsNullOrEmpty(request.SessionId) ? Guid.NewGuid().ToString("N") : request.SessionId;
            var record = new QuickPlaySessionRecord
            {
                sessionId = sessionId,
                phase = (int)QuickPlaySessionPhase.Preparing,
                originalActiveSelf = request.Original.activeSelf,
                originalName = request.Original.name,
                preferenceToolIds = new List<string>(request.Snapshot?.stripToolIds ?? new List<string>()),
            };

            // 身份与 Selection / 场景快照：只在能可靠取得时写入，绝不退化为按名字匹配。
            if (!QuickPlayObjectIdentity.TryCaptureId(request.Original, out var originalId, out var identityError))
            {
                record.originalGlobalId = string.Empty;
                Debug.LogWarning("[QuickPlay] 无法建立原对象的 durable 身份（" + identityError +
                                 "），本次会话仍会使用 clone 标记中的直接引用；域重载后若标记也失效，将只告警而不猜测对象。");
            }
            else
            {
                record.originalGlobalId = originalId;
            }

            var selection = request.SelectionSnapshot ?? QuickPlayObjectIdentity.CaptureSelection();
            record.selectionGlobalIds = new List<string>(selection.GlobalObjectIds);
            record.selectionActiveGlobalId = selection.ActiveGlobalObjectId;
            record.selectionWasEmpty = selection.WasEmpty;
            record.selectionHadUncapturableObjects = selection.HadUncapturableObjects;
            record.cleanScenePaths = QuickPlayObjectIdentity.CaptureCleanScenePaths();

            result.Record = record;

            // 步骤 1：先写 durable 记录（Preparing）。写失败不得继续修改场景。
            if (!store.TrySave(record, out var saveError))
            {
                result.Error = "准备失败：无法写入会话记录（" + saveError + "），未对场景做任何修改。";
                result.FailedStep = "记录 Preparing";
                return result;
            }

            try
            {
                // 步骤 2：实例化并立刻登记所有权。
                var clone = operations.Instantiate(request.Original);
                if (clone == null)
                {
                    result.Error = "准备失败：无法实例化临时副本。";
                    result.FailedStep = "Instantiate";
                    return Abort(result, record, store, operations, owned);
                }

                owned.Add(clone);
                result.Clone = clone;
                record.cloneName = clone.name;

                // 步骤 3：保持目标场景/父节点/local TRS。
                if (!operations.TryPlaceClone(clone, request.Original, out var placeError))
                {
                    result.Error = "准备失败：无法按原语义放置临时副本（" + placeError + "）。";
                    result.FailedStep = "放置 clone";
                    return Abort(result, record, store, operations, owned);
                }

                // 步骤 4：附加所有权标记（失败则不再继续）。
                if (!operations.TryAttachMarker(clone, sessionId, request.Original, out var markerError))
                {
                    result.Error = "准备失败：无法建立副本所有权标记（" + markerError + "）。";
                    result.FailedStep = "附加标记";
                    return Abort(result, record, store, operations, owned);
                }

                // 步骤 5：记录 Prepared。
                record.phase = (int)QuickPlaySessionPhase.Prepared;
                if (!store.TrySave(record, out var preparedSaveError))
                {
                    result.Error = "准备失败：无法写入 Prepared 会话记录（" + preparedSaveError + "）。";
                    result.FailedStep = "记录 Prepared";
                    return Abort(result, record, store, operations, owned);
                }

                // 步骤 6：剔除计划；任何所选工具无法抑制即失败。
                var stripResult = new QuickPlayStripResult();
                if (!operations.TryApplyStripPlan(clone, request.Snapshot, stripResult, out var stripError))
                {
                    result.StripResult = stripResult;
                    result.Error = "准备失败：所选工具无法按已验证规则抑制（" + stripError + "）。";
                    result.FailedStep = "剔除计划";
                    return Abort(result, record, store, operations, owned);
                }

                result.StripResult = stripResult;

                // 只有实际执行成功的抑制才进入本会话的有效计划；运行 hook 一律以它为准。
                record.effectiveToolIds = new List<string>(stripResult.AppliedToolIds);
                record.d4RkSuppressionActive = stripResult.D4RkBlockerInstalled;
                record.vqtSuppressionActive = stripResult.VqtMenuIconStateSuppressionRequested;
                if (!store.TrySave(record, out var effectiveSaveError))
                {
                    result.Error = "准备失败：无法写入本会话的有效抑制计划（" + effectiveSaveError + "）。";
                    result.FailedStep = "记录有效计划";
                    return Abort(result, record, store, operations, owned);
                }

                result.Success = true;
                return result;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                result.Error = "准备失败：发生异常 " + exception.GetType().Name + "：" + exception.Message;
                result.FailedStep = result.FailedStep.Length > 0 ? result.FailedStep : "异常";
                return Abort(result, record, store, operations, owned);
            }
        }

        private static QuickPlayPrepareResult Abort(
            QuickPlayPrepareResult result,
            QuickPlaySessionRecord record,
            IQuickPlaySessionStore store,
            IQuickPlayPrepareOperations operations,
            List<GameObject> owned)
        {
            result.Success = false;
            var cleanupComplete = true;

            foreach (var gameObject in owned)
            {
                try
                {
                    if (gameObject != null) operations.DestroyOwned(gameObject);
                }
                catch (Exception exception)
                {
                    cleanupComplete = false;
                    Debug.LogException(exception);
                }
            }

            // 不丢弃所有权：仍存活的对象必须交回会话按引用重试销毁。
            result.ResidualObjects.Clear();
            foreach (var gameObject in owned)
            {
                if (gameObject == null) continue; // DestroyImmediate 成功后 Unity 认为其为 null
                result.ResidualObjects.Add(gameObject);
            }

            owned.Clear();
            result.ResidualOwnershipUncertain = !cleanupComplete;

            if (cleanupComplete)
            {
                // 已确认没有任何临时对象残留，才可以抹掉记录。
                store.Clear();
            }
            else
            {
                // 清理未完成：保留记录（并标记所有权不确定）作为唯一恢复依据，交由后续恢复流程重试。
                record.phase = (int)QuickPlaySessionPhase.Cleaning;
                record.residualOwnershipUncertain = true;
                if (!store.TrySave(record, out var keepError))
                {
                    Debug.LogError("[QuickPlay] 准备中止且无法保留会话记录：" + keepError);
                }

                Debug.LogError("[QuickPlay] 准备中止但清理未完成，已保留会话记录与残留对象引用以便重试。");
            }

            return result;
        }
    }

    /// <summary>生产环境的准备操作实现。</summary>
    internal sealed class QuickPlayPrepareOperations : IQuickPlayPrepareOperations
    {
        public GameObject Instantiate(GameObject original)
        {
            return UnityEngine.Object.Instantiate(original);
        }

        public bool TryPlaceClone(GameObject clone, GameObject original, out string error)
        {
            error = string.Empty;
            if (clone == null || original == null)
            {
                error = "clone 或原对象为空。";
                return false;
            }

            clone.name = original.name + QuickPlaySession.CloneNameSuffix;
            clone.transform.SetParent(original.transform.parent, false);

            if (clone.scene != original.scene)
            {
                if (!original.scene.IsValid() || !original.scene.isLoaded)
                {
                    error = "原对象所在场景不可用。";
                    return false;
                }

                SceneManager.MoveGameObjectToScene(clone, original.scene);
            }

            if (clone.scene != original.scene)
            {
                error = "clone 未能与原对象位于同一场景。";
                return false;
            }

            clone.transform.localPosition = original.transform.localPosition;
            clone.transform.localRotation = original.transform.localRotation;
            clone.transform.localScale = original.transform.localScale;
            clone.transform.SetSiblingIndex(original.transform.GetSiblingIndex() + 1);
            clone.SetActive(true);
            return true;
        }

        public bool TryAttachMarker(GameObject clone, string sessionId, GameObject original, out string error)
        {
            error = string.Empty;
            var marker = clone.AddComponent<QuickPlayCloneMarker>();
            if (marker == null)
            {
                error = "AddComponent(QuickPlayCloneMarker) 返回 null。";
                return false;
            }

            marker.Initialize(sessionId, original);
            if (!string.Equals(marker.SessionId, sessionId, StringComparison.Ordinal))
            {
                error = "标记写入后 sessionId 不一致。";
                return false;
            }

            return true;
        }

        public bool TryApplyStripPlan(
            GameObject clone,
            QuickPlayPreferenceData snapshot,
            QuickPlayStripResult result,
            out string error)
        {
            return QuickPlayComponentRules.TryApplyStripPlan(clone, snapshot, result, out error);
        }

        public void DestroyOwned(GameObject owned)
        {
            UnityEngine.Object.DestroyImmediate(owned);
        }

        public void SetOriginalActive(GameObject original, bool active)
        {
            if (original != null) original.SetActive(active);
        }

        public void SelectClone(GameObject clone)
        {
            if (clone != null) UnityEditor.Selection.activeGameObject = clone;
        }
    }
}
