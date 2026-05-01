## 1. 结构清理与模式拆分

- [x] 1.1 清理 BoneKeyMirror.cs 及其相关旧资产引用，确保目录中只保留新的 KeyframeMirror 实现入口
- [x] 1.2 从现有 KeyframeMirror 中拆分 Mirror Keyframes 与 Key Symmetry 的执行入口，避免继续共用基于标量取反的旧路径
- [x] 1.3 为 Mirror Keyframes 增加活动根对象解析与显示逻辑，在无法解析参考根时阻止执行

## 2. Mirror Keyframes 姿态采样与空间反射内核

- [x] 2.1 实现 Animation Window 选区按 Transform + 时间点聚合的采样模型，确保部分通道选中时能提升为完整 Transform 样本
- [x] 2.2 实现基于活动根对象本地空间的镜像平面定义，明确 X/Y/Z 对应 YZ/XZ/XY 平面
- [x] 2.3 实现“采样 -> 参考空间反射 -> 解回局部姿态”的 Mirror Keyframes 内核，替换当前字符串判定和标量取反逻辑

## 3. 曲线表示与写回

- [x] 3.1 为位置、欧拉旋转、四元数旋转建立独立的读取与写回路径，避免混用同一属性规则
- [x] 3.2 在关键帧写回时保留原曲线表示以及可恢复的插值元数据，包括 tangent mode、broken/constant、weighted mode 和权重
- [x] 3.3 将 Key Symmetry 接入共享姿态镜像内核，并保持其写回目标为左右骨映射后的目标 Transform 集合

## 4. 验证与回归保护

- [ ] 4.1 验证 Mirror Keyframes 在 X/Y/Z 三种镜像轴下都按活动根对象本地平面执行，而不是按标量通道取反
- [ ] 4.2 验证部分通道选中、完整通道选中、欧拉曲线和四元数曲线都能得到一致且可预期的镜像结果
- [ ] 4.3 验证 Key Symmetry 与 Mirror Keyframes 的行为边界清晰，且目录内不再残留旧脚本导致的重复类或重复入口问题