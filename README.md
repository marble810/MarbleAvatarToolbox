# MarbleAvatarToolbox

## 工具列表
### 


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
- 填完对应版本节后再次运行脚本，脚本会校验说明已完成、更新 `Packages/marble810.marbleavatartools/package.json` 的版本号、提交 commit、打纯版本号 tag 并推送。
- 远端 `Build Release` workflow 会在 tag 推送后生成 `.zip`、`.unitypackage` 与 GitHub Release；`Build Repo Listing` workflow 会在 release 变更后重建 VPM listing 并发布到 GitHub Pages。
- 更详细的本地使用说明见 `docs/local/release-workflow.md`。