#if UNITY_INCLUDE_TESTS

using System.Collections.Generic;
using System.Linq;
using marble810.MarbleAvatarToolbox.PlayModeTools;
using NUnit.Framework;

namespace MarbleQuickPlay.Tests
{
    /// <summary>
    /// 用户级配置持久化的真实后端事务测试（R5）与新 schema 行为一致性测试。
    /// 事务逻辑使用生产类 QuickPlayScriptableSingletonPersistence，仅注入可失败的资产访问器。
    /// </summary>
    [Category("QuickPlay")]
    public sealed class QuickPlaySettingsPersistenceTests
    {
        [Test]
        public void RealPersistence_SaveFailureRollsBackAssetState()
        {
            var accessor = new FakeSettingsAssetAccessor();
            accessor.Seed(QuickPlayPreferenceData.CurrentSchemaVersion, true, new[] { "vrcfury", "aao" });
            accessor.FailFlush = true;

            var persistence = new QuickPlayScriptableSingletonPersistence(accessor);

            var saved = persistence.TrySave(
                new QuickPlayPreferenceData
                {
                    schemaVersion = QuickPlayPreferenceData.CurrentSchemaVersion,
                    initialized = true,
                    stripToolIds = new List<string> { "meshia" },
                },
                out var error);

            Assert.IsFalse(saved);
            Assert.IsNotEmpty(error);
            CollectionAssert.AreEqual(new[] { "vrcfury", "aao" }, accessor.CurrentIds,
                "落盘失败后必须把内存状态回滚到写入前的值，避免 UI 与磁盘不一致。");
        }

        [Test]
        public void RealPersistence_SaveSuccessWritesThrough()
        {
            var accessor = new FakeSettingsAssetAccessor();
            accessor.Seed(QuickPlayPreferenceData.CurrentSchemaVersion, true, new[] { "vrcfury" });

            var persistence = new QuickPlayScriptableSingletonPersistence(accessor);

            var saved = persistence.TrySave(
                new QuickPlayPreferenceData
                {
                    schemaVersion = QuickPlayPreferenceData.CurrentSchemaVersion,
                    initialized = true,
                    stripToolIds = new List<string> { "aao" },
                },
                out var error);

            Assert.IsTrue(saved, error);
            CollectionAssert.AreEqual(new[] { "aao" }, accessor.CurrentIds);
            Assert.IsTrue(accessor.CurrentInitialized);
        }

        [Test]
        public void RealPersistence_LoadReturnsNullWhenAssetNotInitialized()
        {
            var accessor = new FakeSettingsAssetAccessor();
            accessor.Seed(QuickPlayPreferenceData.CurrentSchemaVersion, false, Enumerable.Empty<string>());

            var persistence = new QuickPlayScriptableSingletonPersistence(accessor);

            Assert.IsTrue(persistence.TryLoad(out var data, out _));
            Assert.IsNull(data);
        }

        [Test]
        public void RealPersistence_LoadReportsReadFailure()
        {
            var accessor = new FakeSettingsAssetAccessor { FailRead = true };
            var persistence = new QuickPlayScriptableSingletonPersistence(accessor);

            Assert.IsFalse(persistence.TryLoad(out _, out var error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void RealPersistence_HasStoredDataUsesResolvedAbsolutePath()
        {
            var accessor = new FakeSettingsAssetAccessor { FilePath = string.Empty };
            var persistence = new QuickPlayScriptableSingletonPersistence(accessor);
            Assert.IsFalse(persistence.HasStoredData());

            // 用一个确实存在的文件验证存在性判定（只读）。
            var existing = System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "Packages", "manifest.json");
            accessor.FilePath = System.IO.Path.GetFullPath(existing);
            Assert.IsTrue(persistence.HasStoredData());
        }

        [Test]
        public void SettingsAsset_PathAndImportContract()
        {
            var attribute = typeof(QuickPlaySettingsAsset)
                .GetCustomAttributes(typeof(UnityEditor.FilePathAttribute), true)
                .Cast<UnityEditor.FilePathAttribute>()
                .SingleOrDefault();

            Assert.IsNotNull(attribute, "配置必须使用 FilePath 特性落盘到项目内用户级目录。");
            Assert.AreEqual("UserSettings/MarbleAvatarToolbox/QuickPlaySettings.asset", QuickPlaySettingsAsset.RelativeAssetPath);

            var pathProperty = typeof(UnityEditor.FilePathAttribute).GetProperty("filepath",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(pathProperty);
            Assert.AreEqual(QuickPlaySettingsAsset.RelativeAssetPath, pathProperty.GetValue(attribute));

            var resolved = QuickPlaySettingsAsset.ResolvedStoredFilePath;
            Assert.IsTrue(System.IO.Path.IsPathRooted(resolved), "落盘路径必须可绝对化：" + resolved);
            StringAssert.EndsWith(QuickPlaySettingsAsset.RelativeAssetPath.Replace('/', '\\'), resolved.Replace('/', '\\'));
        }

        [Test]
        public void Preferences_NewerSchemaWarningMatchesActualWriteBehaviour()
        {
            var persistence = new FakePersistence();
            persistence.Seed(new QuickPlayPreferenceData
            {
                schemaVersion = QuickPlayPreferenceData.CurrentSchemaVersion + 1,
                initialized = true,
                stripToolIds = new List<string> { "vrcfury" },
            });

            QuickPlayTestHelpers.ClearWarnings();
            var preferences = new QuickPlayPreferences(persistence, QuickPlayTestHelpers.WarningSink());
            preferences.Load();

            Assert.IsTrue(QuickPlayTestHelpers.HasWarningContaining("更新的 schema"));
            Assert.IsFalse(QuickPlayTestHelpers.HasWarningContaining("只读"),
                "警告文案必须与实际写入行为一致（这里确实会写回当前 schema）。");

            // 实际写入行为：保存成功，并以当前 schema 写回。
            Assert.IsTrue(preferences.TrySetSelected("aao", true, out var error), error);
            Assert.AreEqual(1, persistence.SaveCount);
            Assert.AreEqual(QuickPlayPreferenceData.CurrentSchemaVersion, persistence.Stored.schemaVersion);
        }
    }
}

#endif
