#if UNITY_INCLUDE_TESTS

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using marble810.MarbleAvatarToolbox.PlayModeTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MarbleQuickPlay.Tests
{
    /// <summary>
    /// QuickPlay 基础发行版（不含 SDK 整链结果观测）的定向回归。
    ///
    /// 这些测试直接驱动生产路径（Start / QuickPlayPreparePipeline / Tick / SDK hooks / Cleanup），
    /// 只替换存储、对话框与 EnterPlay/ExitPlay 入口（seam），并且：
    /// - 不进入真实 Play、不 Build/Upload、不使用用户 Avatar；
    /// - 不手工调用 SDK preprocess，也不观测/推断 SDK 构建结果；
    /// - 所有对象都创建在测试自有的隔离容器内。
    ///
    /// 由于本机不能用 Unity Test Runner（它会关闭/重载用户未保存场景），
    /// 这些方法由 <see cref="QuickPlayDirectTestRunner"/> 直接执行。
    /// </summary>
    [Category("QuickPlay")]
    public sealed class QuickPlayBasicReleaseTests
    {
        private QuickPlayIsolatedScene _container;
        private FakeSessionStore _store;
        private Action _restoreDialogs;
        private Action<Action> _previousScheduler;
        private Action _previousExitInvoker;
        private Action _previousEnterInvoker;
        private List<Action> _scheduled;
        private int _exitRequests;

        [SetUp]
        public void SetUp()
        {
            _container = QuickPlayObjectTestScope.Require();
            _store = new FakeSessionStore();
            QuickPlaySession.SetStoreForTests(_store);
            _restoreDialogs = QuickPlayTestHelpers.TrapDialogs();

            _previousScheduler = QuickPlaySession.DeferredActionScheduler;
            _previousExitInvoker = QuickPlaySession.ExitPlayInvoker;
            _previousEnterInvoker = QuickPlaySession.EnterPlayInvoker;

            _scheduled = new List<Action>();
            _exitRequests = 0;
            QuickPlaySession.DeferredActionScheduler = action => _scheduled.Add(action);
            QuickPlaySession.ExitPlayInvoker = () => _exitRequests++;
            QuickPlaySession.EnterPlayInvoker = () => { };

            QuickPlaySession.PlayCancelGraceSeconds = 0d;

            // 不读写用户真实配置：用内存替身替换偏好实例（必须 Load 才会应用默认值）。
            var testPreferences = new QuickPlayPreferences(new FakePersistence());
            testPreferences.Load();
            QuickPlaySettingsWindow.ResetForTests(testPreferences);

            QuickPlayLogCapture.Install();
            QuickPlayLogCapture.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            QuickPlaySettingsWindow.ResetForTests(null);
            QuickPlaySession.PlayCancelGraceSeconds = 2.0d;
            QuickPlaySession.DeferredActionScheduler = _previousScheduler;
            QuickPlaySession.ExitPlayInvoker = _previousExitInvoker;
            QuickPlaySession.EnterPlayInvoker = _previousEnterInvoker;
            _restoreDialogs?.Invoke();
            _restoreDialogs = null;
            QuickPlaySession.SetStoreForTests(null);
            QuickPlayLogCapture.Uninstall();
            _container?.Dispose();
            _container = null;
        }

        private GameObject CreateAvatar(string name)
        {
            var avatar = _container.CreateGameObject(name);
            var descriptorType = QuickPlayTestTypes.Find("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            Assert.IsNotNull(descriptorType, "本工程必须安装 VRC SDK Avatars（VRCAvatarDescriptor）。");
            avatar.AddComponent(descriptorType);
            return avatar;
        }

        private static QuickPlayPreferenceData Snapshot(params string[] toolIds)
        {
            return new QuickPlayPreferenceData
            {
                schemaVersion = QuickPlayPreferenceData.CurrentSchemaVersion,
                initialized = true,
                stripToolIds = new List<string>(toolIds),
            };
        }

        private QuickPlayPrepareResult PrepareRunningSession(string sessionId, params string[] toolIds)
        {
            var original = CreateAvatar("Avatar-" + sessionId);
            var captured = QuickPlayObjectIdentity.CaptureSelection();
            var snapshot = Snapshot(toolIds.Length == 0 ? new[] { QuickPlayToolRegistry.VrcFuryId } : toolIds);

            var result = QuickPlayPreparePipeline.Execute(
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

            Assert.IsTrue(result.Success, result.Error);
            QuickPlaySession.AdoptPreparedSession(result.Record, result.Clone, original, captured, 0d);
            result.Record.phase = (int)QuickPlaySessionPhase.Running;
            _store.Seed(result.Record);
            return result;
        }

        // ------------------------------------------------------------ 基础启动

        [Test]
        public void BasicStart_PreparesCloneAndRequestsEnterPlayOnce_WithoutSdkObservationGate()
        {
            var avatar = CreateAvatar("BasicStartAvatar");
            Selection.activeGameObject = avatar;

            var enterRequests = 0;
            var recordAtEnter = new QuickPlaySessionRecord();
            GameObject cloneAtEnter = null;
            var cloneHadMarkerAtEnter = false;
            QuickPlaySession.EnterPlayInvoker = () =>
            {
                enterRequests++;
                var live = QuickPlaySession.CurrentRecord;
                Assert.IsNotNull(live, "请求 EnterPlay 前必须已有会话记录。");
                // 复制关键字段（记录对象在回滚清理时会被就地修改）。
                recordAtEnter.sessionId = live.sessionId;
                recordAtEnter.phase = live.phase;
                recordAtEnter.preferenceToolIds = new List<string>(live.preferenceToolIds);
                cloneAtEnter = QuickPlayCloneLocator.FindAll(live.sessionId).FirstOrDefault();
                cloneHadMarkerAtEnter = cloneAtEnter != null && cloneAtEnter.GetComponent<QuickPlayCloneMarker>() != null;
            };

            QuickPlaySession.Start(avatar);

            Assert.AreEqual(1, enterRequests, "基础版必须只请求一次 EnterPlay，且不得被 SDK 观测门禁拦住。");
            Assert.AreEqual((int)QuickPlaySessionPhase.EnteringPlay, recordAtEnter.phase);
            Assert.IsNotEmpty(recordAtEnter.sessionId);
            CollectionAssert.Contains(recordAtEnter.preferenceToolIds, QuickPlayToolRegistry.VrcFuryId);
            Assert.IsTrue(cloneHadMarkerAtEnter, "请求 EnterPlay 时必须已存在带会话所有权标记的临时副本。");
            Assert.IsFalse(QuickPlayTestHelpers.HasDialogContaining("暂不可启动"), "不得再出现开发启动门禁。");
            Assert.IsFalse(QuickPlayLogCapture.HasError("门禁"), "不得再出现门禁文案。");

            // seam 没有真实进入 Play：生产路径按「进入 Play 失败」回滚，证明准备/剔除/清理都走真实路径。
            Assert.IsTrue(QuickPlayTestHelpers.HasDialogContaining("进入 Play 失败"), "未真实进入 Play 时必须回滚。");
            Assert.IsTrue(cloneAtEnter == null, "回滚必须销毁临时副本。");
            Assert.IsNull(_store.Data, "回滚完成后会话记录必须清除。");
            Assert.IsTrue(avatar.activeSelf, "原对象 active 必须恢复。");
        }

        [Test]
        public void BasicStart_WithNoToolSelected_IsStillAllowed()
        {
            var persistence = new FakePersistence();
            persistence.Seed(new QuickPlayPreferenceData
            {
                schemaVersion = QuickPlayPreferenceData.CurrentSchemaVersion,
                initialized = true,
                stripToolIds = new List<string>(),
            });
            var loadedPreferences = new QuickPlayPreferences(persistence);
            loadedPreferences.Load();
            QuickPlaySettingsWindow.ResetForTests(loadedPreferences);

            var avatar = CreateAvatar("EmptyPrefsAvatar");
            var enterRequests = 0;
            QuickPlaySession.EnterPlayInvoker = () => enterRequests++;

            QuickPlaySession.Start(avatar);

            Assert.AreEqual(1, enterRequests, "全部不勾选仍是合法配置。");
            Assert.IsTrue(QuickPlayTestHelpers.HasDialogContaining("进入 Play 失败"));
            Assert.IsNull(_store.Data);
        }

        [Test]
        public void Preflight_RejectsWhenApplyOnPlayDisabled()
        {
            var input = new QuickPlayPreflightInput
            {
                ApplyOnPlayEnabled = false,
                Target = new QuickPlayTargetResolution { AvatarRoot = CreateAvatar("ApplyOnPlayAvatar") },
                Snapshot = Snapshot(),
            };

            var check = QuickPlayPreflight.Evaluate(input);

            Assert.IsFalse(check.CanStart);
            StringAssert.Contains("Apply on Play", check.Error);
        }

        [Test]
        public void TargetSelection_RejectsOwnedCloneAndIneligibleAvatar()
        {
            var ownedClone = _container.CreateGameObject("OwnedClone");
            ownedClone.AddComponent<QuickPlayCloneMarker>().Initialize("other-session", null);

            var rejectClone = QuickPlayTargetSelectionPolicy.Evaluate(
                new QuickPlaySelectionFacts { HasSelection = true, SelectionIsOwnedClone = true }, out _);
            Assert.IsNotNull(rejectClone, "选中 QuickPlay 自己的副本必须拒绝。");
            StringAssert.Contains("临时副本", rejectClone);

            var rejectIneligible = QuickPlayTargetSelectionPolicy.Evaluate(
                new QuickPlaySelectionFacts
                {
                    HasSelection = true,
                    FoundAvatarInParents = true,
                    AvatarEligible = false,
                    AvatarIneligibleReason = "目标是预览场景对象。",
                },
                out _);
            Assert.IsNotNull(rejectIneligible, "父链中不可用的 Avatar 必须拒绝且不回退。");
            StringAssert.Contains("预览场景", rejectIneligible);

            QuickPlayTargetSelectionPolicy.Evaluate(new QuickPlaySelectionFacts { HasSelection = false }, out var useFallback);
            Assert.IsTrue(useFallback, "只有完全没有有效选择时才允许回退到场景唯一候选。");
        }

        [Test]
        public void UnreliableStore_PrepareAbortsBeforeAnySceneChange()
        {
            var avatar = CreateAvatar("StoreFailureAvatar");
            _store.FailSave = true;

            var result = QuickPlaySession.PrepareForTests(
                avatar,
                Snapshot(QuickPlayToolRegistry.VrcFuryId),
                new QuickPlayPrepareOperations(),
                "session-store-fail",
                0d);

            Assert.IsFalse(result.Success, "存储不可靠时必须拒绝启动。");
            StringAssert.Contains("无法写入会话记录", result.Error);
            Assert.IsTrue(avatar.activeSelf, "存储不可靠时不得修改原对象。");
            Assert.IsNull(result.Clone, "不得留下副本引用。");
            Assert.IsFalse(_container.Scene.GetRootGameObjects().Any(root => root.name.Contains("QuickPlay Clone")),
                "不得在场景中留下临时副本。");
            Assert.AreEqual(0, _store.ClearCount, "失败发生在写记录之前，没有需要清除的记录。");
        }

        // ------------------------------------------------------------ 无 SDK 结果观测

        [Test]
        public void NoSdkCallbacks_DoNotTriggerFallbackTimeoutOrExit()
        {
            var prepare = PrepareRunningSession("session-no-late");
            var clone = prepare.Clone;

            // 其它 Avatar 的链活动：非目标必须完全 no-op。
            var unrelated = CreateAvatar("UnrelatedAvatar");
            Assert.IsTrue(QuickPlaySession.OnSdkPreprocessEntered(unrelated), "非目标不得被中止。");
            QuickPlaySession.OnSdkPreprocessCompleted(unrelated);

            // 目标进入 Early（本会话没有 d4rk 计划）：只是工具协作，不构成成功或失败。
            Assert.IsTrue(QuickPlaySession.OnSdkPreprocessEntered(clone));

            // 长时间推进：基础版没有 SDK 等待、超时、补发或成功推断路径。
            for (var i = 0; i < 50; i++)
            {
                QuickPlaySession.Tick(isPlaying: true, isPlayingOrWillChange: true, now: 1000d + i * 10d);
            }

            Assert.IsNotNull(_store.Data);
            Assert.IsFalse(_store.Data.sessionFailed, "没有回调到达不得被判定为失败。");
            Assert.AreEqual((int)QuickPlaySessionPhase.Running, _store.Data.phase, "会话必须保持 Running。");
            Assert.IsEmpty(QuickPlayTestHelpers.CollectedDialogs, "不得弹出任何失败或门禁提示。");
            Assert.AreEqual(0, _exitRequests, "不得请求退出 Play。");
            Assert.IsEmpty(_scheduled, "不得挂起失败延迟动作。");
            Assert.IsEmpty(QuickPlayLogCapture.Errors, "不得记录任何错误。");
        }

        [Test]
        public void LateHook_OnRegisteredTarget_IsOnlyPlayabilityCooperation()
        {
            var prepare = PrepareRunningSession("session-late");
            var clone = prepare.Clone;

            QuickPlaySession.OnSdkPreprocessCompleted(clone);

            Assert.IsFalse(_store.Data.sessionFailed, "收尾回调到达不得被当作失败或成功判定。");
            Assert.IsEmpty(QuickPlayTestHelpers.CollectedDialogs);
            Assert.AreEqual(0, _exitRequests);
            Assert.IsEmpty(_scheduled);
        }

        [Test]
        public void KnownD4RkRecheckFailure_StillAbortsSdkChainAndPersistsFailure()
        {
            var prepare = PrepareRunningSession("session-d4rk-fail");
            prepare.Record.d4RkSuppressionActive = true;
            _store.Seed(prepare.Record);

            var continued = QuickPlaySession.OnSdkPreprocessEntered(prepare.Clone);

            Assert.IsFalse(continued, "d4rk 复核失败必须同步中止 SDK 回调链。");
            Assert.IsTrue(_store.Data.sessionFailed, "本工具已知失败必须持久化。");
            Assert.IsNotEmpty(_store.Data.failureReason);
            Assert.IsTrue(QuickPlayTestHelpers.HasDialogContaining("d4rk"), "必须提示失败原因。");
            Assert.AreEqual(1, _scheduled.Count, "失败必须挂起自己的退出动作。");
            Assert.IsTrue(QuickPlayLogCapture.HasError("d4rk"));
        }

        [Test]
        public void CriticalPersistFailure_FailsClosedWithOwnExitAction()
        {
            var prepare = PrepareRunningSession("session-persist-fail");
            prepare.Record.phase = (int)QuickPlaySessionPhase.EnteringPlay;
            _store.Seed(prepare.Record);

            _store.FailSaveWhenPhaseIsRunning = true;
            QuickPlaySession.OnPlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);

            Assert.IsTrue(QuickPlayTestHelpers.HasDialogContaining("无法保存会话状态"), "关键持久化失败必须安全终止。");
            Assert.IsTrue(QuickPlaySession.CurrentRecord.sessionFailed, "失败状态必须写入内存记录。");
            Assert.AreEqual(1, _scheduled.Count, "必须挂起自己的退出动作。");
            Assert.AreEqual(0, _exitRequests, "延迟动作不得同步退出 Play。");

            _scheduled[0]();
            Assert.IsNull(QuickPlaySession.CurrentRecord, "同一失败会话的退出动作必须完成清理。");
        }

        [Test]
        public void ReleasePackage_ContainsNoSdkObservationPrototypeOrManualSdkCall()
        {
            var assembly = typeof(QuickPlaySession).Assembly;

            var suspiciousTypes = assembly.GetTypes()
                .Where(type => type.Name.IndexOf("Observation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               type.Name.IndexOf("Prototype", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               type.Name.IndexOf("Harmony", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(type => type.FullName)
                .ToList();
            Assert.IsEmpty(suspiciousTypes, "发行程序集不得包含 SDK 观测原型/Harmony 补丁类型：" + string.Join(", ", suspiciousTypes));

            Assert.IsFalse(assembly.GetTypes().Any(type => type.Name == "QuickPlayPreprocessStateMachine"),
                "已移除的整链结果验证状态机不得回归。");

            var sdkEntryReferences = FindSdkPipelineCallReferences(assembly);
            Assert.IsEmpty(sdkEntryReferences, "不得手工调用 SDK preprocess 入口：" + string.Join("；", sdkEntryReferences));
        }

        /// <summary>扫描生产程序集 IL，找出任何引用 VRCBuildPipelineCallbacks 的方法（含 ldftn/委托）。</summary>
        private static List<string> FindSdkPipelineCallReferences(Assembly assembly)
        {
            var found = new List<string>();
            const string sdkTypeName = "VRCBuildPipelineCallbacks";

            foreach (var type in assembly.GetTypes())
            {
                var methods = type.GetMethods(BindingFlags.Static | BindingFlags.Instance |
                                              BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (var method in methods)
                {
                    byte[] il;
                    try
                    {
                        il = method.GetMethodBody()?.GetILAsByteArray();
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (il == null) continue;

                    for (var i = 0; i + 4 < il.Length; i++)
                    {
                        var opcode = il[i];
                        if (opcode != 0x28 && opcode != 0x6F && opcode != 0x73 && opcode != 0x7B &&
                            opcode != 0x7D && opcode != 0x74 && opcode != 0x8C && opcode != 0xA5 &&
                            opcode != 0x71 && opcode != 0x7C && opcode != 0xA3 && opcode != 0x28 &&
                            opcode != 0xFE)
                        {
                            continue;
                        }

                        if (opcode == 0xFE && il[i + 1] != 0x06) continue;

                        var token = BitConverter.ToInt32(il, i + 1);
                        try
                        {
                            var member = method.Module.ResolveMember(token, type.GetGenericArguments(), method.GetGenericArguments());
                            if (member == null) continue;
                            if (member.DeclaringType != null && member.DeclaringType.Name.Contains(sdkTypeName))
                            {
                                found.Add(type.Name + "." + method.Name + " → " + member.DeclaringType.Name + "." + member.Name);
                            }
                        }
                        catch (Exception)
                        {
                            // 不是有效 token（扫描未对齐的字节）：忽略。
                        }
                    }
                }
            }

            return found.Distinct().ToList();
        }

        // ------------------------------------------------------------ 失败延迟动作隔离

        [Test]
        public void PendingFailureAction_IsNoOpAfterSessionReset()
        {
            PrepareRunningSession("session-old");
            QuickPlaySession.FailSession("测试失败");
            Assert.AreEqual(1, _scheduled.Count, "失败必须挂起一个延迟动作。");

            // 模拟域重载/重置：静态状态清空，durable 记录仍在（Reset 必须取消挂起的旧动作）。
            QuickPlaySession.SetStoreForTests(_store);
            var recordBefore = _store.Data;
            var clearCountBefore = _store.ClearCount;

            _scheduled[0]();

            Assert.AreEqual(0, _exitRequests, "旧会话的延迟动作不得退出 Play。");
            Assert.AreEqual(clearCountBefore, _store.ClearCount, "旧动作不得清理任何记录。");
            Assert.IsNotNull(_store.Data, "旧动作不得改动 durable 记录。");
            Assert.AreEqual(recordBefore.sessionId, _store.Data.sessionId);
            Assert.AreEqual(recordBefore.phase, _store.Data.phase);
        }

        [Test]
        public void PendingFailureAction_IsNoOpAfterNewSessionAdopted()
        {
            PrepareRunningSession("session-old");
            QuickPlaySession.FailSession("测试失败");
            Assert.AreEqual(1, _scheduled.Count);

            var newSession = PrepareRunningSession("session-new");
            var newRecordBefore = _store.Data;

            _scheduled[0]();

            Assert.AreEqual(0, _exitRequests, "旧会话的延迟动作不得退出新会话的 Play。");
            Assert.IsNotNull(_store.Data, "新会话记录不得被旧动作清理。");
            Assert.AreEqual(newSession.Record.sessionId, _store.Data.sessionId);
            Assert.AreEqual(newRecordBefore.phase, _store.Data.phase);
        }

        [Test]
        public void PendingFailureAction_StillActsForTheSameFailedSession()
        {
            PrepareRunningSession("session-same");
            QuickPlaySession.FailSession("测试失败");
            Assert.AreEqual(1, _scheduled.Count);

            _scheduled[0]();

            Assert.AreEqual(0, _exitRequests, "非 Play 状态下不应请求退出 Play。");
            Assert.IsNull(QuickPlaySession.CurrentRecord, "同一失败会话的延迟动作应完成清理。");
        }

        [Test]
        public void PendingFailureAction_IsNoOpAfterCleanupStarted()
        {
            var prepare = PrepareRunningSession("session-cleanup-old");

            // 破坏恢复依据（标记 + durable 身份），并模拟域重载：静态引用丢失，只剩 durable 记录。
            UnityEngine.Object.DestroyImmediate(prepare.Clone.GetComponent<QuickPlayCloneMarker>());
            prepare.Record.originalGlobalId = string.Empty;
            prepare.Record.originalActiveWritten = true;
            _store.Seed(prepare.Record);
            QuickPlaySession.SetStoreForTests(_store);

            QuickPlaySession.FailSession("测试失败");
            Assert.AreEqual(1, _scheduled.Count);

            var completed = QuickPlaySession.Cleanup("不完整清理", restoreSelection: false);

            Assert.IsFalse(completed, "无法确认原对象恢复时不得宣称清理完成。");
            Assert.IsNotNull(_store.Data);
            Assert.AreEqual((int)QuickPlaySessionPhase.Cleaning, _store.Data.phase);

            var clearCountBefore = _store.ClearCount;
            _scheduled[0]();

            Assert.AreEqual(0, _exitRequests, "Cleanup 开始后旧失败动作不得退出 Play。");
            Assert.AreEqual(clearCountBefore, _store.ClearCount, "旧动作不得清理未完成的会话记录。");
            Assert.IsNotNull(_store.Data);
        }

        [Test]
        public void PendingFailureAction_IsNoOpForOrdinaryPlayWithoutSession()
        {
            PrepareRunningSession("session-gone");
            QuickPlaySession.FailSession("测试失败");
            Assert.AreEqual(1, _scheduled.Count);

            // 普通 Play / 没有会话记录：旧动作必须 no-op。
            QuickPlaySession.SetStoreForTests(new FakeSessionStore());
            var clearCountBefore = _store.ClearCount;

            _scheduled[0]();

            Assert.AreEqual(0, _exitRequests, "普通 Play 不得被旧失败动作退出。");
            Assert.IsNull(QuickPlaySession.CurrentRecord);
            Assert.AreEqual(clearCountBefore, _store.ClearCount);
        }

        [Test]
        public void PendingFailureAction_SecondInvocationIsNoOp()
        {
            PrepareRunningSession("session-token");
            QuickPlaySession.FailSession("测试失败");

            _scheduled[0]();
            Assert.IsNull(QuickPlaySession.CurrentRecord);

            var storeAfterFirst = _store.Data;
            _scheduled[0]();

            Assert.AreEqual(0, _exitRequests);
            Assert.AreEqual(storeAfterFirst, _store.Data, "重复执行同一动作不得产生任何副作用。");
        }
    }

    /// <summary>
    /// 本机日志捕获（替代只在 Test Runner 内可用的 LogAssert）。
    /// 由 <see cref="QuickPlayDirectTestRunner"/> 运行的测试在 SetUp 中安装。
    /// </summary>
    internal static class QuickPlayLogCapture
    {
        private static readonly List<string> _errors = new List<string>();
        private static readonly List<string> _warnings = new List<string>();
        private static bool _installed;

        public static IReadOnlyList<string> Errors => _errors;

        public static IReadOnlyList<string> Warnings => _warnings;

        public static void Install()
        {
            if (_installed) return;
            Application.logMessageReceived += OnLog;
            _installed = true;
        }

        public static void Uninstall()
        {
            if (_installed) Application.logMessageReceived -= OnLog;
            _installed = false;
            Clear();
        }

        public static void Clear()
        {
            _errors.Clear();
            _warnings.Clear();
        }

        public static bool HasError(string token)
        {
            return _errors.Any(message => message.IndexOf(token, StringComparison.Ordinal) >= 0);
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _errors.Add(message);
            }
            else if (type == LogType.Warning)
            {
                _warnings.Add(message);
            }
        }
    }
}

#endif
