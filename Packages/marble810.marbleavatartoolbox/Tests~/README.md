# Neck Mask Maker 回归验证

`Tests~` 不参与 Unity 导入。`.cs` 文件是 UnityMCP `execute_code` 的方法体，不是生产脚本；通过 MCP 执行其全文，选择 Roslyn 编译器。仅使用现有 Unity API 与反射，不引入测试依赖。

- `NeckMaskMakerRegression.cs`：调用生产拓扑和循环函数，验证三层圆柱、全部三角形拆点的圆柱、闭合四价扇面对侧、三面共边拒绝、预览 Shader 编译支持。无需打开工具或修改场景。
- `NeckMaskLocalizationRegression.cs`：只读文案表与源码，验证四列结构、键等于中文原文、三语非空且不重复、下拉框显示 `中文` / `日本語` / `English`、ZH/JP/EN 切换、缺键回退、`F()` 占位符格式化、EditorPrefs 记忆与三个代码文件无裸中文字面量。不修改场景或资产。
- `NeckMaskMakerRealAvatarRegression.cs`：依赖本次复现 Avatar、已打开窗口和当前顶部边选择，验证顶部循环闭合且高度跨度小于 0.05 米。该高度阈值仅适用于此固定模型，不是生产算法的阈值。

## 2026-07-17 验证结果

Unity 2022.3.22f1，测试工程 `F:/Project_VRC_Local/Test` 使用独立 Package 副本，已同步本次修改的 C# 和 Shader。

- 修复前：真实 Body_Base 顶部种子边 6518，260 顶点，高度跨度 0.465951 米，测试失败。
- 修复后：30 个唯一顶点，31 个绘制索引（最后重复首点以显式闭环），高度跨度 0.02526581 米，测试通过。
- 合成拓扑与 Shader 回归全部通过，Console 未发现 error。
- 临时启用 Preview 捕获连续两帧，两帧全图及颈部区域像素差均为空；验证结束恢复 Preview 原来的关闭状态。
- 截图位于测试工程 `Temp/NeckMaskMaker/neck-loop-fixed.png`、`neck-preview-a.png`、`neck-preview-b.png`。

## 第二轮：边线投影与宽度

- 原 Handles AA 边线在真实模型无遮挡边段偏离相机投影约 7–8 像素，宽度阈值计数从 1 到 5 不等。
- 改为独立屏幕空间三角带 Shader，固定 3 个逻辑像素宽度，按 DPI 缩放。补偿渲染纹理投影 Y 翻转，否则斜线的扩展法线会变成沿线方向，造成线条变细。
- 选边采用 X-Ray 辅助线，避免逐像素深度裁切导致半条线消失；红色 Mask 表面仍使用深度测试。
- `NeckMaskEdgeGpuRegression.cs`：调用生产线网格构建函数，在真实 GPU 上验证透视／正交、两种距离与水平／45°／竖线共 12 组组合。中心误差最多 0.5 像素，亮度积分宽度 3.420–3.467 像素，组合之间变化不到 0.05 像素。亮度积分包含颜色空间和 AA 的影响，不等同于 UI 标称宽度。
- `NeckMaskEdgeScreenshotRegression.py`：固定模型截图检测；需要开发环境已有 Pillow，不构成 Unity 插件依赖。原截图所有检查边段失败，新截图通过。截图原点受 GrabPixels / DPI 取整影响，图像检查允许 3.5 像素误差；精确绘制中心另由 GPU 测试覆盖。
- 真实边中点拾取通过，并验证设置非单位 Handles.matrix 后仍能正确拾取。拾取与边线共用相机像素投影。
- 第二轮截图在测试工程 `Temp/NeckMaskMaker/edge-before.png`、`edge-after-flip.png`，投影参照为 `projected.txt`。

## 仍需人工确认

静止帧比较不能证明所有角度或动态姿态下完全没有抖动。打开 Preview 并拖动 Scene 视角、缩放、改变姿态，确认遮罩稳定且项圈和头发仍正确遮挡预览。

拓扑位置焊接容差为局部空间 1e-6。原始网格和 UV 不变；重合多层面可能产生非流形边，这类边会拒绝继续推导，不做任意分叉选择。

## 材质槽隔离回归

- `NeckMaskMaterialSlotRegression.cs`：经生产窗口与 GPU 管线验证双槽完全重叠 UV 得到白／黑而非平均灰；相同 Material 不合并；预览网格仅含本槽索引；面板 RT 与 PNG 独立；参数复用缓存；勾选同步和 JsonUtility 序列化保持；空选择、空材质、混合拓扑中的非三角形槽、额外材质槽映射以及批量烘焙错误拒绝。测试夹具为内存对象，EditorJsonUtility 会置空其非持久化引用，使用 JsonUtility 保留 instanceID，不代表执行了实际域重载。
- `NeckMaskMaterialSlotRealAvatarRegression.cs`：复用真实模型与已选循环线到独立测试窗口，调用生产保存函数写入 `Temp/NeckMaskMaker/material-slots/`，逐像素比较 PNG 与对应槽 RT；验证子网格预览索引及原材质引用。
- Unity 2022.3.22f1 / D3D11，全部通过。真实 256 输出：Body_base 槽 0 为 18802 三角形／36726 覆盖像素，Body 槽 0 为 6106／46416，Body 槽 1 为 2972／29083。三张独立 PNG 保存成功。
- 原 GPU、拓扑回归继续通过，Console 无 error；工程其他插件仍有既有 warnings。窗口视觉检查确认勾选列表、材质引用与保存按钮完整显示。
- 本轮脚本重新编译后，原窗口仍保持 Body_base 槽 0、Body 槽 0 选中，Body 槽 1 未选中的状态。Unity 完整重启、连续视角与动态姿态仍需人工检查。不更改原窗口勾选或 Avatar。

## 窗口布局重排（2026-10-07）

- 「目标材质槽」按 Body_base / Body 分栏，行内只保留勾选框、槽号与材质引用；四个功能区块改为较暗 Card 底板并保留标题；「表面预览」/「贴图预览」统一文案与控件样式并合并到「预览」区块；保存按钮固定在窗口底边；打开时窗口按默认尺寸（520 x 700）拉高。
- 已验证：`refresh_unity` 后 Console 无 error；窗口截图为实际渲染结果：两栏材质槽、五张 Card、预览区块的并列开关、底部保存按钮均正确显示。
- 发现并修复：Card 样式最初以 `GUI.skin.box` 为基底，`normal.background` 的纯色贴图赋值在绘制时不生效，画出的是 Unity 自带浅灰 box 贴图；改用 `EditorStyles.helpBox` 为基底后底色正确。
- 底色与描边按后续要求调成「只比背景暗一点点 + 1px 更深描边」：Pro 主题实测背景 56、Card 填充 51、描边 28（0.20 / 0.11），扫描线逐像素验证描边正好占 1 像素。
- 内容区四周留出相同外边距（左右实测各 10 物理像素、上下同样），底部保存按钮左右边缘与 Card 对齐（实测 18..666 对比 Card 17..671，受滚动条占位影响差 5 物理像素）。
- 行为回归（`NeckMaskGpuRegression` / `NeckMaskMaterialSlotRegression` / `NeckMaskMakerRegression`）不受影响，仍全部 PASS；材质槽角色名提取为常量后与导出文件名、错误文案使用的 `role` 完全一致。

## 精简参数与选项（2026-10-07）

- 按用户要求移除「从当前选择自动填充」、「Fill Mode」、「Write Alpha」与「仅烘焙 Body_base」，对应业务代码同步删除：
  - Mask 固定为带状填充，Compute Shader 不再接收 `_FillMode`；侧向信息（`_Centroid` / `_Normal` 与 `field.y`）与 Body_base 顶点分布求向代码一并移除，距离缓存从 `float2` 改为单个 `float`（`_Distances`）。
  - Alpha 恒等于 Mask，Shader 不再接收 `_WriteAlpha`。
  - 材质槽列表不再按 Body / Body_base 过滤，全部有效槽均可勾选与烘焙。
- 已验证：`refresh_unity` 后 Console 无 error；三个 Roslyn 回归（`NeckMaskGpuRegression`、`NeckMaskMaterialSlotRegression`、`NeckMaskMakerRegression`）全部 PASS；反射确认 `_fillMode`、`_writeAlpha`、`_bodyBaseOnly`、`AutoFillTargetsFromSelection` 均已删除，`Prepare` 为 9 个参数，`LoopGeometry` 只剩 `segments` / `valid`。
- 窗口截图确认：目标对象仅剩 `Body` / `Body_base` / 「刷新网格」；Mask 参数只剩 Max Distance、Smoothness、Texture Size、Dilation；输出只剩 Preview、保存与贴图预览。

## 界面语言切换（2026-10-07）

- 窗口内容区顶部新增固定一行语言选择（标签固定 `Language`，下拉框按各自语言显示 `中文` / `日本語` / `English`），语言标签与下拉框选项带 Tooltip「由 Deepseek V4.1 Flash 翻译」；语言写入 `EditorPrefs`（`MarbleAvatarToolbox.NeckMaskMaker.Language`），切换后立即重绘并清空已按旧语言格式化的状态与逐槽错误。
- 文案集中在 `NeckMaskLocalization.cs` 的四列文案表（键 = 中文原文 / ZH / JP / EN），键缺失时回退中文原文；参数名与导出文件名保持英文。窗口默认高度按新增语言行从 720 调到 748。
- 已验证：`refresh_unity` 后 Console 无 error；`NeckMaskLocalizationRegression.cs` 返回 `PASS: 文案表 76 条 / 三语切换 / 缺键回退 / F() 格式化 / EditorPrefs 记忆 / 源码无裸文案`。
- 窗口截图确认真实渲染：ZH / JP / EN 三语下 Card 标题、按钮、状态文案与底部保存按钮全部随语言切换，下拉框按各自语言显示当前语言（中文 / 日本語 / English）。
- 发现并修复：`EditorGUILayout.Popup(GUIContent label, ...)` 与 `GUILayout.Width(150f)` 同时使用时宽度被算作「标签 + 控件」总宽，标签占满后控件被挤到几乎为零、看不到当前语言；改为标签与下拉框分开绘制（标签 80、下拉框 150）。

## 衰减模式 Linear / Smooth / Constant（2026-10-07）

- 按用户要求移除 `Smoothness`：Mask 参数区新增 `Sampling Mode` 下拉（`Linear` / `Smooth` / `Constant`），滑杆随模式显示——`Linear` 无参数、`Smooth` 显示 `Curvature`（0.25–4，默认 1）、`Constant` 显示 `Threshold`（0–1，默认 0.5）；两个参数各自保存，切换模式不丢值。
- 衰减公式（`t = clamp01(distance / Max Distance)`）：`Linear` = `1 - t`；`Smooth` = `1 - u²(3 - 2u)`，`u = pow(t, Curvature)`（`Curvature = 1` 即旧默认 smoothstep）；`Constant` = `t <= Threshold ? 1 : 0`。
- Compute Shader 的 `_Smoothness` 换成 `_SamplingMode`（int）与 `_SamplingParameter`（float）；`NeckMaskGpuBaker.Prepare` 与重映射缓存键接受 `NeckMaskSamplingMode` 与参数；模式名、曲率上下限、默认值与按模式收敛集中在新增的 `NeckMaskSampling.cs`，界面显示值与 GPU 计算值同源。
- 已验证：`refresh_unity` 后 Console 无 error；`NeckMaskGpuRegression.cs` 返回 `PASS: 三种衰减模式 / 参数收敛 / 重叠 UV / 扩大距离 / 缓存复用 / 零值优先覆盖 / Dilation 参考对比 / Alpha / 旧版移除`（重叠 UV 像素值：Linear 159、Smooth 曲率 2 为 179、曲率 0.5 为 134、曲率超上限 100 收敛为 225、Constant 阈值 0.5 为 128、阈值 0.8 为 255；切换模式复用最终 RT 但重算 Mask）；`NeckMaskLocalizationRegression.cs` 返回 `PASS: 文案表 77 条`（源码扫描新增 `NeckMaskSampling.cs`）；`NeckMaskMakerRegression.cs` 与 `NeckMaskMaterialSlotRegression.cs` 继续 PASS。
- 窗口截图确认真实渲染：`Smooth` 显示 `Curvature 1`、`Constant` 显示 `Threshold 0.5`、`Linear` 无参数行，其余 Card 与底部保存按钮位置不变；验证结束后已恢复窗口原状态（`Smooth` / `Curvature 1` / `Max Distance 0.026`）。
- 旧窗口序列化数据中的 `_smoothness` 被忽略且不回填，重开后为默认 `Smooth` + `Curvature 1`（旧默认外观）；`Constant` 是 8 位贴图上的硬边，未做额外抗锯齿。

## GPU 表面贴图管线（历史验证记录）

- `NeckMaskGpuRegression.cs`：合成内存 GPU 测试，验证稀疏三角形内的非线性表面求值、重叠 UV 逐贡献平均、扩大 Max Distance、距离/贡献 Buffer 与最终 RT 复用、零值高优先级覆盖、Dilation 和 Alpha（Alpha 恒等于 Mask）。八轮 GPU 外扩对比测试目录内的独立邻居平均参考（生产代码已移除 CPU Mask 管线），8 位输出允许最多 1 级舍入差。
- Unity 2022.3.22f1 / D3D11 验证全部通过；Compute Shader 消息为空，Preview Shader 支持，Console 无 error。原拓扑回归也通过。
- 当前真实 Body / Body_Base 各生成一张 1024 RT，覆盖像素分别为 829020 / 587955；合并输出覆盖 936112 像素，PNG 内存编码成功，前后材质引用不变。此记录是固定模型的正确性检查，不是性能基准。
- 按用户要求未做基线测速，连续拖动、动态姿态、Scene 视觉与实际文件保存由用户手测。使用说明见 `Docs/local/NeckMaskGpuPreview.md`。
