#if UNITY_INCLUDE_TESTS

using System.Collections.Generic;
using marble810.MarbleAvatarToolbox.PlayModeTools;
using NUnit.Framework;
using UnityEngine;

namespace MarbleQuickPlay.Tests
{
    /// <summary>
    /// 启动前检查测试：NDMF ApplyOnPlay 关闭、重复会话、工具不兼容、未安装工具与空选择。
    /// 全部为纯决策逻辑，不修改编辑器状态、不进入 Play。
    /// </summary>
    [Category("QuickPlay")]
    public sealed class QuickPlayPreflightTests
    {
        private QuickPlayIsolatedScene _scene;
        private GameObject _avatar;

        [SetUp]
        public void SetUp()
        {
            _scene = QuickPlayObjectTestScope.Require();
            _avatar = _scene.CreateGameObject("QuickPlayPreflightAvatar");
        }

        [TearDown]
        public void TearDown()
        {
            _scene?.Dispose();
            _scene = null;
            _avatar = null;
        }

        private static QuickPlayPreferenceData Snapshot(params string[] ids)
        {
            return new QuickPlayPreferenceData
            {
                schemaVersion = 1,
                initialized = true,
                stripToolIds = new List<string>(ids),
            };
        }

        private QuickPlayPreflightInput CreateInput(params string[] ids)
        {
            return new QuickPlayPreflightInput
            {
                IsPlayingOrWillChangePlaymode = false,
                IsCompilingOrUpdating = false,
                HasActiveSession = false,
                ApplyOnPlayEnabled = true,
                Target = new QuickPlayTargetResolution { AvatarRoot = _avatar },
                Snapshot = Snapshot(ids),
            };
        }

        [Test]
        public void ApplyOnPlayDisabled_RejectsBeforeAnySceneChange()
        {
            var input = CreateInput("vrcfury");
            input.ApplyOnPlayEnabled = false;

            var check = QuickPlayPreflight.Evaluate(input);

            Assert.IsFalse(check.CanStart);
            StringAssert.Contains("Apply on Play", check.Error);
            StringAssert.Contains("不会自动修改", check.Error);
        }

        [Test]
        public void PlayingOrCompilingOrActiveSession_Rejects()
        {
            var input = CreateInput("vrcfury");
            input.IsPlayingOrWillChangePlaymode = true;
            Assert.IsFalse(QuickPlayPreflight.Evaluate(input).CanStart);

            input = CreateInput("vrcfury");
            input.IsCompilingOrUpdating = true;
            Assert.IsFalse(QuickPlayPreflight.Evaluate(input).CanStart);

            input = CreateInput("vrcfury");
            input.HasActiveSession = true;
            Assert.IsFalse(QuickPlayPreflight.Evaluate(input).CanStart);
        }

        [Test]
        public void AmbiguousTarget_RejectsWithReason()
        {
            var input = CreateInput("vrcfury");
            input.Target = new QuickPlayTargetResolution { FailureReason = "无法确定要使用的 Avatar。" };

            var check = QuickPlayPreflight.Evaluate(input);

            Assert.IsFalse(check.CanStart);
            StringAssert.Contains("无法确定", check.Error);
        }

        [Test]
        public void IncompatibleSelectedTool_RejectsAndNamesTheTool()
        {
            var input = CreateInput("lac");

            var check = QuickPlayPreflight.Evaluate(input, definition => new QuickPlayToolCapability(
                definition,
                definition.Id == "lac" ? QuickPlayToolAvailability.Incompatible : QuickPlayToolAvailability.Available,
                "TextureCompressor 来自未登记程序集 Something.Else",
                new List<System.Type>()));

            Assert.IsFalse(check.CanStart);
            StringAssert.Contains("Avatar Compressor (LAC)", check.Error);
            StringAssert.Contains("未登记程序集", check.Error);
            Assert.AreEqual(1, check.BlockedToolReasons.Count);
        }

        [Test]
        public void IncompatibleUnselectedTool_DoesNotBlockStart()
        {
            var input = CreateInput("vrcfury");

            var check = QuickPlayPreflight.Evaluate(input, definition => new QuickPlayToolCapability(
                definition,
                definition.Id == "vrcfury" ? QuickPlayToolAvailability.Available : QuickPlayToolAvailability.Incompatible,
                "不兼容（测试）",
                new List<System.Type>()));

            Assert.IsTrue(check.CanStart, "未勾选的不兼容工具不应阻止启动。");
        }

        [Test]
        public void UninstalledSelectedTool_DoesNotBlockStart()
        {
            var input = CreateInput("meshia", "mantis-ndmf");

            var check = QuickPlayPreflight.Evaluate(input, definition => new QuickPlayToolCapability(
                definition, QuickPlayToolAvailability.NotInstalled, "未安装（测试）", new List<System.Type>()));

            Assert.IsTrue(check.CanStart, "未安装工具应被忽略，且不要求安装新 Package。");
        }

        [Test]
        public void UnknownHistoricalId_DoesNotBlockStart()
        {
            var input = CreateInput("legacy-unknown-id");

            var check = QuickPlayPreflight.Evaluate(input);

            Assert.IsTrue(check.CanStart);
            Assert.IsEmpty(check.BlockedToolReasons);
        }

        [Test]
        public void EmptySelection_IsValid()
        {
            var input = CreateInput();

            var check = QuickPlayPreflight.Evaluate(input);

            Assert.IsTrue(check.CanStart, "全部不勾选仍是合法配置。");
        }

        [Test]
        public void NullInput_RejectsWithoutThrowing()
        {
            var check = QuickPlayPreflight.Evaluate(null);
            Assert.IsFalse(check.CanStart);
        }
    }
}

#endif
