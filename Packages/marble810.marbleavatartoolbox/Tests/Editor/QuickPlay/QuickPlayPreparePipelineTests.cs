#if UNITY_INCLUDE_TESTS

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using marble810.MarbleAvatarToolbox.PlayModeTools;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MarbleQuickPlay.Tests
{
    /// <summary>
    /// 副本准备流水线的故障注入回归测试（R4 / R7）。
    /// 关键约束：标记之前失败不留未登记 clone；durable 记录写失败不得继续修改场景；
    /// 清理未完成时保留唯一恢复记录。
    /// </summary>
    [Category("QuickPlay")]
    public sealed class QuickPlayPreparePipelineTests
    {
        private QuickPlayIsolatedScene _scene;
        private GameObject _original;
        private FakeSessionStore _store;
        private FakePrepareOperations _operations;

        [SetUp]
        public void SetUp()
        {
            _scene = QuickPlayObjectTestScope.Require();
            _original = _scene.CreateGameObject("Avatar");
            _store = new FakeSessionStore();
            _operations = new FakePrepareOperations();
        }

        [TearDown]
        public void TearDown()
        {
            _scene?.Dispose();
            _scene = null;
        }

        private QuickPlayPreferenceData Snapshot(params string[] ids)
        {
            return new QuickPlayPreferenceData
            {
                schemaVersion = 1,
                initialized = true,
                stripToolIds = new List<string>(ids),
            };
        }

        private QuickPlayPrepareResult Run(QuickPlayPreferenceData snapshot = null)
        {
            return QuickPlayPreparePipeline.Execute(
                new QuickPlayPrepareRequest
                {
                    Original = _original,
                    Snapshot = snapshot ?? Snapshot("vrccury-placeholder"),
                    SessionId = "session-test",
                    RequestedAt = 0d,
                },
                _operations,
                _store);
        }

        [Test]
        public void RecordSaveFailure_AbortsBeforeTouchingScene()
        {
            _store.FailSave = true;

            var result = Run();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("记录 Preparing", result.FailedStep);
            Assert.IsEmpty(_operations.Created, "记录写失败时不得创建任何对象。");
            Assert.IsEmpty(_operations.Destroyed);
            Assert.IsEmpty(_operations.OriginalActiveChanges, "记录写失败时不得修改原对象。");
            Assert.IsEmpty(_operations.SelectedClones);
            Assert.AreEqual(0, _operations.StripPlanCalls);
        }

        [Test]
        public void InstantiateException_LeavesNoOwnedObject()
        {
            _operations.ThrowOnInstantiate = true;
            LogAssert.Expect(LogType.Exception, new Regex("Instantiate 注入异常"));

            var result = Run();

            Assert.IsFalse(result.Success);
            Assert.IsEmpty(_operations.Created);
            Assert.IsEmpty(_operations.Destroyed);
            Assert.AreEqual(1, _store.ClearCount, "没有对象被创建时可以安全清除记录。");
        }

        [Test]
        public void MarkerFailure_DestroysCloneUsingOwnershipNotName()
        {
            _operations.FailMarker = true;

            var result = Run();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("附加标记", result.FailedStep);
            Assert.IsNotEmpty(_operations.Created);
            Assert.AreEqual(_operations.Created.Count, _operations.Destroyed.Count, "未登记的 clone 必须按对象引用销毁。");
            Assert.IsNotEmpty(_operations.Destroyed, "必须清理掉未登记的临时副本。");
            Assert.AreEqual(1, _store.ClearCount, "清理成功后才可清除记录。");
            Assert.IsEmpty(_operations.OriginalActiveChanges, "失败路径不得禁用原对象。");
        }

        [Test]
        public void PlaceFailure_DestroysCloneBeforeMarker()
        {
            _operations.FailPlace = true;

            var result = Run();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("放置 clone", result.FailedStep);
            Assert.IsNotEmpty(_operations.Destroyed);
            Assert.AreEqual(1, _store.ClearCount);
        }

        [Test]
        public void StripPlanFailure_FailsClosedWithoutDisablingOriginal()
        {
            _operations.FailStrip = true;

            var result = Run();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("剔除计划", result.FailedStep);
            StringAssert.Contains("测试", result.Error);
            Assert.IsNotEmpty(_operations.Destroyed, "剔除失败必须回滚临时副本。");
            Assert.IsEmpty(_operations.OriginalActiveChanges, "剔除失败不得禁用原对象。");
            Assert.AreEqual(1, _store.ClearCount);
        }

        [Test]
        public void DestroyFailure_KeepsRecordForRecovery()
        {
            _operations.FailMarker = true;
            _operations.FailDestroyAlways = true;
            LogAssert.Expect(LogType.Exception, new Regex("DestroyOwned 注入异常"));
            LogAssert.Expect(LogType.Error, new Regex("准备中止但清理未完成"));

            var result = Run();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, _store.ClearCount, "清理未完成时不得抹掉唯一恢复记录。");
            Assert.IsNotNull(_store.Data);
            Assert.AreEqual((int)QuickPlaySessionPhase.Cleaning, _store.Data.phase);
        }

        [Test]
        public void Success_KeepsOriginalUntouchedAndRecordsPrepared()
        {
            var result = Run();

            Assert.IsTrue(result.Success, result.Error);
            Assert.IsNotNull(result.Clone);
            Assert.IsTrue(_operations.MarkerAttached, "必须附加所有权标记。");
            Assert.AreEqual(1, _operations.StripPlanCalls);
            Assert.IsEmpty(_operations.OriginalActiveChanges, "准备阶段不得禁用原对象（由会话在成功后处理）。");
            Assert.IsNotNull(result.Record);
            Assert.AreEqual("session-test", result.Record.sessionId);
            Assert.AreEqual(0, _store.ClearCount, "成功路径不清除记录。");
        }

        [Test]
        public void Success_RecordsSelectionAndCleanSceneSnapshot()
        {
            var result = Run();

            Assert.IsTrue(result.Success, result.Error);

            // 临时场景没有可往返的 GlobalObjectId，因此身份为空但流程仍可继续（绝不退化为按名字匹配）。
            Assert.IsNotNull(result.Record);
            Assert.IsNotNull(result.Record.selectionGlobalIds);
            Assert.IsNotNull(result.Record.cleanScenePaths);
            Assert.IsFalse(result.Record.sessionFailed, "成功准备不得写入失败状态。");
            Assert.AreEqual(0, result.Record.preprocessStage, "本版本不写入旧版 preprocess 观察字段。");
        }

        [Test]
        public void Success_UsesProvidedSessionIdAndToolSnapshot()
        {
            var result = Run(Snapshot("vrcfury", "aao"));

            Assert.IsTrue(result.Success, result.Error);
            CollectionAssert.AreEqual(new[] { "vrcfury", "aao" }, result.Record.preferenceToolIds);
            Assert.IsFalse(result.Record.d4RkSuppressionActive);
            Assert.IsFalse(result.Record.vqtSuppressionActive);
        }

        [Test]
        public void D4RkAndVqtSelectionAreFrozenInRecord()
        {
            var result = Run(Snapshot("d4rk-avatar-optimizer", "vqt-menu-icons"));

            Assert.IsTrue(result.Success, result.Error);
            CollectionAssert.Contains(result.Record.preferenceToolIds, "d4rk-avatar-optimizer");
            CollectionAssert.Contains(result.Record.preferenceToolIds, "vqt-menu-icons");
            // 有效计划只包含本机实际可用的工具：d4rk 未安装时不得激活。
            Assert.IsFalse(result.Record.d4RkSuppressionActive);
            Assert.AreEqual(result.Record.effectiveToolIds.Contains("vqt-menu-icons"), result.Record.vqtSuppressionActive);
        }

        [Test]
        public void NullOriginal_FailsWithoutStoreWrites()
        {
            var result = QuickPlayPreparePipeline.Execute(
                new QuickPlayPrepareRequest { Original = null, Snapshot = Snapshot(), SessionId = "s", RequestedAt = 0d },
                _operations,
                _store);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, _store.SaveCount);
            Assert.IsEmpty(_operations.Created);
        }
    }

    /// <summary>生产准备操作在「原对象不在 active scene」时的场景/父节点/local TRS 语义（R7）。</summary>
    [Category("QuickPlay")]
    public sealed class QuickPlayPrepareOperationsTests
    {
        [Test]
        public void PlaceClone_MovesCloneIntoOriginalSceneAndKeepsTrs()
        {
            using var scene = QuickPlayObjectTestScope.Require();

            // 用一个独立的 preview 场景作为「另一个场景」：只读语义、无资产文件，
            // 用于验证 clone 起始场景与原对象不同时的搬运逻辑。
            var otherScene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            GameObject clone = null;
            try
            {
                var original = new GameObject("Avatar");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(original, otherScene);
                original.transform.localPosition = new Vector3(1f, 2f, 3f);
                original.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
                original.transform.localScale = new Vector3(2f, 2f, 2f);

                clone = scene.CreateGameObject("CloneStartedInAnotherScene");
                Assert.AreNotEqual(original.scene, clone.scene, "前置条件：clone 与原对象位于不同场景。");

                var operations = new QuickPlayPrepareOperations();
                Assert.IsTrue(operations.TryPlaceClone(clone, original, out var error), error);

                Assert.AreEqual(original.scene, clone.scene, "clone 必须移动到原对象所在场景。");
                Assert.AreEqual(original.transform.parent, clone.transform.parent);
                Assert.AreEqual(original.transform.localPosition, clone.transform.localPosition);
                Assert.AreEqual(original.transform.localRotation, clone.transform.localRotation);
                Assert.AreEqual(original.transform.localScale, clone.transform.localScale);
                Assert.AreEqual(original.transform.GetSiblingIndex() + 1, clone.transform.GetSiblingIndex());
            }
            finally
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(otherScene);
            }
        }

        [Test]
        public void PlaceClone_PreservesParentAndIsSiblingAfterOriginal()
        {
            using var scene = QuickPlayObjectTestScope.Require();
            var parent = scene.CreateGameObject("Container");
            var original = scene.CreateGameObject("Avatar");
            original.transform.SetParent(parent.transform, false);
            original.transform.localPosition = new Vector3(0.5f, 0f, 0f);
            var sibling = scene.CreateGameObject("Sibling");
            sibling.transform.SetParent(parent.transform, false);

            var clone = UnityEngine.Object.Instantiate(original);
            var operations = new QuickPlayPrepareOperations();
            Assert.IsTrue(operations.TryPlaceClone(clone, original, out var error), error);

            Assert.AreEqual(parent.transform, clone.transform.parent, "clone 必须保留父节点语义。");
            Assert.AreEqual(original.transform.GetSiblingIndex() + 1, clone.transform.GetSiblingIndex());
            Assert.AreEqual(original.transform.localPosition, clone.transform.localPosition);

            UnityEngine.Object.DestroyImmediate(clone);
        }
    }
}

#endif
