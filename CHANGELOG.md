# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog,
and this project adheres to Semantic Versioning.

## [1.1.1] - 2026-10-06
### Changed
- 移除 QuickPlay 退出时的 Selection 已变化与场景 dirty 提示，不改变恢复和清理行为。
- 清理完成日志改为英文，仅显示 Play 已退出与清理耗时：`[QuickPlay] Play exited. Cleanup took N ms.`

## [1.1.0] - 2026-10-06
### Added
- QuickPlay：`MarbleAvatarToolbox/QuickPlay`（以及 GameObject 右键入口）为选中 Avatar 创建临时副本并进入 Play，在副本上剔除所选优化配置；退出 Play 后恢复原对象并清理副本。
- QuickPlay Settings：十个固定工具级 Checkbox（VRCFury、AAO、d4rk Avatar Optimizer、Meshia、LAC、Overall、NDMF Mantis、旧版 lilNDMFMeshSimplifier、TexTransTool 仅贴图优化、VRCQuestTools 仅菜单图标优化），勾选即时保存；未安装工具禁用显示但保留偏好；全部不勾选也是合法配置。
- 用户级配置保存到 `UserSettings/MarbleAvatarToolbox/QuickPlaySettings.asset`，不写入场景或 Avatar 资产。

### Changed
- **BREAKING**：移除 `MarbleAvatarToolbox/Play Without VRCFury` 顶层与右键入口，统一由 QuickPlay 提供（不保留旧名别名）；首次配置默认仍只勾选 VRCFury。
- QuickPlay 要求 NDMF 的 Apply on Play 已开启；关闭时在修改场景前拒绝并提示，不修改全局开关。
- d4rk 与 VRCQuestTools 采用保守校验：接口不兼容时拒绝启动；抑制只作用于目标临时副本，不改写第三方全局设置。
- 失败闭合：本工具自身的准备、抑制或会话状态持久化失败会中止处理、退出 Play 并保留恢复记录，不会静默继续。

### Notes
- QuickPlay 仅用于临时预览：本版本**不做完整 SDK 构建结果校验**，也不会手工补发 SDK preprocess，因此预览结果不等同于最终上传/构建产物。
- 完整 SDK/NDMF 整链结果观测与相应验收留待后续版本；本版本不承诺构建结果、性能或场景重载行为已通过验证。

## [1.0.0] - 2026-05-09
### Changed
- Renamed package from `marble810.marbleavatartools` to `marble810.marbleavatartoolbox`.
- All namespaces, file paths, assembly definitions, Unity Editor menu paths, and CI/CD workflows updated to reflect the new Marble Avatar Toolbox identity.
