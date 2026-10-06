#if UNITY_INCLUDE_TESTS

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using marble810.MarbleAvatarToolbox.PlayModeTools;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MarbleQuickPlay.Tests
{
    /// <summary>
    /// 回归：QuickPlay 自身的抑制失败必须在**真实 NDMF pass 循环**里立即中止（后续 pass 不再执行），
    /// 且不得影响其它插件错误的处理方式。
    ///
    /// 使用的真实 seam：
    /// - `new BuildContext(root, assetRootPath: null, ...)` → NDMF 使用 NullAssetSaver（不写任何工程资产）；
    /// - 反射调用 NDMF 内部 `AvatarProcessor.ProcessAvatar(BuildContext, BuildPhase, BuildPhase)` → 真实 pass 循环；
    /// - 目标对象位于测试自有隔离容器内，且被登记为本会话的 VQT 抑制目标。
    ///
    /// 中止证明：`BuildContext.RunPass` 捕获 pass 异常后调用插件 `OnUnhandledException`，
    /// QuickPlay 只对自己的 `QuickPlaySuppressionFailure` 重新抛出；该异常会逃出 `RunPass`，
    /// 并逃出 `AvatarProcessor.ProcessAvatar` 的 phase/pass 循环（该循环没有 catch），因此后续 pass 不会执行。
    /// </summary>
    [Category("QuickPlay")]
    public sealed class QuickPlayNdmfFailurePropagationTests
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
        }

        [TearDown]
        public void TearDown()
        {
            QuickPlayVqtSuppression.SuppressionFailureInjection = null;
            _restoreDialogs?.Invoke();
            _restoreDialogs = null;
            QuickPlaySession.SetStoreForTests(null);
            _container?.Dispose();
            _container = null;
        }

        private GameObject CreateRegisteredVqtTarget()
        {
            var root = _container.CreateGameObject("QuickPlayNdmfProbe");
            var record = new QuickPlaySessionRecord
            {
                sessionId = "ndmf-sentinel-session",
                phase = (int)QuickPlaySessionPhase.Running,
                preferenceToolIds = new List<string> { QuickPlayToolRegistry.VqtId },
                effectiveToolIds = new List<string> { QuickPlayToolRegistry.VqtId },
                vqtSuppressionActive = true,
            };
            _store.Seed(record);
            root.AddComponent<QuickPlayCloneMarker>().Initialize(record.sessionId, null);
            return root;
        }

        private static Exception TryRunRealOptimizingLoop(BuildContext context)
        {
            var method = typeof(AvatarProcessor).GetMethod(
                "ProcessAvatar",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(BuildContext), typeof(BuildPhase), typeof(BuildPhase) },
                null);

            Assert.IsNotNull(method, "本机 NDMF 应提供内部 ProcessAvatar(BuildContext, BuildPhase, BuildPhase) 作为 pass 循环入口。");

            try
            {
                method.Invoke(null, new object[] { context, BuildPhase.Optimizing, BuildPhase.Optimizing });
                return null;
            }
            catch (TargetInvocationException exception)
            {
                return exception.InnerException ?? exception;
            }
        }

        [Test]
        public void F2_RealNdmfPassLoop_CompletesWhenSuppressionSucceeds()
        {
            var root = CreateRegisteredVqtTarget();
            var context = new BuildContext(root, null, false);

            Assert.AreEqual("NullAssetSaver", context.AssetSaver.GetType().Name,
                "测试必须使用 NullAssetSaver，避免写入任何工程资产。");
            Assert.IsTrue(QuickPlaySession.IsVqtSuppressionTarget(root), "前置条件：目标必须被登记为 VQT 抑制目标。");

            var exception = TryRunRealOptimizingLoop(context);

            Assert.IsNull(exception, "无注入故障时真实 pass 循环应正常完成：" + exception);
            Assert.IsTrue(context.Successful, "控制组上下文应为成功。");
        }

        [Test]
        public void F2_RealNdmfPassLoop_AbortsOnQuickPlaySuppressionFailure()
        {
            var root = CreateRegisteredVqtTarget();
            QuickPlayVqtSuppression.SuppressionFailureInjection = () => "注入的 VQT 状态抑制失败";

            var context = new BuildContext(root, null, false);

            // AvatarProcessor 的 pass 循环在异常逃出 RunPass 时会先记录 "Error processing pass"，然后向外抛出。
            LogAssert.Expect(LogType.Error, new Regex("Error processing pass"));
            LogAssert.Expect(LogType.Exception, new Regex("QuickPlaySuppressionFailure"));
            var exception = TryRunRealOptimizingLoop(context);

            Assert.IsNotNull(exception, "QuickPlay 抑制失败必须中止真实 pass 循环（异常逃出 RunPass 与 ProcessAvatar 循环）。");
            Assert.IsInstanceOf<QuickPlaySuppressionFailure>(exception,
                "中止必须由 QuickPlay 自己的异常类型触发（不是其它插件的错误）：" + exception);
            StringAssert.Contains("注入的 VQT 状态抑制失败", exception.Message);

            // 说明：插件异常处理重新抛出后，NDMF 的 ErrorReport.ReportException 不会被执行，
            // 因此 context.Successful 可能仍为 true。失败可见性由「异常逃出 SDK hook（LogException + 返回 false）」
            // 与 QuickPlay 自己的状态机保证；这里不断言 context.Successful，避免把未实现的语义写成 GREEN。
            Assert.IsNotNull(exception, "必须观察到中止异常。");
        }

        [Test]
        public void F2_OnlyQuickPlayFailuresAreRepropagated()
        {
            var plugin = QuickPlayNdmfPlugin.Instance;
            var onUnhandled = typeof(QuickPlayNdmfPlugin).GetMethod("OnUnhandledException",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
            Assert.IsNotNull(onUnhandled, "插件必须覆写 OnUnhandledException。");

            // 非 QuickPlay 异常：交给基类默认处理（记录日志，不向外传播）。
            LogAssert.Expect(LogType.Exception, new Regex("普通异常（测试）"));
            Assert.DoesNotThrow(() =>
                onUnhandled.Invoke(plugin, new object[] { new InvalidOperationException("普通异常（测试）") }));

            // QuickPlay 自身抑制失败：向外传播。
            var propagated = Assert.Throws<TargetInvocationException>(() =>
                onUnhandled.Invoke(plugin, new object[] { new QuickPlaySuppressionFailure("QuickPlay 抑制失败（测试）") }));
            Assert.IsInstanceOf<QuickPlaySuppressionFailure>(propagated.InnerException);
        }

        [Test]
        public void F2_SdkCallbackContract_EarlyHookFailureStopsSubsequentCallbacks()
        {
            // SDK 的 OnPreprocessAvatar 在任一回调返回 false 时立即以 false 返回（本机 VRCSDKBase-Editor 的 IL 证据：
            // callvirt OnPreprocessAvatar → brfalse → 返回 0）。这里按该顺序语义驱动**生产 Early hook** + 本地哨兵，
            // 断言 Early 返回 false 后不会执行后续回调。
            var root = CreateRegisteredVqtTarget();
            _store.Seed(new QuickPlaySessionRecord
            {
                sessionId = "ndmf-sentinel-session",
                phase = (int)QuickPlaySessionPhase.Running,
                preferenceToolIds = new List<string> { QuickPlayToolRegistry.D4RkId },
                effectiveToolIds = new List<string> { QuickPlayToolRegistry.D4RkId },
                d4RkSuppressionActive = true, // 本机未安装 d4rk：复核必然失败
            });

            var laterCallbackRan = false;

            bool LaterCallback(GameObject gameObject)
            {
                laterCallbackRan = true;
                return true;
            }

            var callbacks = new List<(int order, Func<GameObject, bool> invoke)>
            {
                (-20000, gameObject => new QuickPlayEarlySdkHook().OnPreprocessAvatar(gameObject)),
                (int.MaxValue - 2, LaterCallback),
            };

            LogAssert.Expect(LogType.Error, new Regex("d4rk 阻断状态在 SDK preprocess 阶段复核失败"));

            var continued = true;
            foreach (var callback in callbacks.OrderBy(c => c.order))
            {
                if (callback.invoke(root)) continue;
                continued = false; // 与 SDK 的 brfalse 中止语义一致
                break;
            }

            Assert.IsFalse(continued, "Early hook 复核失败必须返回 false，使 SDK 中止后续回调。");
            Assert.IsFalse(laterCallbackRan, "Early 返回 false 后不得继续执行后续回调。");
            Assert.IsTrue(QuickPlaySession.CurrentRecord.sessionFailed,
                "失败必须先写入 durable 记录。");
        }

        [Test]
        public void F2_SdkCallbackContract_NonTargetIsNoOpAndContinues()
        {
            var unrelated = _container.CreateGameObject("NotATarget");
            var laterCallbackRan = false;

            bool LaterCallback(GameObject gameObject)
            {
                laterCallbackRan = true;
                return true;
            }

            var early = new QuickPlayEarlySdkHook();
            Assert.IsTrue(early.OnPreprocessAvatar(unrelated), "非目标对象必须 no-op 并返回 true。");
            Assert.IsTrue(LaterCallback(unrelated));
            Assert.IsTrue(laterCallbackRan);
            Assert.IsEmpty(QuickPlayTestHelpers.CollectedDialogs, "非目标对象不得触发任何失败提示。");
        }
    }
}

#endif
