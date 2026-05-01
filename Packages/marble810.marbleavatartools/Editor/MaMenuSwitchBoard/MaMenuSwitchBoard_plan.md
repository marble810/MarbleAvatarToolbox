# 总览
- 获取所选Avatar的Reaction Object MenuItems
  - 所选Avatar：通过SerializedProperty让用户输入Avatar GameObject
  - 如何获取MenuItems？：通过ndmf framework还是遍历子GameObjects？
- Flatten these MenuItems to a ListView
  - Hiererachy View: ▼ SubMenu -> MenuItem
- Add State Override Buttons
  - make three btns like @../../../nadena.dev.modular-avatar/Editor/ReactiveObjects/Simulator/StateOverrideController.cs
  ```C#
    btn_disable.text = "-";
    btn_default.text = " ";
    btn_enable.text = "+";
  ```
  - StateOverrideController is internal class. Can I ref it? if ok we can just reuse it.
- Make The State Override Buttons have the same features as ROSimulator
- Check the analysis doc about ROSimulator @../../../../.claude/docs/Simulator_MenuItem_Override_API.md
- Generate the script to MaMenuSwitchBoard.cs