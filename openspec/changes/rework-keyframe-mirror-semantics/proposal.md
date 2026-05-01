## Why

当前 KeyframeMirror 中的 Mirror Keyframes 实际上是按曲线和标量通道做条件取反，而不是按空间镜像语义对姿态进行反射。这导致用户在选择 X 作为镜像方向时，得到的结果并不是“以 YZ 平面为镜像平面”的对称姿态，尤其在旋转与部分通道选中场景下偏差明显。

## What Changes

- 重新定义 Mirror Keyframes 的语义：镜像操作必须表示对选中姿态样本的空间反射，而不是对单条关键帧曲线做数值取反。
- 明确镜像轴含义：当镜像方向为 X 时，镜像平面为 Avatar 参考空间中的 YZ 平面；Y 对应 XZ 平面；Z 对应 XY 平面。
- 明确选区语义：只要某个 Transform 在某一时间点的任一相关通道被选中，Mirror Keyframes 就必须以该时间点的完整 Transform 样本为单位执行，而不是仅修改被选中的单个标量通道。
- 将 Mirror Keyframes 与 Key Symmetry 从语义和内部数据流上拆开：前者是单侧姿态的自体空间镜像，后者是基于左右骨映射的跨骨对称复制。
- 重构关键帧处理路径，使位置、欧拉旋转、四元数旋转与插值信息在镜像过程中按各自正确的数学与编辑器语义处理。
- 将 BoneKeyMirror.cs 视为已废弃旧脚本，在本次重构中清理，避免与 KeyframeMirror 的新实现并存造成资产和入口混乱。

## Capabilities

### New Capabilities
- `keyframe-mirror`: 定义编辑器中 KeyframeMirror 的镜像语义、参考空间、选区聚合规则，以及 Mirror Keyframes 与 Key Symmetry 的行为边界。

### Modified Capabilities

## Impact

- 受影响代码集中在 [Editor/AnimatingHelper/KeyframeMirror.cs](Editor/AnimatingHelper/KeyframeMirror.cs) 及同目录下遗留的旧脚本资产清理。
- 需要重新梳理 Animation Window 选区读取、按时间点聚合 Transform 数据、以及写回 AnimationClip 的编辑器流程。
- 需要区分 Unity Transform 的位置、欧拉旋转与四元数旋转曲线，避免继续沿用当前统一的字符串判定与标量取反逻辑。
- 不引入新的第三方 Package，但会影响现有 Mirror Keyframes / Key Symmetry 的用户行为定义，因此需要明确 spec 和 design 后再进入实现。