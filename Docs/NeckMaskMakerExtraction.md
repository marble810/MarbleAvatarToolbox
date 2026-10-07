# NeckMaskMaker 分离记录

迁移前完整工作区已提交为 `d60c4a3dd5eac4a1b5d606f41a63dca97f36b389`。NeckMaskMaker 源码、Shader、回归测试和初始计划移至独立仓库 [marble810/NeckMaskMaker](https://github.com/marble810/NeckMaskMaker)，本地路径 `F:/Git/NeckMaskMaker`。

新包 `marble810.neckmaskmaker` 不依赖 Toolbox；Toolbox 不自动安装新包，其原包 ID、依赖、1.1.1 版本与历史 Release 保持不变。原正式发布版本未包含本工具。菜单改为 `Tools/Marble/Neck Mask Maker`。

VPM 总列表仍为 `https://marble810.github.io/vpmlist/index.json`，source.json 追加新仓库。独立列表为 `https://marble810.github.io/NeckMaskMaker/index.json`，仅在发布后提供可安装版本。本次不打 Release tag。上游生成器不支持无 Release 的来源，已给总列表和独立列表增加生成前检查：未发布时暂时跳过，完整首版发布后自动纳入，不改变仓库中的来源配置。

测试工程旧实现备份在 `F:/Project_VRC_Local/Test/Temp/NeckMaskMaker/extraction-backup`，只定向清除旧 NeckMask 文件，再链接新独立包。回滚须先解除新包链接，再恢复备份，不能把两份相同 GUID 的实现同时导入 Unity。其他 Toolbox 工具、场景、Avatar、材质、已有 PNG 不变。

## 验证

UnityMCP 的 6 项合成回归、2 项真实 Avatar 回归通过；实际域重载保持窗口状态，关闭处理释放 GPU/材质，Scene 未变脏。静态包检查 5 项和 VPM 未发布来源检查 4 项通过。

独立源码首提交 `7b0f83b`，列表边界修正 `5940139`；VPM 来源提交 `18ff760`、未发布来源保护提交 `9a52229`，均已推送。独立仓库 CI 和无 Release 跳过流程成功，总列表生成及 Pages 部署成功，产物中保留 Toolbox 的三个历史版本、原列表 ID/URL，未伪造 NeckMask 下载版本。

上述拆分阶段仅提交源码，未打发布 tag。后续用户确认推送，并于 2026-10-08 确认发布 Toolbox 1.1.2 与 NeckMaskMaker 0.1.0。

## 首次发布

两个 Release 和自动通知 VPM 总列表均成功，总列表现已包含 Toolbox 1.1.2 与 NeckMaskMaker 0.1.0，旧 Toolbox 版本保持。新仓库 Secret 由用户配置，Token 未从旧仓库复制或读回。

实下载 ZIP 的 SHA256 与总列表及各自独立列表一致；独立 NeckMask 网站的首次模板遗漏已修复，未改写已发布 tag/ZIP。完整记录见 [首次发布验证](https://github.com/marble810/NeckMaskMaker/blob/main/Docs/ReleaseVerification.md)。VCC 安装/升级/卸载与人工视觉验收仍待用户确认。

详细迁移边界、偏好迁移、待验收项与发布配置见独立仓库 [Docs/Extraction.md](https://github.com/marble810/NeckMaskMaker/blob/main/Docs/Extraction.md)。本仓库 OpenSpec 历史保留，新的插件功能规格和后续工作由独立仓库维护。
