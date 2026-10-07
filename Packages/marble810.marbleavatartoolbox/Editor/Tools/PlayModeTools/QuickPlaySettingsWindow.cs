using UnityEditor;
using UnityEngine;

namespace marble810.MarbleAvatarToolbox.PlayModeTools
{
    /// <summary>
    /// QuickPlay Settings：固定十项工具 Checkbox。
    /// 只修改用户级配置；打开/修改窗口不创建 clone、不进入 Play、不执行剥离。
    /// </summary>
    internal sealed class QuickPlaySettingsWindow : EditorWindow
    {
        internal const string MenuPath = "MarbleAvatarToolbox/QuickPlay Settings";
        internal const string WindowTitle = "QuickPlay Settings";
        internal const string HeaderText = "请选择剔除的工具";
        internal const string NotInstalledSuffix = "\n\n（未安装）";
        internal const string IncompatibleSuffix = "\n\n（当前版本不兼容，勾选后无法启动）：";

        private static QuickPlayPreferences _preferences;
        private Vector2 _scrollPosition;

        [MenuItem(MenuPath, false, 100)]
        private static void ShowWindow()
        {
            var window = GetWindow<QuickPlaySettingsWindow>(false, WindowTitle, true);
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(320f, 200f);
            window.Show();
        }

        internal static QuickPlayPreferences Preferences
        {
            get
            {
                if (_preferences == null)
                {
                    _preferences = new QuickPlayPreferences(
                        new QuickPlayScriptableSingletonPersistence(),
                        Debug.LogWarning);
                    _preferences.Load();
                }

                return _preferences;
            }
        }

        private void OnGUI()
        {
            var preferences = Preferences;

            EditorGUILayout.LabelField(HeaderText, EditorStyles.boldLabel);
            EditorGUILayout.Space(2f);

            using (var scroll = new EditorGUILayout.ScrollViewScope(
                       _scrollPosition,
                       GUILayout.ExpandHeight(true),
                       GUILayout.ExpandWidth(true)))
            {
                _scrollPosition = scroll.scrollPosition;

                foreach (var tool in QuickPlayToolRegistry.Tools)
                {
                    DrawToolToggle(tool, preferences);
                }
            }
        }

        private static void DrawToolToggle(QuickPlayToolDefinition tool, QuickPlayPreferences preferences)
        {
            var capability = QuickPlayToolRegistry.Evaluate(tool);
            var installed = capability.Availability != QuickPlayToolAvailability.NotInstalled;
            var tooltip = capability.Availability switch
            {
                QuickPlayToolAvailability.NotInstalled => tool.Tooltip + NotInstalledSuffix,
                QuickPlayToolAvailability.Incompatible => tool.Tooltip + IncompatibleSuffix + capability.Reason,
                _ => tool.Tooltip,
            };

            var checkedState = preferences.IsSelected(tool.Id);

            using (new EditorGUI.DisabledScope(!installed))
            {
                var newValue = EditorGUILayout.ToggleLeft(new GUIContent(tool.Label, tooltip), checkedState);
                if (newValue == checkedState) return;

                if (!preferences.TrySetSelected(tool.Id, newValue, out var error))
                {
                    EditorUtility.DisplayDialog(
                        "QuickPlay 设置",
                        "保存 QuickPlay 设置失败，已回滚本次修改：\n" + error,
                        "确定");
                }
            }
        }

        /// <summary>测试辅助：重置缓存的偏好实例。</summary>
        internal static void ResetForTests(QuickPlayPreferences preferences = null)
        {
            _preferences = preferences;
        }
    }
}
