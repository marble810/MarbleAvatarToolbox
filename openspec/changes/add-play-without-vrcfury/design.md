## Context

当前项目面向 Unity 2022.3.22f1 的 VRChat Avatar 编辑流程，工作区中同时存在 nadena.dev.ndmf、Avatar Optimizer 与 com.vrcfury.vrcfury。分析结果表明：

- VRCFury 的主预处理入口运行在 VRCSDK 预处理回调中，核心 builder 会在 Avatar 上检测 VRCFury 组件是否存在。
- NDMF 的早期预处理虽早于 VRCFury 主 builder，但仍处于同一条 Play / Build 预处理链中。
- 当前项目启用了 DisableDomainReload 与 DisableSceneReload，因此“直接在当前场景 Avatar 上删组件再进 Play”存在残留状态风险。
- Mafuchiffon_FT_V1 的 VRCFury 配置集中在根对象上，当前主要类型为 VF.Model.VRCFury，没有发现额外的 Haptic 或 Debug 类型分布。

因此，本变更应以“隔离副本”而不是“直接改当前对象”为前提。

## Goals / Non-Goals

**Goals:**
- 提供一个专用的编辑器入口，让用户能对指定 Avatar 发起“无 VRCFury”的 Play 会话。
- 确保原始 Avatar、场景文件和磁盘资源在整个流程中不被持久改写。
- 在退出 Play 后自动恢复编辑场景状态，不依赖用户手动清理。
- 用尽量少的耦合方式识别并排除 VRCFury 组件，避免直接依赖其 internal API。

**Non-Goals:**
- 不修改 Unity 主播放按钮的原生行为或覆盖 Unity 顶部 Toolbar 的默认实现。
- 不尝试重排 VRCFury 与 NDMF 的全局预处理顺序。
- 不在该变更中扩展为“任意第三方组件剥离框架”或通用预处理沙箱。
- 不改变正常 Play、正常 Upload、正常 Build 的既有默认流程。

## Decisions

### 1. 使用临时 clone，而不是直接操作原 Avatar
- 决策：进入 Play 前在 EditMode 复制目标 Avatar，仅对 clone 做 VRCFury 组件剥离。
- 原因：当前项目启用了不重载 Domain / Scene 的 Play 模式优化，直接修改原对象后很难保证退出 Play 时完全恢复。
- 备选方案：
  - 直接在当前 Avatar 上删除组件，再在 EditMode 恢复。放弃原因：恢复逻辑脆弱，且容易与其他预处理残留状态叠加。
  - 在 VRCSDK 或 NDMF 回调中运行时删除组件。放弃原因：仍然发生在实际预处理链中，调试和回滚边界不清晰。

### 2. 用程序集名或类型名反射识别 VRCFury 组件
- 决策：通过组件类型所在程序集是否为 VRCFury，或通过全名匹配 VF.* / VRCFury 相关类型，识别要从 clone 上移除的组件。
- 原因：VRCFuryComponent 及其相关类型多数为 internal，本包不应为了这个功能去改装配访问边界。
- 备选方案：
  - 直接引用 VRCFury internal 类型。放弃原因：破坏包边界，未来升级时风险大。
  - 仅按组件名字符串做白名单。放弃原因：覆盖面不足，不利于兼容后续新增类型。

### 3. 入口采用共享子菜单，并拆分为 Play 与 Hierarchy Copy 两个动作
- 决策：通过同一个子菜单暴露两个入口，一个用于进入无 VRCFury 的特殊 Play，会话结束后自动清理；另一个用于在 EditMode 下复制一个无 VRCFury 的 Avatar 到 Hierarchy 中，并禁用原 Avatar 以供手动调试。
- 原因：当前包现有工具均采用 MenuItem / EditorWindow 形式，实现成本低，兼容性更高，同时两个动作共享 clone 剥离逻辑但生命周期不同，拆分入口更清晰。
- 备选方案：
  - 扩展 Unity 顶部 Toolbar。放弃原因：与 Unity 版本和编辑器内部 API 耦合更强，维护成本高。

### 4. 用会话状态对象显式跟踪原 Avatar 与临时 clone
- 决策：维护一个编辑器静态状态，记录原 Avatar、临时 clone、原始 active 状态、会话是否已启动。
- 原因：可以明确处理重复点击、Play 失败、退出 Play 清理等情况。
- 备选方案：
  - 只靠对象命名约定查找 clone。放弃原因：冲突与误删风险高。

### 5. 退出 Play 时统一执行清理与恢复
- 决策：监听 playModeStateChanged，在 EnteredEditMode 时删除临时 clone、恢复原 Avatar，并重置会话状态。
- 原因：与当前项目的 Play 生命周期最契合，也能在 Play 期间保持隔离边界。
- 备选方案：
  - 在 ExitingPlayMode 就清理。放弃原因：对象仍处于运行态，容易与其他销毁流程冲突。

## Risks / Trade-offs

- [风险] clone 上仍可能残留某些非组件级的 VRCFury 预处理产物无法覆盖 → [缓解] 首版聚焦“组件层剥离 + 隔离测试”，并在日志中明确这是无 VRCFury 组件会话，而非全量还原所有 VRCFury 历史结果。
- [风险] 用户在会话期间手动删除 clone 或原 Avatar → [缓解] 所有恢复逻辑都做空引用检查，并在失败时清空内部状态避免后续连锁错误。
- [风险] 目标 Avatar 不唯一或未选中 → [缓解] 入口显式要求选中目标 Avatar，若无法解析则拒绝启动并给出错误提示。
- [风险] 只按程序集匹配可能遗漏未来跨程序集的 VRCFury 类型 → [缓解] 同时保留类型全名规则，并将识别逻辑集中封装，后续可扩展。
- [风险] 进入 Play 前禁用原 Avatar 可能影响用户当前调试上下文 → [缓解] 仅在特殊入口中执行，并在退出 Play 时恢复原始 active 状态。

## Migration Plan

- 该功能仅新增编辑器工具，无需迁移已有场景、资源或运行时数据。
- 若实现后发现兼容性问题，可直接移除入口与会话控制器，不影响已有 Avatar 资产。

## Open Questions

- 首版入口放在顶层 MarbleAvatarTools 菜单，还是放在单独的小窗口中更适合当前使用习惯。
- 是否需要在 clone 上保留一个可见标记组件或命名后缀，帮助用户在 Hierarchy 中辨认当前测试对象。
- 是否要在首版就支持“默认锁定当前场景唯一 Avatar”，还是强制要求用户先选择目标对象。