using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace marble810.MarbleAvatarToolbox.PlayModeTools
{
    internal enum QuickPlaySessionPhase
    {
        Idle = 0,
        Preparing = 1,
        Prepared = 2,
        EnteringPlay = 3,
        Running = 4,
        Cleaning = 5,
    }

    /// <summary>
    /// QuickPlay 会话控制：目标解析、副本准备、剔除计划、SDK/NDMF 协作、恢复与清理。
    /// 所有结构修改都限定在自有的临时副本上；失败一律收敛为一次性退出与清理。
    /// </summary>
    [InitializeOnLoad]
    internal static class QuickPlaySession
    {
        internal const string CloneNameSuffix = " (QuickPlay Clone)";

        /// <summary>进入 Play 后等待状态收敛的宽限（秒）。测试可缩短。</summary>
        internal static double PlayCancelGraceSeconds = 2.0;

        /// <summary>对话框呈现（测试可替换，生产使用 EditorUtility.DisplayDialog）。</summary>
        internal static Action<string, string> DialogPresenter =
            (title, message) => EditorUtility.DisplayDialog(title, message, "确定");

        /// <summary>
        /// 测试 seam：延迟动作调度（生产默认 EditorApplication.delayCall）。
        /// </summary>
        internal static Action<Action> DeferredActionScheduler = action => EditorApplication.delayCall += () => action();

        /// <summary>测试 seam：退出 Play（生产默认 EditorApplication.ExitPlaymode）。</summary>
        internal static Action ExitPlayInvoker = () => EditorApplication.ExitPlaymode();

        /// <summary>测试 seam：进入 Play（生产默认 EditorApplication.EnterPlaymode）。</summary>
        internal static Action EnterPlayInvoker = () => EditorApplication.EnterPlaymode();

        private static IQuickPlaySessionStore _store = new QuickPlaySessionStateStore();
        private static QuickPlaySessionRecord _record;
        private static GameObject _originalReference;
        private static readonly List<GameObject> OwnedClones = new List<GameObject>();
        private static readonly List<GameObject> ResidualObjects = new List<GameObject>();
        private static bool _originalActiveWritten;
        private static UnityEngine.Object[] _selectionWeSet;
        private static UnityEngine.Object _selectionActiveWeSet;
        private static QuickPlaySelectionSnapshot _capturedSelection;
        private static bool _durableWriteFailed;
        private static double _stateDivergenceSince = -1d;
        private static bool _playableAnimatorEnsured;
        private static bool _failureHandled;
        private static string _unreadableRecordError = string.Empty;
        private static bool _pendingFailureActionPending;
        private static string _pendingFailureSessionId = string.Empty;
        private static int _pendingFailureActionId;

        static QuickPlaySession()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.delayCall += RecoverAfterDomainReload;
        }

        internal static QuickPlaySessionRecord CurrentRecord
        {
            get
            {
                EnsureRecordLoaded();
                return _record;
            }
        }

        /// <summary>durable 记录优先：重载后延迟恢复前也必须能看到已有会话。</summary>
        internal static bool HasActiveSession
        {
            get
            {
                EnsureRecordLoaded();
                return _record != null && _record.phase != (int)QuickPlaySessionPhase.Idle;
            }
        }

        private static double Now => EditorApplication.timeSinceStartup;

        /// <summary>测试辅助：替换 durable 存储后端并清空会话静态状态（模拟域重载）。</summary>
        internal static void SetStoreForTests(IQuickPlaySessionStore store)
        {
            ResetRuntimeState(clearStore: false);
            _store = store ?? new QuickPlaySessionStateStore();
        }

        private static void ResetRuntimeState(bool clearStore)
        {
            if (clearStore) _store.Clear();
            _record = null;
            _originalReference = null;
            OwnedClones.Clear();
            ResidualObjects.Clear();
            _originalActiveWritten = false;
            _selectionWeSet = null;
            _selectionActiveWeSet = null;
            _capturedSelection = null;
            _durableWriteFailed = false;
            _playableAnimatorEnsured = false;
            _failureHandled = false;
            _unreadableRecordError = string.Empty;
            CancelPendingFailureAction();
            _stateDivergenceSince = -1d;
        }

        // ---------------------------------------------------------------- 启动

        /// <summary>菜单入口。</summary>
        public static void Start(GameObject selectedObject)
        {
            if (!TryRecoverStaleSession(out var recoveryError))
            {
                ShowDialog("无法启动 QuickPlay", recoveryError);
                return;
            }

            var preferences = QuickPlaySettingsWindow.Preferences;
            var snapshot = preferences.Snapshot();

            var input = new QuickPlayPreflightInput
            {
                IsPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
                IsCompilingOrUpdating = EditorApplication.isCompiling || EditorApplication.isUpdating,
                HasActiveSession = HasActiveSession,
                ApplyOnPlayEnabled = QuickPlayNdmfConfig.ApplyOnPlay,
                Target = QuickPlayTargetResolver.Resolve(selectedObject),
                Snapshot = snapshot,
            };

            var check = QuickPlayPreflight.Evaluate(input);
            if (!check.CanStart)
            {
                ShowDialog("无法启动 QuickPlay", check.Error);
                return;
            }

            ExecuteStart(check, snapshot);
        }

        private static void ExecuteStart(QuickPlayStartCheck check, QuickPlayPreferenceData snapshot)
        {
            var original = check.Input.Target.AvatarRoot;
            var sessionId = Guid.NewGuid().ToString("N");
            var requestedAt = Now;
            var prepareWatch = Stopwatch.StartNew();

            // 只捕获一次 Selection：同域直接引用 + durable 身份。
            var capturedSelection = QuickPlayObjectIdentity.CaptureSelection();

            var prepareResult = QuickPlayPreparePipeline.Execute(
                new QuickPlayPrepareRequest
                {
                    Original = original,
                    Snapshot = snapshot,
                    SessionId = sessionId,
                    RequestedAt = requestedAt,
                    SelectionSnapshot = capturedSelection,
                },
                new QuickPlayPrepareOperations(),
                _store);

            prepareWatch.Stop();

            if (!prepareResult.Success)
            {
                HandlePreparationFailure(prepareResult);
                ShowDialog("QuickPlay 准备失败", prepareResult.Error);
                return;
            }

            AdoptPreparedSession(prepareResult.Record, prepareResult.Clone, original, capturedSelection, requestedAt);
            LogStripResult(prepareResult.StripResult, prepareWatch.ElapsedMilliseconds, prepareResult.Record);

            try
            {
                // 只有全部可失败步骤成功后，才修改原对象。
                _originalActiveWritten = true;
                _record.originalActiveWritten = true;
                original.SetActive(false);
                SelectPreparedClone(prepareResult.Clone);

                _record.phase = (int)QuickPlaySessionPhase.EnteringPlay;
                if (!_store.TrySave(_record, out var saveError))
                {
                    throw new InvalidOperationException("无法写入 EnteringPlay 会话记录：" + saveError);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Cleanup("进入 Play 前失败", restoreSelection: true);
                ShowDialog("QuickPlay 准备失败", "进入 Play 前发生错误，会话已回滚：\n" + exception.Message);
                return;
            }

            EnterPlayInvoker();

            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Cleanup("进入 Play 失败", restoreSelection: true);
                ShowDialog("进入 Play 失败", "编辑器未能进入 Play Mode，QuickPlay 临时会话已回滚。");
            }
        }

        /// <summary>
        /// 接管一次成功准备的会话状态（生产与测试共用同一路径）。
        /// </summary>
        internal static void AdoptPreparedSession(
            QuickPlaySessionRecord record,
            GameObject clone,
            GameObject original,
            QuickPlaySelectionSnapshot capturedSelection,
            double requestedAt)
        {
            _record = record;
            _originalReference = original;
            _capturedSelection = capturedSelection;
            OwnedClones.Clear();
            ResidualObjects.Clear();
            if (clone != null) OwnedClones.Add(clone);
            _playableAnimatorEnsured = false;
            _failureHandled = false;
            _durableWriteFailed = false;
            _stateDivergenceSince = -1d;
            CancelPendingFailureAction(); // 新会话接管：旧会话的挂起失败动作不得再执行
        }

        /// <summary>把 Selection 指向临时副本，并记录「本工具设置的选择」用于冲突判断。</summary>
        internal static void SelectPreparedClone(GameObject clone)
        {
            if (clone == null) return;
            Selection.activeGameObject = clone;
            _selectionWeSet = Selection.objects;
            _selectionActiveWeSet = Selection.activeObject;
        }

        /// <summary>
        /// 准备失败后的接管：保留并接管仍存活的对象引用，记录「所有权不确定」状态。
        /// 未确认销毁前禁止自动清记录或启动新会话。
        /// </summary>
        internal static void HandlePreparationFailure(QuickPlayPrepareResult result)
        {
            if (result == null) return;

            _record = result.Record;
            ResidualObjects.Clear();
            foreach (var residual in result.ResidualObjects)
            {
                if (residual != null) ResidualObjects.Add(residual);
            }

            if (result.ResidualOwnershipUncertain || ResidualObjects.Count > 0)
            {
                Debug.LogError("[QuickPlay] 准备失败后仍有 " + ResidualObjects.Count +
                               " 个残留对象需要处理；所有权不确定，已阻止自动清理与启动新会话。");
                TryDestroyResiduals(out _);
            }

            _durableWriteFailed = false;
        }

        /// <summary>尝试销毁准备残留；返回是否全部销毁成功。</summary>
        private static bool TryDestroyResiduals(out string error)
        {
            error = string.Empty;
            var remaining = new List<GameObject>();

            foreach (var residual in ResidualObjects)
            {
                if (residual == null) continue;
                try
                {
                    UnityEngine.Object.DestroyImmediate(residual);
                    if (residual != null) remaining.Add(residual);
                }
                catch (Exception exception)
                {
                    remaining.Add(residual);
                    error = exception.Message;
                    Debug.LogException(exception);
                }
            }

            ResidualObjects.Clear();
            ResidualObjects.AddRange(remaining);

            if (ResidualObjects.Count == 0 && _record != null && _record.residualOwnershipUncertain)
            {
                _record.residualOwnershipUncertain = false;
                if (!_store.TrySave(_record, out var saveError))
                {
                    Debug.LogWarning("[QuickPlay] 无法更新残留状态：" + saveError);
                    return false;
                }

                if (_record.phase == (int)QuickPlaySessionPhase.Cleaning)
                {
                    _store.Clear();
                    _record = null;
                }
            }

            return ResidualObjects.Count == 0;
        }

        internal static QuickPlayPrepareResult PrepareForTests(
            GameObject original,
            QuickPlayPreferenceData snapshot,
            IQuickPlayPrepareOperations operations,
            string sessionId,
            double requestedAt)
        {
            return QuickPlayPreparePipeline.Execute(
                new QuickPlayPrepareRequest
                {
                    Original = original,
                    Snapshot = snapshot,
                    SessionId = sessionId,
                    RequestedAt = requestedAt,
                },
                operations,
                _store);
        }

        private static void LogStripResult(QuickPlayStripResult result, long prepareMilliseconds, QuickPlaySessionRecord record)
        {
            var builder = new StringBuilder();
            builder.Append("[QuickPlay] 临时副本准备完成（").Append(prepareMilliseconds).Append("ms）");

            if (result != null)
            {
                foreach (var pair in result.RemovedByToolId)
                {
                    var definition = QuickPlayToolRegistry.Find(pair.Key);
                    builder.Append("；").Append(definition?.Label ?? pair.Key).Append(" 剔除 ").Append(pair.Value).Append(" 个组件");
                }

                if (result.D4RkBlockerInstalled) builder.Append("；已建立 d4rk 阻断");
                if (result.VqtMenuIconStateSuppressionRequested) builder.Append("；将跳过 VQT 菜单图标压缩");

                foreach (var warning in result.Warnings)
                {
                    Debug.LogWarning("[QuickPlay] " + warning);
                }
            }

            if (record != null && (record.effectiveToolIds?.Count ?? 0) == 0)
            {
                builder.Append("；本会话未剔除任何工具");
            }

            Debug.Log(builder.ToString());
        }

        // ---------------------------------------------------------------- 恢复

        internal static bool TryRecoverStaleSession(out string error)
        {
            error = string.Empty;
            EnsureRecordLoaded();

            if (!string.IsNullOrEmpty(_unreadableRecordError))
            {
                error = "上一次 QuickPlay 会话记录无法识别，已保留原文并阻止自动恢复。\n" +
                        "原因：" + _unreadableRecordError + "\n\n" +
                        "请重启 Unity 编辑器（会话记录随编辑器会话结束自动清空）后再试。";
                return false;
            }

            if (_record == null || _record.phase == (int)QuickPlaySessionPhase.Idle) return true;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return true;

            // 有所有权不确定的残留时，绝不自动清记录或启动新会话。
            if (_record.residualOwnershipUncertain && ResidualObjects.Count == 0)
            {
                error = "上一次 QuickPlay 准备留下所有权不确定的残留对象，且当前进程已无法定位它们。\n" +
                        "已保留会话记录，不会自动清理或启动新会话。请检查场景中 QuickPlay 临时副本并手动处理后重试。";
                Debug.LogError("[QuickPlay] " + error);
                return false;
            }

            Debug.LogWarning("[QuickPlay] 检测到上次会话的残留记录，启动前先尝试安全恢复。");
            if (Cleanup("启动前恢复上次残留会话", restoreSelection: true)) return true;

            error = "上一次 QuickPlay 会话的清理未完成，会话记录已保留以便重试。\n" +
                    "请查看 Console 中的错误，处理残留对象后再次启动。";
            return false;
        }

        private static void RecoverAfterDomainReload()
        {
            EnsureRecordLoaded();

            if (_record == null) return;
            if (_record.phase == (int)QuickPlaySessionPhase.Idle)
            {
                ClearRecord();
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                NormalizePhaseAgainstEditorState();
                return;
            }

            Debug.LogWarning("[QuickPlay] 脚本重载后发现未完成会话，正在安全恢复。");
            Cleanup("脚本重载后的恢复", restoreSelection: true);
        }

        /// <summary>
        /// 恢复后的 phase 必须与编辑器真实状态一致。
        /// 正在 Play 却停留在 EnteringPlay/Prepared/Preparing 时归一化（或安全终止），不允许永久等待。
        /// </summary>
        private static void NormalizePhaseAgainstEditorState()
        {
            if (_record == null) return;

            var phase = (QuickPlaySessionPhase)_record.phase;
            var playing = EditorApplication.isPlaying;

            if (playing && (phase == QuickPlaySessionPhase.Preparing ||
                            phase == QuickPlaySessionPhase.Prepared ||
                            phase == QuickPlaySessionPhase.EnteringPlay))
            {
                if (OwnedClones.All(clone => clone == null) && QuickPlayCloneLocator.FindAll(_record.sessionId).Count == 0)
                {
                    Debug.LogError("[QuickPlay] 编辑器处于 Play 中，但会话记录阶段为 " + phase + " 且找不到目标副本；" +
                                   "按安全终止处理。");
                    FailSession("会话记录阶段（" + phase + "）与编辑器状态不一致，且目标副本不存在。");
                    return;
                }

                Debug.LogWarning("[QuickPlay] 会话记录阶段为 " + phase + "，但编辑器已在 Play 中；归一化为 Running。");
                _record.phase = (int)QuickPlaySessionPhase.Running;
                if (!PersistSessionState(critical: true, context: "重载后归一化 Running"))
                {
                    return;
                }
            }

            if (!playing && phase == QuickPlaySessionPhase.Running)
            {
                Debug.LogWarning("[QuickPlay] 会话记录阶段为 Running，但编辑器不在 Play 中；按安全终止处理。");
                FailSession("会话记录阶段（Running）与编辑器状态不一致（编辑器不在 Play 中）。");
            }
        }

        internal static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                {
                    EnsureRecordLoaded();
                    if (_record == null || _record.phase != (int)QuickPlaySessionPhase.EnteringPlay) return;

                    _record.phase = (int)QuickPlaySessionPhase.Running;
                    _playableAnimatorEnsured = false;
                    _stateDivergenceSince = -1d;

                    // Running 是关键持久化；失败必须安全终止而不是仅 warning 继续。
                    if (!PersistSessionState(critical: true, context: "进入 Play 后写入 Running"))
                    {
                        return;
                    }

                    Debug.Log("[QuickPlay] 已进入 Play。");
                    break;
                }

                case PlayModeStateChange.EnteredEditMode:
                {
                    EnsureRecordLoaded();
                    if (_record == null || _record.phase == (int)QuickPlaySessionPhase.Idle) return;

                    Cleanup("退出 Play", restoreSelection: true);
                    break;
                }
            }
        }

        internal static void OnEditorUpdate()
        {
            Tick(EditorApplication.isPlaying, EditorApplication.isPlayingOrWillChangePlaymode, Now);
        }

        /// <summary>
        /// 会话阶段推进（生产与测试共用同一路径；编辑器状态与时序由调用方传入）。
        /// 只做会话自身的阶段收敛、已知失败重驱动与目标副本丢失处理，
        /// 不推断 SDK/NDMF 构建结果，也不会因为没有回调到达而重试或超时退出。
        /// </summary>
        internal static void Tick(bool isPlaying, bool isPlayingOrWillChange, double now)
        {
            EnsureRecordLoaded();
            var record = _record;
            if (record == null) return;

            var phase = (QuickPlaySessionPhase)record.phase;

            if (phase == QuickPlaySessionPhase.EnteringPlay)
            {
                if (isPlaying || isPlayingOrWillChange)
                {
                    _stateDivergenceSince = -1d;
                    return;
                }

                if (_stateDivergenceSince < 0d) _stateDivergenceSince = now;
                if (now - _stateDivergenceSince < PlayCancelGraceSeconds) return;

                Cleanup("进入 Play 被取消", restoreSelection: true);
                Debug.LogWarning("[QuickPlay] 进入 Play 被取消，会话已回滚。");
                return;
            }

            if (phase != QuickPlaySessionPhase.Running) return;

            if (!isPlaying)
            {
                if (_stateDivergenceSince < 0d) _stateDivergenceSince = now;
                if (now - _stateDivergenceSince < PlayCancelGraceSeconds) return;

                Cleanup("Play 已结束但未收到退出回调", restoreSelection: true);
                return;
            }

            _stateDivergenceSince = -1d;

            // 重载后仍有效的已知失败：继续收敛到退出/清理（失败依据来自 durable 记录，不依赖回调到达）。
            if (record.sessionFailed && !_failureHandled)
            {
                FailSession(record.failureReason);
                return;
            }

            var clone = FindSingleOwnedClone(record.sessionId);
            if (clone == null)
            {
                FailSession("目标副本已不存在（可能被手动删除），无法保证本次会话的处理结果。");
            }
        }

        // ---------------------------------------------------------------- SDK / NDMF 协作

        /// <summary>目标副本是否属于当前活动会话（供 SDK hook 与 NDMF pass 使用）。</summary>
        internal static bool IsRegisteredTarget(GameObject gameObject)
        {
            if (gameObject == null) return false;

            EnsureRecordLoaded();
            var record = _record;
            if (record == null) return false;

            switch ((QuickPlaySessionPhase)record.phase)
            {
                case QuickPlaySessionPhase.Prepared:
                case QuickPlaySessionPhase.EnteringPlay:
                case QuickPlaySessionPhase.Running:
                    break;
                default:
                    return false;
            }

            var marker = gameObject.GetComponent<QuickPlayCloneMarker>();
            if (marker == null) return false;
            return string.Equals(marker.SessionId, record.sessionId, StringComparison.Ordinal);
        }

        /// <summary>
        /// VQT 抑制只对本会话**实际生效**的计划生效：
        /// 用户偏好里勾选了 VQT 但当前版本未安装/未验证时，这里必须为 false。
        /// </summary>
        internal static bool IsVqtSuppressionTarget(GameObject gameObject)
        {
            EnsureRecordLoaded();
            return _record != null && _record.vqtSuppressionActive && IsRegisteredTarget(gameObject);
        }

        /// <summary>
        /// SDK Early hook 入口。返回 false 表示当前目标必须立即中止 SDK preprocess 链。
        /// 非目标对象一律 no-op 且返回 true。
        /// </summary>
        internal static bool OnSdkPreprocessEntered(GameObject avatarGameObject)
        {
            if (!IsRegisteredTarget(avatarGameObject)) return true;

            // 只有本会话实际建立过 d4rk 阻断时才复核；未安装/未验证的偏好不得激活 hook。
            if (_record == null || !_record.d4RkSuppressionActive) return true;

            var definition = QuickPlayToolRegistry.Find(QuickPlayToolRegistry.D4RkId);
            if (definition == null)
            {
                FailSession("d4rk 阻断状态在 SDK preprocess 阶段复核失败：未找到 d4rk 定义。");
                return false;
            }

            if (!QuickPlayComponentRules.TryInstallD4RkBlocker(avatarGameObject, definition, null, out var blockerError))
            {
                FailSession("d4rk 阻断状态在 SDK preprocess 阶段复核失败：" + blockerError);
                return false;
            }

            return true;
        }

        /// <summary>
        /// SDK Late hook 入口：仅对本会话登记的目标副本做可播放性协作（启用被禁用的 root Animator）。
        /// 不把回调到达当作构建成功，也不据此重试、超时或终止会话；非目标一律 no-op。
        /// </summary>
        internal static void OnSdkPreprocessCompleted(GameObject avatarGameObject)
        {
            if (!IsRegisteredTarget(avatarGameObject)) return;
            if (_playableAnimatorEnsured) return;

            _playableAnimatorEnsured = true;
            QuickPlayAvatarPostProcess.EnsurePlayableRootAnimator(avatarGameObject);
        }

        /// <summary>
        /// 失败收敛：只执行一次；先把「本工具已知失败」写入 durable 记录，再提示与退出。
        /// 失败依据来自本工具自己的检查，不依赖任何回调到达。
        /// </summary>
        internal static void FailSession(string reason)
        {
            if (_failureHandled) return;
            _failureHandled = true;

            Debug.LogError("[QuickPlay] " + reason);

            EnsureRecordLoaded();
            MarkSessionFailedInMemory(reason);
            if (!_durableWriteFailed)
            {
                if (!PersistSessionState(critical: true, context: "记录失败状态")) return;
            }

            ShowDialog("QuickPlay 处理失败", reason + "\n\nQuickPlay 将退出 Play 并清理本次会话。");
            SchedulePendingFailureExit();
        }

        /// <summary>把已知失败写入内存记录（持久化由调用方负责）。</summary>
        private static void MarkSessionFailedInMemory(string reason)
        {
            if (_record == null) return;
            _record.sessionFailed = true;
            _record.failureReason = string.IsNullOrEmpty(reason) ? "未知失败。" : reason;
        }

        /// <summary>
        /// 只挂起「自己的」延迟动作：带上本次会话 ID 与唯一 token，
        /// 执行前核对仍是同一个有效失败会话；Cleanup/Reset/新会话会取消它。
        /// </summary>
        private static void SchedulePendingFailureExit()
        {
            _pendingFailureSessionId = _record?.sessionId ?? string.Empty;
            var actionId = ++_pendingFailureActionId;
            _pendingFailureActionPending = true;
            DeferredActionScheduler(() => RunPendingFailureAction(actionId));
        }

        /// <summary>取消本会话挂起的失败延迟动作（不触碰 EditorApplication.delayCall 的其它回调）。</summary>
        private static void CancelPendingFailureAction()
        {
            _pendingFailureActionPending = false;
            _pendingFailureSessionId = string.Empty;
            _pendingFailureActionId++;
        }

        /// <summary>
        /// 执行挂起的失败动作：仅在「仍是同一个有效失败会话」时生效。
        /// 会话已清理/进入 Cleaning/已换新会话、或普通 Play（无会话记录）时一律 no-op。
        /// </summary>
        private static void RunPendingFailureAction(int actionId)
        {
            if (!_pendingFailureActionPending) return;
            if (actionId != _pendingFailureActionId) return;

            _pendingFailureActionPending = false;

            EnsureRecordLoaded();
            var record = _record;
            if (record == null || record.phase == (int)QuickPlaySessionPhase.Idle) return;
            if (record.phase == (int)QuickPlaySessionPhase.Cleaning) return;
            if (!record.sessionFailed) return;

            if (!string.Equals(record.sessionId, _pendingFailureSessionId, StringComparison.Ordinal)) return;

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.Log("[QuickPlay] 本次预览中止，正在退出 Play。");
                ExitPlayInvoker();
                return;
            }

            Cleanup("处理失败", restoreSelection: true);
        }

        // ---------------------------------------------------------------- 清理

        /// <summary>
        /// 幂等清理：先冻结原对象恢复依据，再销毁本会话对象；按 durable 身份恢复，并保留未完成恢复的记录。
        /// </summary>
        internal static bool Cleanup(string reason, bool restoreSelection)
        {
            EnsureRecordLoaded();
            var record = _record;
            if (record == null) return true;

            // 清理一开始（含失败/不完整清理）就取消本会话挂起的旧失败动作。
            CancelPendingFailureAction();

            record.phase = (int)QuickPlaySessionPhase.Cleaning;
            var watch = Stopwatch.StartNew();
            var cleanupComplete = true;
            var errors = new List<string>();

            var owned = new List<GameObject>();
            foreach (var clone in OwnedClones)
            {
                if (clone != null && !owned.Contains(clone)) owned.Add(clone);
            }

            foreach (var clone in QuickPlayCloneLocator.FindAll(record.sessionId))
            {
                if (clone != null && !owned.Contains(clone)) owned.Add(clone);
            }

            foreach (var residual in ResidualObjects)
            {
                if (residual != null && !owned.Contains(residual)) owned.Add(residual);
            }

            var selectionWasOurs = QuickPlayObjectIdentity.CurrentSelectionMatches(_selectionWeSet, _selectionActiveWeSet);

            // 必须在销毁 clone 之前解析并冻结原对象引用（marker 就在 clone 上）。
            var original = _originalReference;
            var originalIdentityFailure = string.Empty;
            if (original == null)
            {
                original = QuickPlayOriginalReference.Resolve(record, owned, out originalIdentityFailure);
            }

            // 原对象未知但曾由本工具写入 active（内存或 durable 证据）：无法确认恢复 → 不得宣称清理完成。
            var weWroteActive = _originalActiveWritten || record.originalActiveWritten;
            var restorationUnconfirmed = original == null && weWroteActive;

            foreach (var clone in owned)
            {
                try
                {
                    if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                    if (clone != null)
                    {
                        cleanupComplete = false;
                        errors.Add("副本销毁后仍存在：" + clone.name);
                    }
                }
                catch (Exception exception)
                {
                    cleanupComplete = false;
                    errors.Add("销毁副本失败：" + exception.Message);
                    Debug.LogException(exception);
                }
            }

            OwnedClones.Clear();
            ResidualObjects.Clear();

            if (owned.Count == 0)
            {
                Debug.LogWarning("[QuickPlay] 未找到属于本会话的副本（可能已被手动删除），不会删除任何同名对象。");
            }

            if (original != null)
            {
                if (!QuickPlayObjectIdentity.TryRestoreActiveState(original, record.originalActiveSelf,
                        writtenValue: false, weWrote: weWroteActive, out var activeConflict))
                {
                    if (!string.IsNullOrEmpty(activeConflict))
                    {
                        Debug.LogWarning("[QuickPlay] " + activeConflict);
                    }
                }
            }
            else if (_originalActiveWritten || record.originalActiveWritten)
            {
                Debug.LogError("[QuickPlay] 无法恢复原对象 active 状态：" + originalIdentityFailure +
                               "（记录中的原始名称仅供参考：" + record.originalName + "）。" +
                               "已保留会话记录，不会自动清除。");
            }
            else
            {
                Debug.LogWarning("[QuickPlay] 原对象不在本会话所有权范围内且未记录身份：" + originalIdentityFailure);
            }

            if (restoreSelection && selectionWasOurs)
            {
                try
                {
                    var restoredCount = QuickPlayObjectIdentity.RestoreSelection(_capturedSelection, out var warnings);
                    foreach (var warning in warnings) Debug.LogWarning("[QuickPlay] " + warning);
                    Debug.Log("[QuickPlay] 已恢复进入会话前的 Selection（" + restoredCount + " 个对象）。");
                }
                catch (Exception exception)
                {
                    errors.Add("恢复 Selection 失败：" + exception.Message);
                    Debug.LogException(exception);
                }
            }
            else if (restoreSelection)
            {
                Debug.Log("[QuickPlay] 当前 Selection 已由用户改变，保持现状，不覆盖。");
            }

            try
            {
                var newlyDirty = QuickPlayObjectIdentity.FindNewlyDirtyScenes(record.cleanScenePaths);
                if (newlyDirty.Count > 0)
                {
                    Debug.LogWarning(
                        "[QuickPlay] 以下场景在会话开始时是干净的，现在有未保存修改（原因未归因，QuickPlay 不会自动清除 dirty）：" +
                        string.Join(", ", newlyDirty));
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[QuickPlay] 检查场景 dirty 状态失败：" + exception.Message);
            }

            watch.Stop();

            if (cleanupComplete && !restorationUnconfirmed)
            {
                ResetRuntimeState(clearStore: true);
                Debug.Log($"[QuickPlay] 会话已结束（{reason}），清理耗时 {watch.ElapsedMilliseconds}ms。");
                return true;
            }

            // 清理未完成 / 恢复不可确认：保留记录，供后续恢复重试。
            record.phase = (int)QuickPlaySessionPhase.Cleaning;
            if (restorationUnconfirmed) record.residualOwnershipUncertain = true;
            if (!_store.TrySave(record, out var keepError))
            {
                Debug.LogError("[QuickPlay] 清理未完成且无法保留会话记录：" + keepError);
            }

            Debug.LogError("[QuickPlay] 清理未完成（" + reason + "）：" +
                           (errors.Count > 0 ? string.Join("；", errors) : "原对象 active 恢复无法确认") +
                           "。会话记录已保留，将在下次启动/重载时重试。");
            return false;
        }

        private static GameObject FindSingleOwnedClone(string sessionId)
        {
            var clones = QuickPlayCloneLocator.FindAll(sessionId);
            if (clones.Count == 1) return clones[0];

            foreach (var owned in OwnedClones)
            {
                if (owned != null && QuickPlayCloneLocator.IsOwnedClone(owned)) return owned;
            }

            return null;
        }

        // ---------------------------------------------------------------- 记录

        /// <summary>
        /// 持久化会话状态。critical=true 表示跨重载安全依赖该写入；失败必须安全终止会话。
        /// </summary>
        private static bool PersistSessionState(bool critical, string context)
        {
            if (_record == null) return true;

            if (!_store.TrySave(_record, out var error))
            {
                _durableWriteFailed = true;
                Debug.LogError("[QuickPlay] 关键会话状态写入失败（" + context + "）：" + error);

                if (!critical) return false;

                // 关键写入失败：不能"只 warning 继续"，也不能在同一次调用里再次依赖同一失败存储。
                _failureHandled = true;
                MarkSessionFailedInMemory("关键会话状态无法持久化（" + context + "）：" + error);
                ShowDialog("QuickPlay 无法保存会话状态",
                    "QuickPlay 无法持久化关键会话状态（" + context + "）：\n" + error +
                    "\n\n为避免重载后进入不一致状态，QuickPlay 将退出 Play 并清理本次会话。");
                SchedulePendingFailureExit();
                return false;
            }

            return true;
        }

        private static void ClearRecord()
        {
            _store.Clear();
        }

        private static void EnsureRecordLoaded()
        {
            if (_record != null) return;

            var loaded = _store.Load(out var error);
            if (loaded == null)
            {
                if (!string.IsNullOrEmpty(error))
                {
                    // 未知/损坏记录：保留原文、阻止自动恢复与新会话；SessionState 会在编辑器重启时清空。
                    Debug.LogError("[QuickPlay] 会话记录不可用（已保留原文，不会自动清除或恢复）：" + error);
                    _unreadableRecordError = error;
                }

                return;
            }

            _record = loaded;
        }

        private static void ShowDialog(string title, string message)
        {
            DialogPresenter(title, message);
        }
    }

    /// <summary>NDMF 全局开关的只读访问（QuickPlay 不会修改这些开关）。</summary>
    internal static class QuickPlayNdmfConfig
    {
        public static bool ApplyOnPlay
        {
            get
            {
                try
                {
                    return nadena.dev.ndmf.config.Config.ApplyOnPlay;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[QuickPlay] 无法读取 NDMF ApplyOnPlay 设置：" + exception.Message);
                    return false;
                }
            }
        }
    }
}
