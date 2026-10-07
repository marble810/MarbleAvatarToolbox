# MarbleAvatarToolbox

## 工具列表
- **Keyframe Mirror**（`MarbleAvatarToolbox/Keyframe Mirror`）：按 Left/Right 等左右命名模式自动匹配对称骨骼，将动画片段全部或选中的关键帧沿指定镜像轴在参考根局部空间内镜像，写回左右两侧骨骼。
- **MaMenuSwitchBoard**（`MarbleAvatarToolbox/MaMenuSwitchBoard`）：集中列出场景 Avatar 的全部 MA 菜单项，借助 MA Simulator API 在编辑器内直接覆盖/切换菜单参数状态进行预览，无需进入 Play；支持收藏、EditorOnly 过滤与快速聚焦。
- **PhysBone Colliders Batch Setup**（`MarbleAvatarToolbox/PhysBone/PhysBone Colliders Batch Setup`）：将一组 VRCPhysBoneCollider 批量应用到多个目标 VRCPhysBone，一次性覆盖其碰撞体列表。
- **PhysBone Duplicater**（`MarbleAvatarToolbox/PhysBone/PhysBone Duplicater`）：在源与目标骨骼间按层级自动匹配，把源骨骼链上的 VRCPhysBone 配置整链复制到目标骨骼链，支持撤销。
- **QuickPlay**（`MarbleAvatarToolbox/QuickPlay`，右键 `GameObject/marbleTools/QuickPlay`）：为选中 Avatar 创建临时副本并进入 Play，在副本上剔除所选优化配置；退出 Play 后恢复原对象并清理副本。默认仅勾选 VRCFury（承接原 Play Without VRCFury 用途）。
  - **基础版边界**：仅用于临时预览，要求 NDMF 的 Apply on Play 已开启；本版本**不做完整 SDK 构建结果校验**，也不会手工补发 SDK preprocess，预览结果不等同于最终上传/构建产物；完整整链结果观测留待后续版本。
- **QuickPlay Settings**（`MarbleAvatarToolbox/QuickPlay Settings`）：用十个工具级 Checkbox 选择 QuickPlay 要剔除的工具，勾选即时保存到项目内用户级配置（`UserSettings/MarbleAvatarToolbox/QuickPlaySettings.asset`）。未安装的工具会禁用显示但保留偏好；全部不勾选也是合法配置。不兼容的工具会在启动前提示，请取消勾选或使用兼容版本。


## 独立插件

[Neck Mask Maker](https://github.com/marble810/NeckMaskMaker) 已从本仓库拆出，包 ID 为 `marble810.neckmaskmaker`，菜单为 `Tools/Marble/Neck Mask Maker`。需要单独安装；Toolbox 不会自动依赖它。既有 Toolbox 正式发布版本未包含该工具。

两个包共用 [Marble VPM 总列表](https://marble810.github.io/vpmlist/index.json)，原订阅地址不变。独立插件源码迁移不等同于正式发布，请以其 Release 和列表中的版本为准。开发副本迁移与回滚说明见 [拆分记录](Docs/NeckMaskMakerExtraction.md)。

## 开发
### 环境配置
#### 使用Symbolink将Package链接到Unity
- 在 `Script/dev.env` 文件中设置 `UNITYPROJECT` 变量，指向你的 Unity 项目路径。
- 运行`Script/symlink-to-unity.ps1`脚本来创建符号链接。
- 运行`Script/unlink.ps1`脚本来移除符号链接。
- 确保使用`Powershell 7`以保证兼容性

## Release
- 运行 `pwsh Script/prepare-release.ps1 patch`，或将 `patch` 改成 `minor` / `major`。
- 第一次运行会在根目录 `CHANGELOG.md` 中自动插入目标版本节，然后停止，等待补全发布说明。
- 填完对应版本节后再次运行脚本，脚本会校验说明已完成、更新 `Packages/marble810.marbleavatartoolbox/package.json` 的版本号、提交 commit、打纯版本号 tag 并推送。
- 远端 `Build Release` workflow 会在 tag 推送后生成 `.zip`、`.unitypackage` 与 GitHub Release。
- `Build Repo Listing` workflow 会在 `Build Release` 成功后自动重建本仓库 VPM listing，并保留手动触发入口。
- `Notify VPM List` workflow 会在 `Build Release` 成功后向 `marble810/vpmlist` 发送 `repository_dispatch` 事件；如果需要补发，可以手动触发这个 workflow。
- 使用跨仓库通知前，需要在当前仓库配置 `VPMLIST_DISPATCH_TOKEN` secret。该 token 对目标仓库 `marble810/vpmlist` 至少需要 `Contents: write` 的 fine-grained 权限，或 classic PAT 的 `repo` scope。
- 更详细的本地使用说明见 `docs/local/release-workflow.md`。