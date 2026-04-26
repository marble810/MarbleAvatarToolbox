## Why

当前项目中的 Avatar 在进入 Play 时会同时触发 NDMF 与 VRCFury 的预处理链，而 Mafuchiffon_FT_V1 根对象上已存在多个 VRCFury 配置组件。需要一个专用的测试入口，让用户能够在不改动原始 Avatar 结构与磁盘资源的前提下，进入一个“排除 VRCFury 影响”的 Play 会话，用于对比调试 NDMF、Avatar Optimizer 和其他系统在无 VRCFury 参与时的表现。

## What Changes

- 新增一个编辑器侧的“Play without VRCFury”入口，用于对选定 Avatar 发起一次性的特殊 Play 流程。
- 新增一个编辑器侧的“Copy without VRCFury to Hierarchy”入口，用于在 EditMode 下生成一个去除 VRCFury 组件的 Avatar 副本，并禁用原 Avatar。
- 进入 Play 前创建临时 Avatar 副本，仅在该副本上移除来自 VRCFury 运行时程序集的组件，避免修改原始对象或场景资源。
- 将两个入口整理到同一个子菜单中，统一用户的无 VRCFury 调试工作流。
- 在特殊 Play 会话中禁用原 Avatar，仅让剥离 VRCFury 的临时副本参与 Play。
- 退出 Play 回到 EditMode 后自动清理临时副本并恢复原 Avatar 状态。
- 在目标 Avatar 缺失、重复会话、清理失败等情况下提供明确的编辑器提示与兜底处理。

## Capabilities

### New Capabilities
- `play-without-vrcfury`: 提供一个基于临时 clone 的编辑器 Play 入口，使 Avatar 能在不修改原始结构的前提下，以排除 VRCFury 组件的方式进入 Play。

### Modified Capabilities

## Impact

- 受影响代码主要位于本包的 Editor 目录，需要新增一个 Play 会话控制器与入口 UI。
- 需要依赖 Unity Editor、VRCSDK、当前项目中已有的 VRCFury 与 NDMF 行为，但不引入新的第三方 Package。
- 需要与当前项目中已启用的 DisableDomainReload / DisableSceneReload 行为兼容，确保清理与恢复逻辑可靠。