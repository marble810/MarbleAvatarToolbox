#if UNITY_INCLUDE_TESTS

using System.Collections.Generic;
using System.Linq;
using marble810.MarbleAvatarToolbox.PlayModeTools;
using NUnit.Framework;
using UnityEditor;

namespace MarbleQuickPlay.Tests
{
    /// <summary>
    /// 用户级配置的测试：默认值、空选择、未知 ID、损坏恢复、保存失败回滚与快照冻结。
    /// 使用内存后端，不触碰项目 UserSettings 文件。
    /// </summary>
    [Category("QuickPlay")]
    public sealed class QuickPlayPreferencesTests
    {
        [SetUp]
        public void SetUp()
        {
            QuickPlayTestHelpers.ClearWarnings();
        }

        [Test]
        public void FirstRun_DefaultsToVrcFuryOnly()
        {
            var persistence = new FakePersistence();
            var preferences = new QuickPlayPreferences(persistence, QuickPlayTestHelpers.WarningSink());

            preferences.Load();

            Assert.AreEqual(new[] { "vrcfury" }, preferences.SelectedToolIds.ToArray());
            Assert.IsTrue(preferences.IsSelected("vrcfury"));
            Assert.IsFalse(preferences.IsSelected("aao"));
        }

        [Test]
        public void EmptySelection_IsPersistedAndNotResetToDefaults()
        {
            var persistence = new FakePersistence();
            var preferences = new QuickPlayPreferences(persistence, QuickPlayTestHelpers.WarningSink());
            preferences.Load();

            Assert.IsTrue(preferences.TrySetSelected("vrcfury", false, out var error), error);
            Assert.IsEmpty(preferences.SelectedToolIds);

            var reloaded = new QuickPlayPreferences(persistence, QuickPlayTestHelpers.WarningSink());
            reloaded.Load();

            Assert.IsEmpty(reloaded.SelectedToolIds, "全部不勾选必须被保存，而不是回退默认值。");
            Assert.IsTrue(persistence.Stored.initialized);
        }

        [Test]
        public void UnknownHistoricalId_IsPreservedButIgnored()
        {
            var persistence = new FakePersistence();
            persistence.Seed(new QuickPlayPreferenceData
            {
                schemaVersion = 1,
                initialized = true,
                stripToolIds = new List<string> { "legacy-removed-tool", "aao" },
            });

            var preferences = new QuickPlayPreferences(persistence, QuickPlayTestHelpers.WarningSink());
            preferences.Load();

            CollectionAssert.Contains(preferences.SelectedToolIds, "legacy-removed-tool");
            Assert.IsTrue(preferences.IsSelected("aao"));
            Assert.IsFalse(QuickPlayToolRegistry.IsKnownId("legacy-removed-tool"));

            // 修改已知项后，未知 ID 仍被保留。
            Assert.IsTrue(preferences.TrySetSelected("vrcfury", true, out var error), error);
            CollectionAssert.Contains(persistence.Stored.stripToolIds, "legacy-removed-tool");
            CollectionAssert.Contains(persistence.Stored.stripToolIds, "vrcfury");
        }

        [Test]
        public void CorruptConfig_FallsBackToDefaultsWithWarning()
        {
            var persistence = new FakePersistence();
            persistence.SeedCorrupt();

            var preferences = new QuickPlayPreferences(persistence, QuickPlayTestHelpers.WarningSink());
            preferences.Load();

            Assert.AreEqual(new[] { "vrcfury" }, preferences.SelectedToolIds.ToArray());
            Assert.IsTrue(QuickPlayTestHelpers.HasWarningContaining("默认值"), "损坏配置必须给出明确告警。");
        }

        [Test]
        public void MissingInitializedFlag_WithStoredFile_IsTreatedAsCorrupt()
        {
            var persistence = new FakePersistence();
            persistence.Seed(new QuickPlayPreferenceData
            {
                schemaVersion = 1,
                initialized = false,
                stripToolIds = new List<string>(),
            });

            var preferences = new QuickPlayPreferences(persistence, QuickPlayTestHelpers.WarningSink());
            preferences.Load();

            Assert.AreEqual(new[] { "vrcfury" }, preferences.SelectedToolIds.ToArray());
            Assert.IsTrue(QuickPlayTestHelpers.HasWarningContaining("损坏"));
        }

        [Test]
        public void AbortedLoad_FallsBackToDefaultsWithWarning()
        {
            var persistence = new FakePersistence { FailLoad = true };
            persistence.Seed(new QuickPlayPreferenceData());

            var preferences = new QuickPlayPreferences(persistence, QuickPlayTestHelpers.WarningSink());
            preferences.Load();

            Assert.AreEqual(new[] { "vrcfury" }, preferences.SelectedToolIds.ToArray());
            Assert.IsTrue(QuickPlayTestHelpers.HasWarningContaining("无法读取"));
        }

        [Test]
        public void SaveFailure_RollsBackSelection()
        {
            var persistence = new FakePersistence();
            var preferences = new QuickPlayPreferences(persistence, QuickPlayTestHelpers.WarningSink());
            preferences.Load();

            persistence.FailSave = true;

            Assert.IsFalse(preferences.TrySetSelected("aao", true, out var error));
            Assert.IsNotEmpty(error);
            Assert.IsFalse(preferences.IsSelected("aao"), "保存失败时必须回滚显示/内存选择。");
            Assert.IsTrue(QuickPlayTestHelpers.HasWarningContaining("保存失败"));
        }

        [Test]
        public void UninstalledToolSelectionIsRetainedAcrossReload()
        {
            var persistence = new FakePersistence();
            var preferences = new QuickPlayPreferences(persistence, QuickPlayTestHelpers.WarningSink());
            preferences.Load();

            // Meshia 未安装，但偏好仍可保存（缺失工具不成为强制依赖）。
            Assert.AreEqual(QuickPlayToolAvailability.NotInstalled,
                QuickPlayToolRegistry.Evaluate(QuickPlayToolRegistry.Find("meshia")).Availability);
            Assert.IsTrue(preferences.TrySetSelected("meshia", true, out var error), error);

            var reloaded = new QuickPlayPreferences(persistence, QuickPlayTestHelpers.WarningSink());
            reloaded.Load();
            Assert.IsTrue(reloaded.IsSelected("meshia"), "工具暂时缺失时不得丢失已保存的选择。");
        }

        [Test]
        public void Snapshot_IsFrozenAgainstLaterChanges()
        {
            var persistence = new FakePersistence();
            var preferences = new QuickPlayPreferences(persistence, QuickPlayTestHelpers.WarningSink());
            preferences.Load();

            var snapshot = preferences.Snapshot();
            Assert.IsTrue(preferences.TrySetSelected("aao", true, out _));
            Assert.IsTrue(preferences.TrySetSelected("vrcfury", false, out _));

            CollectionAssert.AreEqual(new[] { "vrcfury" }, snapshot.stripToolIds.ToArray(),
                "会话快照不得随后续设置变化。");
        }

        [Test]
        public void DuplicateIdsAreDeduplicated()
        {
            var persistence = new FakePersistence();
            persistence.Seed(new QuickPlayPreferenceData
            {
                schemaVersion = 1,
                initialized = true,
                stripToolIds = new List<string> { "aao", "aao", "vrcfury" },
            });

            var preferences = new QuickPlayPreferences(persistence, QuickPlayTestHelpers.WarningSink());
            preferences.Load();

            Assert.AreEqual(2, preferences.SelectedToolIds.Count);
        }

        [Test]
        public void SettingsAsset_UsesProjectLocalUserSettingsPath()
        {
            var attribute = typeof(QuickPlaySettingsAsset)
                .GetCustomAttributes(typeof(FilePathAttribute), true)
                .Cast<FilePathAttribute>()
                .SingleOrDefault();

            Assert.IsNotNull(attribute, "配置必须使用 FilePath 特性落盘到项目内用户级目录。");
            Assert.AreEqual("UserSettings/MarbleAvatarToolbox/QuickPlaySettings.asset", QuickPlaySettingsAsset.RelativeAssetPath);

            var pathProperty = typeof(FilePathAttribute).GetProperty("filepath",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(pathProperty);
            Assert.AreEqual(QuickPlaySettingsAsset.RelativeAssetPath, pathProperty.GetValue(attribute));
        }

        [Test]
        public void SettingsAsset_ResolvesAbsolutePathUnderProjectUserSettings()
        {
            var resolved = QuickPlaySettingsAsset.ResolvedStoredFilePath;
            Assert.IsFalse(string.IsNullOrEmpty(resolved));
            Assert.IsTrue(System.IO.Path.IsPathRooted(resolved), "落盘路径必须可绝对化，避免受编辑器工作目录影响：" + resolved);
            StringAssert.Contains("UserSettings", resolved.Replace('/', '\\'));
            StringAssert.EndsWith(QuickPlaySettingsAsset.RelativeAssetPath.Replace('/', '\\'), resolved.Replace('/', '\\'));
        }
    }
}

#endif
