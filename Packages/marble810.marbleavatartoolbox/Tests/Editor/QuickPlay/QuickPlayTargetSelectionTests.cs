#if UNITY_INCLUDE_TESTS

using marble810.MarbleAvatarToolbox.PlayModeTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MarbleQuickPlay.Tests
{
    /// <summary>
    /// 目标选择判定的回归测试（R6 / R7）。
    /// 用户明确选中禁止对象（自有 clone / 资产 / Prefab Stage / 预览 / EditorOnly）时必须拒绝，不能回退到场景唯一候选。
    /// </summary>
    [Category("QuickPlay")]
    public sealed class QuickPlayTargetSelectionTests
    {
        private static QuickPlaySelectionFacts Facts(
            bool hasSelection = true,
            bool ownedClone = false,
            bool foundAvatar = false,
            bool avatarEligible = false,
            string ineligibleReason = "目标是持久资产。")
        {
            return new QuickPlaySelectionFacts
            {
                HasSelection = hasSelection,
                SelectionIsOwnedClone = ownedClone,
                FoundAvatarInParents = foundAvatar,
                AvatarEligible = avatarEligible,
                AvatarIneligibleReason = ineligibleReason,
            };
        }

        [Test]
        public void NoSelection_AllowsSceneFallback()
        {
            var error = QuickPlayTargetSelectionPolicy.Evaluate(Facts(hasSelection: false), out var useFallback);

            Assert.IsNull(error);
            Assert.IsTrue(useFallback);
        }

        [Test]
        public void SelectedNonAvatar_AllowsSceneFallback()
        {
            var error = QuickPlayTargetSelectionPolicy.Evaluate(Facts(foundAvatar: false), out var useFallback);

            Assert.IsNull(error);
            Assert.IsTrue(useFallback);
        }

        [Test]
        public void SelectedEligibleAvatar_UsesItWithoutFallback()
        {
            var error = QuickPlayTargetSelectionPolicy.Evaluate(
                Facts(foundAvatar: true, avatarEligible: true), out var useFallback);

            Assert.IsNull(error);
            Assert.IsFalse(useFallback, "有效选择必须直接使用，不回退。");
        }

        [Test]
        public void SelectedOwnedClone_RejectsWithoutFallback()
        {
            var error = QuickPlayTargetSelectionPolicy.Evaluate(
                Facts(ownedClone: true, foundAvatar: true, avatarEligible: true), out var useFallback);

            Assert.IsNotNull(error);
            StringAssert.Contains("临时副本", error);
            Assert.IsFalse(useFallback, "选中自有 clone 时不得回退到其它 Avatar。");
        }

        [Test]
        public void SelectedAssetOrPrefabStageTarget_RejectsWithoutFallback()
        {
            var error = QuickPlayTargetSelectionPolicy.Evaluate(
                Facts(foundAvatar: true, avatarEligible: false, ineligibleReason: "目标是持久资产（Prefab/Project 资产）上的对象。"),
                out var useFallback);

            Assert.IsNotNull(error);
            StringAssert.Contains("持久资产", error);
            StringAssert.Contains("不会回退", error);
            Assert.IsFalse(useFallback, "明确选中禁止对象时必须拒绝，而不是回退到唯一场景候选。");
        }

        [Test]
        public void Production_RejectsPreviewSceneObjectWithReason()
        {
            using var scene = QuickPlayObjectTestScope.Require();
            var previewScene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try
            {
                var previewObject = new GameObject("PreviewAvatar");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(previewObject, previewScene);
                Assert.AreEqual(previewScene, previewObject.scene);

                Assert.IsFalse(QuickPlayTargetResolver.IsEligibleCandidate(previewObject, out var reason));
                StringAssert.Contains("预览", reason);
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        [Test]
        public void Production_AcceptsPlainIsolatedSceneObject()
        {
            using var scene = QuickPlayObjectTestScope.Require();
            var gameObject = scene.CreateGameObject("Avatar");

            Assert.IsTrue(QuickPlayTargetResolver.IsEligibleCandidate(gameObject, out var reason), reason);
        }

        [Test]
        public void Production_RejectsOwnedCloneWithReason()
        {
            using var scene = QuickPlayObjectTestScope.Require();
            var original = scene.CreateGameObject("Avatar");
            var clone = scene.CreateGameObject("Avatar (QuickPlay Clone)");
            clone.AddComponent<QuickPlayCloneMarker>().Initialize("session-a", original);

            Assert.IsFalse(QuickPlayTargetResolver.IsEligibleCandidate(clone, out var reason));
            StringAssert.Contains("临时副本", reason);
        }

        [Test]
        public void Production_ResolveForOwnedCloneRejectsInsteadOfFallingBack()
        {
            using var scene = QuickPlayObjectTestScope.Require();
            var original = scene.CreateGameObject("Avatar");
            var descriptorType = QuickPlayTestTypes.Find("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (descriptorType != null) original.AddComponent(descriptorType);

            var clone = scene.CreateGameObject("Avatar (QuickPlay Clone)");
            clone.AddComponent<QuickPlayCloneMarker>().Initialize("session-a", original);

            var resolution = QuickPlayTargetResolver.Resolve(clone);

            Assert.IsFalse(resolution.Succeeded, "选中自有 clone 必须拒绝，不能回退到普通场景目标。");
            StringAssert.Contains("临时副本", resolution.FailureReason);
        }

        [Test]
        public void Production_ResolveFallsBackWhenSelectionHasNoAvatar()
        {
            using var scene = QuickPlayObjectTestScope.Require();
            var notAnAvatar = scene.CreateGameObject("SomeProp");

            // 隔离场景内的对象不会被当作已加载普通场景候选（临时场景没有资产路径但仍是普通场景），
            // 因此这里只断言「不含 Avatar 的选择不会立即报选中禁止对象」，允许走回退分支。
            var resolution = QuickPlayTargetResolver.Resolve(notAnAvatar);

            if (!resolution.Succeeded)
            {
                StringAssert.DoesNotContain("临时副本", resolution.FailureReason);
                StringAssert.DoesNotContain("不可用于 QuickPlay 的目标", resolution.FailureReason);
            }
        }

        [Test]
        public void Production_ParentChainResolvesDescriptorRoot()
        {
            using var scene = QuickPlayObjectTestScope.Require();
            var root = scene.CreateGameObject("Avatar");
            var descriptorType = QuickPlayTestTypes.Find("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            Assert.IsNotNull(descriptorType, "本机应有 VRChat SDK。");
            root.AddComponent(descriptorType);

            var child = scene.CreateGameObject("Bone");
            child.transform.SetParent(root.transform, false);

            Assert.AreSame(root, QuickPlayTargetResolver.FindAvatarInParents(child));
        }

        [Test]
        public void Preflight_AppliedToBlockedToolByInstalledButIncompatible()
        {
            using var scene = QuickPlayObjectTestScope.Require();
            var input = new QuickPlayPreflightInput
            {
                ApplyOnPlayEnabled = true,
                Target = new QuickPlayTargetResolution { AvatarRoot = scene.CreateGameObject("Probe") },
                Snapshot = new QuickPlayPreferenceData
                {
                    schemaVersion = 1,
                    initialized = true,
                    stripToolIds = new System.Collections.Generic.List<string> { "d4rk-avatar-optimizer" },
                },
            };

            var check = QuickPlayPreflight.Evaluate(input, definition => new QuickPlayToolCapability(
                definition,
                definition.Id == "d4rk-avatar-optimizer"
                    ? QuickPlayToolAvailability.Incompatible
                    : QuickPlayToolAvailability.Available,
                "已安装但接口与已核验版本不一致（测试）",
                new System.Collections.Generic.List<System.Type>()));

            Assert.IsFalse(check.CanStart, "已安装但不兼容的工具必须阻止启动。实际错误：" + check.Error);
            Assert.IsNotEmpty(check.BlockedToolReasons, "应记录被阻止的工具。");
            StringAssert.Contains("d4rk Avatar Optimizer", check.Error);
        }
    }
}

#endif
