# NeckMaskMaker 分离记录

迁移前完整工作区已提交为 `d60c4a3dd5eac4a1b5d606f41a63dca97f36b389`。NeckMaskMaker 源码、Shader、回归测试和初始计划移至独立仓库 [marble810/NeckMaskMaker](https://github.com/marble810/NeckMaskMaker)，本地路径 `F:/Git/NeckMaskMaker`。

新包 `marble810.neckmaskmaker` 不依赖 Toolbox；Toolbox 不自动安装新包，其原包 ID、依赖、1.1.1 版本与历史 Release 保持不变。原正式发布版本未包含本工具。菜单改为 `Tools/Marble/Neck Mask Maker`。

VPM 总列表仍为 `https://marble810.github.io/vpmlist/index.json`，source.json 追加新仓库。独立列表为 `https://marble810.github.io/NeckMaskMaker/index.json`，仅在发布后提供可安装版本。本次不打 Release tag。上游生成器不支持无 Release 的来源，已给总列表和独立列表增加生成前检查：未发布时暂时跳过，完整首版发布后自动纳入，不改变仓库中的来源配置。

测试工程旧实现备份在 `F:/Project_VRC_Local/Test/Temp/NeckMaskMaker/extraction-backup`，只定向清除旧 NeckMask 文件，再链接新独立包。回滚须先解除新包链接，再恢复备份，不能把两份相同 GUID 的实现同时导入 Unity。其他 Toolbox 工具、场景、Avatar、材质、已有 PNG 不变。

## 验证

UnityMCP 的 6 项合成回归、2 项真实 Avatar 回归通过；实际域重载保持窗口状态，关闭处理释放 GPU/材质，Scene 未变脏。静态包检查 5 项和 VPM 未发布来源检查 4 项通过。

独立源码首提交 `7b0f83b`，列表边界修正 `5940139`；VPM 来源提交 `18ff760`、未发布来源保护提交 `9a52229`，均已推送。独立仓库 CI 和无 Release 跳过流程成功，总列表生成及 Pages 部署成功，产物中保留 Toolbox 的三个历史版本、原列表 ID/URL，未伪造 NeckMask 下载版本。

首次正式发布与最小权限 `VPMLIST_DISPATCH_TOKEN` 配置仍需后续完成。本仓库本次只做两个本地 commit，不自动推送或发布 Toolbox。

详细迁移边界、偏好迁移、待验收项与发布配置见独立仓库 [Docs/Extraction.md](https://github.com/marble810/NeckMaskMaker/blob/main/Docs/Extraction.md)。本仓库 OpenSpec 历史保留，新的插件功能规格和后续工作由独立仓库维护。
