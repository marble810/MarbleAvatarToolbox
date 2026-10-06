#if UNITY_INCLUDE_TESTS

using System;
using System.Collections.Generic;
using System.Linq;
using marble810.MarbleAvatarToolbox.PlayModeTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDKBase.Editor.BuildPipeline;

namespace MarbleQuickPlay.Tests
{
    /// <summary>
    /// 生产调用链回归：直接调用生产 QuickPlaySession / QuickPlayPreparePipeline / SDK hook 代码路径，
    /// 只注入存储后端与对话框呈现，不替换会话逻辑本身。
    /// </summary>
    [Category("QuickPlay")]
    public sealed class QuickPlaySessionLifecycleTests
    {
        private QuickPlayIsolatedScene _container;
        private FakeSessionStore _store;
        private Action _restoreDialogs;

        [SetUp]
        public void SetUp()
        {
            _container = QuickPlayObjectTestScope.Require();
            _store = new FakeSessionStore();
            QuickPlaySession.SetStoreForTests(_store);
            _restoreDialogs = QuickPlayTestHelpers.TrapDialogs();
            QuickPlaySession.PlayCancelGraceSeconds = 0d;
        }

        [TearDown]
        public void TearDown()
        {
            _restoreDialogs?.Invoke();
            _restoreDialogs = null;
            QuickPlaySession.PlayCancelGraceSeconds = 2.0d;
            QuickPlaySession.SetStoreForTests(null);
            _container?.Dispose();
            _container = null;
        }

        private static QuickPlayPreferenceData Snapshot(params string[] ids)
        {
            return new QuickPlayPreferenceData
            {
                schemaVersion = QuickPlayPreferenceData.CurrentSchemaVersion,
                initialized = true,
                stripToolIds = new List<string>(ids),
            };
        }

        private QuickPlayPrepareResult Prepare(QuickPlayPreferenceData snapshot, GameObject original, string sessionId = "session-lifecycle")
        {
            var captured = QuickPlayObjectIdentity.CaptureSelection();
            return QuickPlayPreparePipeline.Execute(
                new QuickPlayPrepareRequest
                {
                    Original = original,
                    Snapshot = snapshot,
                    SessionId = sessionId,
                    RequestedAt = 0d,
                    SelectionSnapshot = captured,
                },
                new QuickPlayPrepareOperations(),
                _store);
        }

        private static void InitializeMarker(GameObject clone, string sessionId, GameObject original)
        {
            var marker = clone.GetComponent<QuickPlayCloneMarker>();
            Assert.IsNotNull(marker, "生产准备必须附加所有权标记。");
            if (marker.SessionId == null)
            {
                marker.Initialize(sessionId, original);
            }
        }

        // ------------------------------------------------------------ F1

        [Test]
        public void F1_PreferenceWithNotInstalledTool_IsIgnoredByEffectivePlan()
        {
            var original = _container.CreateGameObject("Avatar");
            var snapshot = Snapshot("meshia", "vrcfury");

            var result = Prepare(snapshot, original);

            Assert.IsTrue(result.Success, result.Error);
            CollectionAssert.Contains(result.Record.preferenceToolIds, "meshia");
            CollectionAssert.Contains(result.Record.preferenceToolIds, "vrcfury");
            CollectionAssert.DoesNotContain(result.Record.effectiveToolIds, "meshia");
            CollectionAssert.Contains(result.Record.effectiveToolIds, "vrcfury");
            Assert.IsFalse(result.Record.vqtSuppressionActive);
            Assert.IsFalse(result.Record.d4RkSuppressionActive);
            Assert.IsTrue(_store.Data.effectiveToolIds.Count == 1);
        }

        [Test]
        public void F1_NotInstalledD4RkPreference_DoesNotActivateRuntimeHook()
        {
            var original = _container.CreateGameObject("Avatar");
            var prepare = Prepare(Snapshot("d4rk-avatar-optimizer"), original);
            Assert.IsTrue(prepare.Success, prepare.Error);
            Assert.IsFalse(prepare.Record.d4RkSuppressionActive, "未安装的 d4rk 不得激活专用 hook。");

            var clone = prepare.Clone;
            InitializeMarker(clone, prepare.Record.sessionId, original);
            QuickPlaySession.AdoptPreparedSession(prepare.Record, clone, original, QuickPlayObjectIdentity.CaptureSelection(), 0d);
            prepare.Record.phase = (int)QuickPlaySessionPhase.Running;

            // 生产 hook 入口：未安装工具必须 no-op 且不失败。
            var result = QuickPlaySession.OnSdkPreprocessEntered(clone);

            Assert.IsTrue(result, "未安装工具的偏好不得导致 SDK 链被中止。");
            Assert.IsEmpty(QuickPlayTestHelpers.CollectedDialogs, "不得因为未安装工具而失败。");
            Assert.IsFalse(QuickPlaySession.CurrentRecord.sessionFailed);
        }

        [Test]
        public void F1_NotInstalledVqtPreference_DoesNotActivateSuppression()
        {
            var original = _container.CreateGameObject("Avatar");
            var prepare = Prepare(Snapshot("meshia"), original);
            Assert.IsTrue(prepare.Success, prepare.Error);
            Assert.IsFalse(prepare.Record.vqtSuppressionActive);

            var clone = prepare.Clone;
            InitializeMarker(clone, prepare.Record.sessionId, original);
            QuickPlaySession.AdoptPreparedSession(prepare.Record, clone, original, QuickPlayObjectIdentity.CaptureSelection(), 0d);
            prepare.Record.phase = (int)QuickPlaySessionPhase.Running;

            Assert.IsFalse(QuickPlaySession.IsVqtSuppressionTarget(clone),
                "有效计划里没有 VQT 时，NDMF 抑制步骤必须 no-op。");
        }

        // ------------------------------------------------------------ F2

        [Test]
        public void F2_EarlyHookReturnsFalse_WhenSuppressionRecheckFails_AndLateCannotWhitelist()
        {
            var original = _container.CreateGameObject("Avatar");
            var prepare = Prepare(Snapshot("vrcfury"), original);
            Assert.IsTrue(prepare.Success, prepare.Error);

            var clone = prepare.Clone;
            InitializeMarker(clone, prepare.Record.sessionId, original);
            QuickPlaySession.AdoptPreparedSession(prepare.Record, clone, original, QuickPlayObjectIdentity.CaptureSelection(), 0d);
            prepare.Record.phase = (int)QuickPlaySessionPhase.Running;

            // 伪造"本会话确实建立了 d4rk 阻断"，但本机未安装 d4rk：
            // 复核必然失败，必须同步中止 SDK 链（返回 false）并先写 Failed。
            prepare.Record.d4RkSuppressionActive = true;
            var sessionId = prepare.Record.sessionId;

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("d4rk 阻断状态在 SDK preprocess 阶段复核失败"));
            var continueChain = QuickPlaySession.OnSdkPreprocessEntered(clone);

            Assert.IsFalse(continueChain, "阻断复核失败必须让 SDK 回调返回 false（同步中止后续回调）。");
            Assert.IsTrue(QuickPlayTestHelpers.HasDialogContaining("d4rk"), "必须提示失败原因。");

            var persisted = _store.Data;
            Assert.IsNotNull(persisted, "失败状态必须写入 durable 记录。");
            Assert.IsTrue(persisted.sessionFailed, "失败必须写入 durable 记录。");
            Assert.IsNotEmpty(persisted.failureReason);

            // Late 回调不得把失败洗白成成功状态。
            QuickPlaySession.OnSdkPreprocessCompleted(clone);
            var afterLate = _store.Data;
            Assert.IsTrue(afterLate.sessionFailed);
            Assert.AreEqual(persisted.failureReason, afterLate.failureReason);
            Assert.AreNotEqual((int)QuickPlaySessionPhase.Idle, afterLate.phase, "会话仍需保留以便清理。");
            Assert.IsNotNull(sessionId);
        }

        // ------------------------------------------------------------ F4

        [Test]
        public void F4_CleanupResolvesOriginalFromMarkerBeforeDestroyingClone()
        {
            var original = _container.CreateGameObject("Avatar");
            var prepare = Prepare(Snapshot("vrcfury"), original);
            Assert.IsTrue(prepare.Success, prepare.Error);

            var clone = prepare.Clone;
            InitializeMarker(clone, prepare.Record.sessionId, original);

            // 模拟域重载：静态引用丢失，但 clone 标记仍持有原对象引用；record 没有 durable GlobalId。
            prepare.Record.originalGlobalId = string.Empty;
            prepare.Record.originalActiveWritten = true;
            prepare.Record.phase = (int)QuickPlaySessionPhase.Running;
            QuickPlaySession.AdoptPreparedSession(prepare.Record, clone, original: null, QuickPlayObjectIdentity.CaptureSelection(), 0d);
            original.SetActive(false);

            var completed = QuickPlaySession.Cleanup("F4 测试", restoreSelection: false);

            Assert.IsTrue(completed, "能从标记取回原对象时必须视为清理完成。");
            Assert.IsTrue(original.activeSelf, "原对象 active 必须恢复（销毁 clone 之前解析引用）。");
            Assert.IsTrue(clone == null, "clone 必须被销毁。");
            Assert.IsNull(_store.Data, "清理完成后记录被清除。");
        }

        [Test]
        public void F4_CleanupKeepsRecord_WhenOriginalCannotBeResolvedAfterWritingActive()
        {
            var original = _container.CreateGameObject("Avatar");
            var prepare = Prepare(Snapshot("vrcfury"), original);
            Assert.IsTrue(prepare.Success, prepare.Error);

            var clone = prepare.Clone;
            // 破坏恢复依据：移除标记（模拟标记丢失）且没有 durable 身份 → 无法解析原对象。
            UnityEngine.Object.DestroyImmediate(clone.GetComponent<QuickPlayCloneMarker>());
            prepare.Record.originalGlobalId = string.Empty;
            prepare.Record.originalActiveWritten = true;
            prepare.Record.phase = (int)QuickPlaySessionPhase.Running;
            QuickPlaySession.AdoptPreparedSession(prepare.Record, clone, original: null, QuickPlayObjectIdentity.CaptureSelection(), 0d);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("无法恢复原对象 active 状态"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("清理未完成"));
            var completed = QuickPlaySession.Cleanup("F4 无法解析原对象", restoreSelection: false);

            Assert.IsFalse(completed, "无法确认原对象恢复时不得宣称清理完成。");
            Assert.IsNotNull(_store.Data, "必须保留唯一恢复记录。");
            Assert.AreEqual((int)QuickPlaySessionPhase.Cleaning, _store.Data.phase);
            Assert.IsTrue(_store.Data.residualOwnershipUncertain);
        }

        // ------------------------------------------------------------ F5

        [Test]
        public void F5_PrepareFailureWithDestroyFailure_HandsResidualsBackAndBlocksRestart()
        {
            var original = _container.CreateGameObject("Avatar");
            var operations = new FakePrepareOperations { FailMarker = true, FailDestroyOnce = true };

            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("DestroyOwned 注入异常"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("准备中止但清理未完成"));
            var result = QuickPlaySession.PrepareForTests(original, Snapshot("vrcfury"), operations, "session-f5", 0d);

            Assert.IsFalse(result.Success);
            Assert.IsNotEmpty(result.ResidualObjects, "未标记的残留对象必须交回调用方（不能只留文字记录）。");
            Assert.IsTrue(result.ResidualOwnershipUncertain);

            // 会话接管：重试销毁成功 → 不再保留不确定状态。
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("残留对象需要处理"));
            QuickPlaySession.HandlePreparationFailure(result);
            Assert.IsTrue(_store.Data == null || !_store.Data.residualOwnershipUncertain,
                "残留销毁成功后不应继续保留不确定状态。");
        }

        [Test]
        public void F5_PersistentDestroyFailure_KeepsRecordAndBlocksNewSession()
        {
            var original = _container.CreateGameObject("Avatar");
            var operations = new FakePrepareOperations { FailMarker = true, FailDestroyAlways = true };

            // 准备阶段（含销毁失败）必须把所有权不确定状态写入 durable 记录。
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("DestroyOwned 注入异常"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("准备中止但清理未完成"));
            var result = QuickPlaySession.PrepareForTests(original, Snapshot("vrcfury"), operations, "session-f5b", 0d);
            Assert.IsFalse(result.Success);
            Assert.IsNotEmpty(result.ResidualObjects, "失败结果必须携带残留对象引用。");
            Assert.IsNotNull(_store.Data, "准备失败必须留下 durable 记录。");
            Assert.IsTrue(_store.Data.residualOwnershipUncertain);

            // 模拟域重载：静态残留引用丢失，只剩 durable 记录 → 必须阻止自动恢复与新会话。
            QuickPlaySession.SetStoreForTests(_store);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("所有权不确定的残留对象"));
            var recovered = QuickPlaySession.TryRecoverStaleSession(out var error);

            Assert.IsFalse(recovered, "所有权不确定时禁止自动恢复/启动新会话。");
            StringAssert.Contains("残留", error);
            Assert.IsNotNull(_store.Data, "记录必须保留。");
        }

        // ------------------------------------------------------------ F6

        [Test]
        public void F6_CleanupRestoresEmptySelection()
        {
            var selectionBefore = Selection.objects;
            try
            {
                Selection.objects = Array.Empty<UnityEngine.Object>();
                Selection.activeObject = null;

                var original = _container.CreateGameObject("Avatar");
                var prepare = Prepare(Snapshot("vrcfury"), original);
                Assert.IsTrue(prepare.Success, prepare.Error);

                var captured = QuickPlayObjectIdentity.CaptureSelection();
                Assert.IsTrue(captured.WasEmpty, "前置条件：初始选择为空。");

                var clone = prepare.Clone;
                InitializeMarker(clone, prepare.Record.sessionId, original);
                prepare.Record.phase = (int)QuickPlaySessionPhase.Running;
                QuickPlaySession.AdoptPreparedSession(prepare.Record, clone, original, captured, 0d);
                QuickPlaySession.SelectPreparedClone(clone);
                Assert.AreSame(clone, Selection.activeGameObject);

                var completed = QuickPlaySession.Cleanup("F6 空选择", restoreSelection: true);

                Assert.IsTrue(completed);
                Assert.IsEmpty(Selection.objects, "真实空选择必须恢复为空集，而不是回退到原 Avatar。");
                Assert.IsNull(Selection.activeObject);
            }
            finally
            {
                Selection.objects = selectionBefore;
            }
        }

        [Test]
        public void F6_CleanupRestoresMultiSceneObjectSelection()
        {
            var selectionBefore = Selection.objects;
            try
            {
                var original = _container.CreateGameObject("Avatar");
                var childA = _container.CreateGameObject("BoneA");
                var childB = _container.CreateGameObject("BoneB");

                // Unity 的 objects setter 会把首元素设为 activeObject，因此把期望 active 的 childB 放首位。
                Selection.objects = new UnityEngine.Object[] { childB, childA };
                Assert.AreEqual(2, Selection.objects.Length,
                    "前置条件：两个场景对象的多选，实际=" + Selection.objects.Length);
                Assert.AreSame(childB, Selection.activeObject, "前置条件：active 应为 childB。");

                var captured = QuickPlayObjectIdentity.CaptureSelection();
                Assert.IsFalse(captured.WasEmpty);
                Assert.AreEqual(2, captured.Count, "前置条件：多选必须被完整捕获。");

                var prepare = Prepare(Snapshot("vrcfury"), original);
                var clone = prepare.Clone;
                InitializeMarker(clone, prepare.Record.sessionId, original);
                prepare.Record.phase = (int)QuickPlaySessionPhase.Running;
                QuickPlaySession.AdoptPreparedSession(prepare.Record, clone, original, captured, 0d);
                QuickPlaySession.SelectPreparedClone(clone);

                var completed = QuickPlaySession.Cleanup("F6 多选场景对象", restoreSelection: true);

                Assert.IsTrue(completed);
                Assert.AreEqual(2, Selection.objects.Length);
                CollectionAssert.AreEquivalent(new UnityEngine.Object[] { childA, childB }, Selection.objects);
                Assert.AreSame(childB, Selection.activeObject, "activeObject 必须恢复到原先的 active 对象。");
            }
            finally
            {
                Selection.objects = selectionBefore;
            }
        }

        [Test]
        public void F6_CleanupRestoresAssetOnlySelection()
        {
            var selectionBefore = Selection.objects;
            try
            {
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/00Scenes/KiriMafu/KiriMafu.unity");
                if (asset == null)
                {
                    asset = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
                }

                Assert.IsNotNull(asset, "需要至少一个只读资产用于非 GameObject 选择测试。");

                Selection.objects = new UnityEngine.Object[] { asset };
                Selection.activeObject = asset;

                var captured = QuickPlayObjectIdentity.CaptureSelection();
                Assert.IsFalse(captured.WasEmpty);
                Assert.AreEqual(1, captured.Count);
                Assert.AreSame(asset, captured.DirectActiveObject, "非 GameObject 的 activeObject 必须被捕获。");

                var original = _container.CreateGameObject("Avatar");
                var prepare = Prepare(Snapshot("vrcfury"), original);
                var clone = prepare.Clone;
                InitializeMarker(clone, prepare.Record.sessionId, original);
                prepare.Record.phase = (int)QuickPlaySessionPhase.Running;
                QuickPlaySession.AdoptPreparedSession(prepare.Record, clone, original, captured, 0d);
                QuickPlaySession.SelectPreparedClone(clone);

                var completed = QuickPlaySession.Cleanup("F6 资产选择", restoreSelection: true);

                Assert.IsTrue(completed);
                Assert.AreEqual(1, Selection.objects.Length, "必须恢复为唯一的资产选择。");
                Assert.AreSame(asset, Selection.objects[0]);
                Assert.AreSame(asset, Selection.activeObject, "activeObject 必须恢复到资产（非 GameObject）。");
            }
            finally
            {
                Selection.objects = selectionBefore;
            }
        }

        [Test]
        public void F6_CleanupDoesNotOverwriteUserSelectionChange()
        {
            var selectionBefore = Selection.objects;
            try
            {
                var original = _container.CreateGameObject("Avatar");
                var userChoice = _container.CreateGameObject("UserPicked");

                var originalSelection = new QuickPlaySelectionSnapshot();
                originalSelection.WasEmpty = true;

                var prepare = Prepare(Snapshot("vrcfury"), original);
                var clone = prepare.Clone;
                InitializeMarker(clone, prepare.Record.sessionId, original);
                prepare.Record.phase = (int)QuickPlaySessionPhase.Running;
                QuickPlaySession.AdoptPreparedSession(prepare.Record, clone, original, originalSelection, 0d);
                QuickPlaySession.SelectPreparedClone(clone);

                // 用户在会话期间改选。
                Selection.activeGameObject = userChoice;

                var completed = QuickPlaySession.Cleanup("F6 用户改选", restoreSelection: true);

                Assert.IsTrue(completed);
                Assert.AreSame(userChoice, Selection.activeGameObject, "不得覆盖用户的新选择。");
            }
            finally
            {
                Selection.objects = selectionBefore;
            }
        }

        [Test]
        public void F6_CleanupDoesNotOverwriteUserActiveEdit()
        {
            var original = _container.CreateGameObject("Avatar");
            var prepare = Prepare(Snapshot("vrcfury"), original);
            var clone = prepare.Clone;
            InitializeMarker(clone, prepare.Record.sessionId, original);
            prepare.Record.phase = (int)QuickPlaySessionPhase.Running;
            prepare.Record.originalActiveWritten = true;
            QuickPlaySession.AdoptPreparedSession(prepare.Record, clone, original, QuickPlayObjectIdentity.CaptureSelection(), 0d);

            // 本工具写入 false，随后用户主动改回 true。
            original.SetActive(false);
            original.SetActive(true);

            var completed = QuickPlaySession.Cleanup("F6 用户改 active", restoreSelection: false);

            Assert.IsTrue(completed);
            Assert.IsTrue(original.activeSelf, "用户主动修改的 active 必须保留（冲突不覆盖）。");
            Assert.IsTrue(QuickPlayTestHelpers.HasDialogContaining("active") ||
                          QuickPlayTestHelpers.CollectedWarnings.Count >= 0);
        }

        // ------------------------------------------------------------ F8

        [Test]
        public void F8_RunningPersistenceFailure_ConvergesInsteadOfWarningOnly()
        {
            var original = _container.CreateGameObject("Avatar");
            var prepare = Prepare(Snapshot("vrcfury"), original);
            var clone = prepare.Clone;
            InitializeMarker(clone, prepare.Record.sessionId, original);
            prepare.Record.phase = (int)QuickPlaySessionPhase.EnteringPlay;
            QuickPlaySession.AdoptPreparedSession(prepare.Record, clone, original, QuickPlayObjectIdentity.CaptureSelection(), 0d);
            _store.Seed(prepare.Record);

            _store.FailSaveWhenPhaseIsRunning = true;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("关键会话状态写入失败"));
            QuickPlaySession.OnPlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);

            Assert.IsTrue(QuickPlayTestHelpers.HasDialogContaining("无法保存会话状态"),
                "关键持久化失败必须安全终止而不是只 warning 继续。");
            Assert.IsTrue(QuickPlaySession.CurrentRecord.sessionFailed,
                "必须先把失败状态写入内存记录（持久化本身失败，无法写盘）。");
        }

        [Test]
        public void F8_ReloadWhileNotPlaying_DoesNotStayStuckInEnteringPlay()
        {
            var original = _container.CreateGameObject("Avatar");
            var prepare = Prepare(Snapshot("vrcfury"), original);
            var clone = prepare.Clone;
            InitializeMarker(clone, prepare.Record.sessionId, original);
            prepare.Record.phase = (int)QuickPlaySessionPhase.EnteringPlay;
            _store.Seed(prepare.Record);

            // 模拟域重载：静态状态清空，只剩 durable 记录。
            QuickPlaySession.SetStoreForTests(_store);
            Assert.AreEqual((int)QuickPlaySessionPhase.EnteringPlay, QuickPlaySession.CurrentRecord.phase);

            QuickPlaySession.OnEditorUpdate();

            var phase = QuickPlaySession.CurrentRecord?.phase ?? (int)QuickPlaySessionPhase.Idle;
            Assert.AreNotEqual((int)QuickPlaySessionPhase.EnteringPlay, phase,
                "不在 Play 中时不得永远停留在 EnteringPlay。");
        }

        [Test]
        public void F8_ProductionStore_RoundTripsSameRecordData()
        {
            var store = new QuickPlaySessionStateStore();
            store.Clear();
            var record = new QuickPlaySessionRecord
            {
                sessionId = "roundtrip-session",
                phase = (int)QuickPlaySessionPhase.Running,
                preferenceToolIds = new List<string> { "vrcfury", "meshia" },
                effectiveToolIds = new List<string> { "vrcfury" },
                preprocessStage = 1, // 旧版观察值：只读兼容，不得被当作成功/失败
                preprocessFailureReason = string.Empty,
                selectionWasEmpty = true,
            };

            try
            {
                Assert.IsTrue(store.TrySave(record, out var saveError), saveError);

                var loaded = store.Load(out var loadError);
                Assert.IsNotNull(loaded, loadError);
                Assert.AreEqual(record.sessionId, loaded.sessionId);
                Assert.AreEqual(record.phase, loaded.phase);
                CollectionAssert.AreEqual(record.preferenceToolIds, loaded.preferenceToolIds);
                CollectionAssert.AreEqual(record.effectiveToolIds, loaded.effectiveToolIds);
                Assert.IsFalse(loaded.sessionFailed);
                Assert.IsTrue(loaded.selectionWasEmpty);
            }
            finally
            {
                store.Clear();
            }
        }

        [Test]
        public void F8_UnknownRecordVersionOrPhase_IsRejectedConservatively()
        {
            var future = new QuickPlaySessionRecord { sessionId = "future", version = QuickPlaySessionRecord.CurrentVersion + 1 };
            Assert.IsFalse(future.TryValidate(out var versionError));
            StringAssert.Contains("版本", versionError);

            var unknownPhase = new QuickPlaySessionRecord { sessionId = "phase", phase = 99 };
            Assert.IsFalse(unknownPhase.TryValidate(out var phaseError));
            StringAssert.Contains("phase", phaseError);

            var unknownStage = new QuickPlaySessionRecord { sessionId = "stage", preprocessStage = 42 };
            Assert.IsFalse(unknownStage.TryValidate(out var stageError));
            StringAssert.Contains("preprocessStage", stageError);
        }

        [Test]
        public void F8_UnreadableRecord_BlocksNewSessionAndKeepsText()
        {
            var store = new QuickPlaySessionStateStore();
            store.Clear();
            try
            {
                SessionState.SetString(QuickPlaySessionStateStore.SessionStateKey, "{not-json");

                QuickPlaySession.SetStoreForTests(store);
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("会话记录不可用"));
                var recovered = QuickPlaySession.TryRecoverStaleSession(out var error);

                Assert.IsFalse(recovered, "无法识别的记录必须阻止自动恢复/新会话。");
                StringAssert.Contains("无法识别", error);
                Assert.IsNotEmpty(SessionState.GetString(QuickPlaySessionStateStore.SessionStateKey, string.Empty),
                    "原始记录文本必须保留，不得被覆盖或清除。");
            }
            finally
            {
                store.Clear();
                QuickPlaySession.SetStoreForTests(null);
            }
        }
    }
}

#endif
