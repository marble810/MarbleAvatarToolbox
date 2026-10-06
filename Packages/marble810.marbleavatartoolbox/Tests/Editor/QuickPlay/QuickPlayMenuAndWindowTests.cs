#if UNITY_INCLUDE_TESTS

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using marble810.MarbleAvatarToolbox.PlayModeTools;
using NUnit.Framework;
using UnityEditor;

namespace MarbleQuickPlay.Tests
{
    /// <summary>
    /// 菜单/窗口契约测试：QuickPlay 入口存在、旧入口消失、GUI 只包含约定元素。
    /// 不打开窗口、不启动会话。
    /// </summary>
    [Category("QuickPlay")]
    public sealed class QuickPlayMenuAndWindowTests
    {
        private static List<string> CollectMenuItemPaths()
        {
            var paths = new List<string>();
            var assembly = typeof(QuickPlaySettingsWindow).Assembly;

            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Instance |
                                                        BindingFlags.Public | BindingFlags.NonPublic |
                                                        BindingFlags.DeclaredOnly))
                {
                    foreach (var attribute in method.GetCustomAttributes(true))
                    {
                        if (!(attribute is MenuItem menuItem)) continue;
                        var value = typeof(MenuItem)
                            .GetField("menuItem",
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?.GetValue(menuItem) as string;
                        if (!string.IsNullOrEmpty(value)) paths.Add(value);
                    }
                }
            }

            return paths;
        }

        [Test]
        public void Menus_ExposeQuickPlayAndSettingsEntries()
        {
            var paths = CollectMenuItemPaths();

            CollectionAssert.Contains(paths, "MarbleAvatarToolbox/QuickPlay");
            CollectionAssert.Contains(paths, "MarbleAvatarToolbox/QuickPlay Settings");
            CollectionAssert.Contains(paths, "GameObject/marbleTools/QuickPlay");
        }

        [Test]
        public void Menus_DoNotExposeLegacyPlayWithoutVrcFuryEntry()
        {
            var paths = CollectMenuItemPaths();

            Assert.IsFalse(paths.Any(path => path.IndexOf("Play Without VRCFury", System.StringComparison.OrdinalIgnoreCase) >= 0),
                "旧 Play Without VRCFury 入口必须移除，且不提供旧名别名。");
        }

        [Test]
        public void Window_TextContractMatchesSpec()
        {
            Assert.AreEqual("请选择剔除的工具", QuickPlaySettingsWindow.HeaderText);
            Assert.AreEqual("仅影响 QuickPlay；剔除后的预览可能不同于最终构建。", QuickPlaySettingsWindow.RiskText);
            Assert.AreEqual("QuickPlay Settings", QuickPlaySettingsWindow.WindowTitle);
            StringAssert.Contains("未安装", QuickPlaySettingsWindow.NotInstalledSuffix);
        }

        [Test]
        public void Window_IsEditorWindowWithScrollSupportOnly()
        {
            Assert.IsTrue(typeof(EditorWindow).IsAssignableFrom(typeof(QuickPlaySettingsWindow)));

            var fields = typeof(QuickPlaySettingsWindow)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Select(field => field.FieldType.Name)
                .ToArray();

            CollectionAssert.Contains(fields, "Vector2", "窗口应使用滚动位置字段支持滚动列表。");

            var methods = typeof(QuickPlaySettingsWindow).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Select(m => m.Name).ToArray();
            Assert.IsFalse(methods.Contains("OnInspectorUpdate"), "不引入额外复杂面板逻辑。");
        }
    }
}

#endif
