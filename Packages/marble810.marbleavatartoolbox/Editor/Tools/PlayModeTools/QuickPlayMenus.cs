using UnityEditor;
using UnityEngine;

namespace marble810.MarbleAvatarToolbox.PlayModeTools
{
    /// <summary>
    /// QuickPlay 顶层菜单与 GameObject 右键入口。两个入口共用同一套会话逻辑与配置。
    /// 原 Play Without VRCFury 入口已移除，不提供旧名别名。
    /// </summary>
    internal static class QuickPlayMenus
    {
        private const string TopMenuPath = "MarbleAvatarToolbox/QuickPlay";
        private const string ContextMenuPath = "GameObject/marbleTools/QuickPlay";

        [MenuItem(TopMenuPath, false, 20)]
        private static void StartFromMenu()
        {
            QuickPlaySession.Start(Selection.activeGameObject);
        }

        [MenuItem(TopMenuPath, true)]
        private static bool ValidateStartFromMenu()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem(ContextMenuPath, false, 0)]
        private static void StartFromContextMenu(MenuCommand command)
        {
            QuickPlaySession.Start(command.context as GameObject);
        }

        [MenuItem(ContextMenuPath, true)]
        private static bool ValidateStartFromContextMenu(MenuCommand command)
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && command.context is GameObject;
        }
    }
}
