# MarbleAvatarToolbox

## 工具列表
- **Keyframe Mirror**（`MarbleAvatarToolbox/Keyframe Mirror`）：按 Left/Right 等左右命名模式自动匹配对称骨骼，将动画片段全部或选中的关键帧沿指定镜像轴在参考根局部空间内镜像，写回左右两侧骨骼。
- **MaMenuSwitchBoard**（`MarbleAvatarToolbox/MaMenuSwitchBoard`）：集中列出场景 Avatar 的全部 MA 菜单项，借助 MA Simulator API 在编辑器内直接覆盖/切换菜单参数状态进行预览，无需进入 Play；支持收藏、EditorOnly 过滤与快速聚焦。
- **PhysBone Colliders Batch Setup**（`MarbleAvatarToolbox/PhysBone/PhysBone Colliders Batch Setup`）：将一组 VRCPhysBoneCollider 批量应用到多个目标 VRCPhysBone，一次性覆盖其碰撞体列表。
- **PhysBone Duplicater**（`MarbleAvatarToolbox/PhysBone/PhysBone Duplicater`）：在源与目标骨骼间按层级自动匹配，把源骨骼链上的 VRCPhysBone 配置整链复制到目标骨骼链，支持撤销。
- **Play Without VRCFury**（`MarbleAvatarToolbox/Play Without VRCFury`）：为选中 Avatar 创建移除 VRCFury 组件的临时克隆并进入 Play 模式，在 VRCFury 构建失败时仍可测试其余 NDMF 插件的处理结果，退出 Play 后自动恢复并清理克隆。


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