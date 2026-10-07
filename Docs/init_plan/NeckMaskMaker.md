# NeckMaskMaker

## UIUX
> 形式：EditorScript + Scene 最前层叠加面板（贴图预览）
>
> 输出内容：{Avatar_Name}_{Datetime}_neckmask.png

### 界面内容
- GameObject(Body)
- GameObject(Body_base)

- Mask设置（max-dist,smooth,etc.）
- Btn(Select Neck Border) -> 打开Scene的交互式Body_base Mesh Edge 选择
- Bool(Toggle Preview)
- Bool(Create Mask for body_base only)
- Btn(Save Mask Texture)
- Bool(Toggle Texture Preview) + Btn(Refresh) -> 在 Scene 最前层叠加显示 Body / Body_base **各自**的 Mask 贴图，采用 Unity 原生编辑器样式，标明角色与对应 Mesh GameObject；仅 × 关闭可交互，兼容保留 EditorPrefs 尺寸；预览固定 Scene viewport 左下角（左 / 下各 12px），选线提示底部居中（下 12px），位置不受旧 EditorPrefs 坐标影响

### 用户操作流程
1. 用户选择Target avatar的body和bodybase
2. 用户点击Select Neck Border，通过Scene交互式选择MeshEdge，敲Enter决定
3. 点击Toggle Preview打开Mask的Preview，红色的Mask范围显示在Mesh上。
4. 点击Save弹出Sys的文件选择器，选择放置的位置。

## 具体执行业务
- 通过用户选择的Edge，按UV三角形内的重心位置重建表面点，在GPU计算距离与Mask，预览直接使用RenderTexture，导出时读回PNG；不保留旧版顶点Mask及CPU烘焙回退
- 需要实现loop selection，让用户只点一次即可获取整个body_base的颈部循环线
- Mesh Edge Select需要在Scene中绘制Preview
