#if UNITY_INCLUDE_TESTS

using System.Collections.Generic;
using System.Linq;
using marble810.MarbleAvatarToolbox.PlayModeTools;
using NUnit.Framework;
using UnityEngine;

namespace MarbleQuickPlay.Tests
{
    /// <summary>
    /// 会话记录、对象身份与 Selection 的回归测试（R1 / R7）。
    /// 关键安全约束：身份失效一律告警并跳过，绝不按名字/兄弟序号猜测。
    /// </summary>
    [Category("QuickPlay")]
    public sealed class QuickPlaySessionRecordTests
    {
        private QuickPlayIsolatedScene _scene;

        [SetUp]
        public void SetUp()
        {
            _scene = QuickPlayObjectTestScope.Require();
        }

        [TearDown]
        public void TearDown()
        {
            _scene?.Dispose();
            _scene = null;
        }

        [Test]
        public void Record_JsonRoundTripKeepsAllFields()
        {
            var record = new QuickPlaySessionRecord
            {
                sessionId = "abc123",
                phase = (int)QuickPlaySessionPhase.Running,
                cloneName = "Avatar (QuickPlay Clone)",
                originalGlobalId = "GlobalObjectId_V1-2-aaaa-1-0",
                originalName = "Avatar",
                originalActiveSelf = false,
                selectionGlobalIds = new List<string> { "id-a", "id-b" },
                selectionActiveGlobalId = "id-b",
                cleanScenePaths = new List<string> { "Assets/A.unity" },
                preferenceToolIds = new List<string> { "vrcfury", "aao" },
                effectiveToolIds = new List<string> { "vrcfury" },
                d4RkSuppressionActive = false,
                vqtSuppressionActive = true,
                sessionFailed = true,
                failureReason = "known-failure",
                preprocessStage = 3, // 旧版字段：只读兼容往返
                preprocessFailureReason = "legacy-reason",
            };

            var restored = JsonUtility.FromJson<QuickPlaySessionRecord>(JsonUtility.ToJson(record));

            Assert.AreEqual(record.sessionId, restored.sessionId);
            Assert.AreEqual(record.phase, restored.phase);
            Assert.AreEqual(record.cloneName, restored.cloneName);
            Assert.AreEqual(record.originalGlobalId, restored.originalGlobalId);
            Assert.AreEqual(record.originalName, restored.originalName);
            Assert.AreEqual(record.originalActiveSelf, restored.originalActiveSelf);
            CollectionAssert.AreEqual(record.selectionGlobalIds, restored.selectionGlobalIds);
            Assert.AreEqual(record.selectionActiveGlobalId, restored.selectionActiveGlobalId);
            CollectionAssert.AreEqual(record.cleanScenePaths, restored.cleanScenePaths);
            CollectionAssert.AreEqual(record.preferenceToolIds, restored.preferenceToolIds);
            CollectionAssert.AreEqual(record.effectiveToolIds, restored.effectiveToolIds);
            Assert.AreEqual(record.d4RkSuppressionActive, restored.d4RkSuppressionActive);
            Assert.AreEqual(record.vqtSuppressionActive, restored.vqtSuppressionActive);
            Assert.AreEqual(record.selectionWasEmpty, restored.selectionWasEmpty);
            Assert.AreEqual(record.selectionHadUncapturableObjects, restored.selectionHadUncapturableObjects);
            Assert.AreEqual(record.sessionFailed, restored.sessionFailed);
            Assert.AreEqual(record.failureReason, restored.failureReason);
            Assert.AreEqual(record.preprocessStage, restored.preprocessStage);
            Assert.AreEqual(record.preprocessFailureReason, restored.preprocessFailureReason);
        }

        [Test]
        public void Record_LegacyFailureStageIsMigratedConservatively()
        {
            // 旧版记录：preprocessStage=3 + 原因 表示本工具已知失败，必须迁移为 sessionFailed。
            var legacyFailed = new QuickPlaySessionRecord
            {
                sessionId = "legacy-failed",
                preprocessStage = 3,
                preprocessFailureReason = "旧版失败原因",
            };
            Assert.IsTrue(legacyFailed.TryValidate(out var failedError), failedError);
            Assert.IsTrue(legacyFailed.sessionFailed);
            Assert.AreEqual("旧版失败原因", legacyFailed.failureReason);

            // 旧版观察值 1/2 不是成功/失败依据：只读兼容，不得迁移为失败。
            var legacyEntered = new QuickPlaySessionRecord { sessionId = "legacy-entered", preprocessStage = 1 };
            Assert.IsTrue(legacyEntered.TryValidate(out var enteredError), enteredError);
            Assert.IsFalse(legacyEntered.sessionFailed);

            var legacyCompleted = new QuickPlaySessionRecord { sessionId = "legacy-completed", preprocessStage = 2 };
            Assert.IsTrue(legacyCompleted.TryValidate(out var completedError), completedError);
            Assert.IsFalse(legacyCompleted.sessionFailed, "旧版收尾观察值不得被当作成功或失败。");

            // 未知值必须保守拒绝。
            var unknown = new QuickPlaySessionRecord { sessionId = "legacy-unknown", preprocessStage = 9 };
            Assert.IsFalse(unknown.TryValidate(out var unknownError));
            StringAssert.Contains("preprocessStage", unknownError);
        }

        [Test]
        public void Identity_UntitledSceneObjectIsNotCapturedByGuesswork()
        {
            // 无路径的临时场景无法生成可往返的 GlobalObjectId：必须明确失败，而不是退化成名字匹配。
            var gameObject = _scene.CreateGameObject("Avatar");
            var captured = QuickPlayObjectIdentity.TryCaptureId(gameObject, out var id, out var error);

            Assert.IsFalse(captured, "未保存场景中的对象不应假装拥有 durable 身份。");
            Assert.IsEmpty(id);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void Identity_ResolveRejectsUnknownOrMalformedIds()
        {
            Assert.IsNull(QuickPlayObjectIdentity.TryResolve(string.Empty, out _));
            Assert.IsNull(QuickPlayObjectIdentity.TryResolve("not-a-global-object-id", out var malformedError));
            Assert.IsNotEmpty(malformedError);

            // 一个格式合法但指向不存在对象的 id 必须解析为 null（身份失效）。
            var bogus = "GlobalObjectId_V1-2-dffe6e7ed5493f748acecf5e5210def0-987654321-0";
            Assert.IsNull(QuickPlayObjectIdentity.TryResolve(bogus, out var missingError));
            Assert.IsNotEmpty(missingError);
        }

        [Test]
        public void OriginalReference_NeverFallsBackToSameNamedObject()
        {
            // 场景里存在一个与记录同名、同样带 VRCAvatarDescriptor 的对象。
            var replacement = _scene.CreateGameObject("Avatar");
            var descriptorType = QuickPlayTestTypes.Find("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (descriptorType != null) replacement.AddComponent(descriptorType);

            var record = new QuickPlaySessionRecord
            {
                sessionId = "s1",
                originalName = "Avatar",
                originalGlobalId = string.Empty,
            };

            var resolved = QuickPlayOriginalReference.Resolve(record, new List<GameObject>(), out var reason);

            Assert.IsNull(resolved, "没有可用身份时绝不能按名字挑对象。");
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void OriginalReference_PrefersMarkerReference()
        {
            var original = _scene.CreateGameObject("Avatar");
            var clone = _scene.CreateGameObject("Avatar (QuickPlay Clone)");
            clone.AddComponent<QuickPlayCloneMarker>().Initialize("s1", original);

            var record = new QuickPlaySessionRecord { sessionId = "s1", originalName = "Avatar", originalGlobalId = string.Empty };
            var resolved = QuickPlayOriginalReference.Resolve(record, new List<GameObject> { clone }, out var reason);

            Assert.AreSame(original, resolved);
            Assert.IsEmpty(reason);
        }

        [Test]
        public void CloneLocator_FindsOnlyMarkedCloneOfThisSession()
        {
            var clone = _scene.CreateGameObject("Avatar (QuickPlay Clone)");
            clone.AddComponent<QuickPlayCloneMarker>().Initialize("session-a", null);

            var otherSessionClone = _scene.CreateGameObject("Other (QuickPlay Clone)");
            otherSessionClone.AddComponent<QuickPlayCloneMarker>().Initialize("session-b", null);

            // 用户自己的对象，名字与 clone 相同，但没有本会话标记。
            _scene.CreateGameObject("Avatar (QuickPlay Clone)");

            var found = QuickPlayCloneLocator.FindInScene(_scene.Scene, "session-a");

            Assert.AreEqual(1, found.Count);
            Assert.AreSame(clone, found[0]);
            Assert.IsTrue(QuickPlayCloneLocator.IsOwnedClone(clone));
            Assert.AreEqual(1, QuickPlayCloneLocator.FindInScene(_scene.Scene, "session-b").Count);
        }

        [Test]
        public void CloneLocator_EmptySessionIdFindsNothing()
        {
            var clone = _scene.CreateGameObject("Avatar (QuickPlay Clone)");
            clone.AddComponent<QuickPlayCloneMarker>().Initialize("session-a", null);

            Assert.IsEmpty(QuickPlayCloneLocator.FindInScene(_scene.Scene, string.Empty));
            Assert.IsEmpty(QuickPlayCloneLocator.FindInScene(_scene.Scene, null));
            Assert.IsEmpty(QuickPlayCloneLocator.FindAll(string.Empty));
        }

        [Test]
        public void CloneLocator_FindsMarkersOnInactiveChildren()
        {
            var clone = _scene.CreateGameObject("Avatar (QuickPlay Clone)");
            var child = _scene.CreateGameObject("Child");
            child.transform.SetParent(clone.transform, false);
            child.SetActive(false);
            child.AddComponent<QuickPlayCloneMarker>().Initialize("session-a", null);

            var found = QuickPlayCloneLocator.FindInScene(_scene.Scene, "session-a");

            Assert.AreEqual(1, found.Count);
            Assert.AreSame(child, found[0]);
        }

        [Test]
        public void SelectionSnapshot_UnresolvableIdsAreSkippedWithoutTouchingScene()
        {
            var snapshot = new QuickPlaySelectionSnapshot();
            snapshot.DirectObjects.Add(null);
            snapshot.GlobalObjectIds.Add("broken-id");
            snapshot.ActiveGlobalObjectId = "broken-id";

            var selectionBefore = UnityEditor.Selection.objects;
            int restored;
            List<string> warnings;
            try
            {
                restored = QuickPlayObjectIdentity.RestoreSelection(snapshot, out warnings);
            }
            finally
            {
                UnityEditor.Selection.objects = selectionBefore;
            }

            Assert.AreEqual(0, restored);
            Assert.IsNotEmpty(warnings);
        }

        [Test]
        public void SelectionCapture_CanBeRestoredForUserSceneObject()
        {
            // 只读：用当前工程里已存在的一个已保存场景对象验证 GlobalObjectId 往返。
            GameObject candidate = null;
            foreach (var scene in QuickPlaySceneEnumerator.LoadedEditableScenes())
            {
                if (string.IsNullOrEmpty(scene.path)) continue;
                candidate = scene.GetRootGameObjects().FirstOrDefault(root => root != null);
                if (candidate != null) break;
            }

            if (candidate == null)
            {
                Assert.Ignore("当前没有已保存场景可用于 GlobalObjectId 往返验证。");
            }

            Assert.IsTrue(QuickPlayObjectIdentity.TryCaptureId(candidate, out var id, out var error), error);
            Assert.IsNotNull(QuickPlayObjectIdentity.TryResolve(id, out _));

            var snapshot = new QuickPlaySelectionSnapshot();
            snapshot.DirectObjects.Add(candidate);
            snapshot.GlobalObjectIds.Add(id);
            snapshot.ActiveGlobalObjectId = id;

            var selectionBefore = UnityEditor.Selection.objects;
            try
            {
                var restored = QuickPlayObjectIdentity.RestoreSelection(snapshot, out var warnings);
                Assert.IsEmpty(warnings);
                Assert.AreEqual(1, restored);
                Assert.AreSame(candidate, UnityEditor.Selection.activeGameObject);
            }
            finally
            {
                UnityEditor.Selection.objects = selectionBefore;
            }
        }

        [Test]
        public void CleanPathCapture_ReportsOnlyNewlyDirtyScenes()
        {
            var clean = QuickPlayObjectIdentity.CaptureCleanScenePaths();
            var newlyDirty = QuickPlayObjectIdentity.FindNewlyDirtyScenes(clean);

            // 本测试自身不修改任何用户场景；因此不应出现「开始时干净、现在 dirty」的场景。
            Assert.IsEmpty(newlyDirty);
        }
    }
}

#endif
