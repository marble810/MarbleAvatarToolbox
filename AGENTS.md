- 环境限定在Unity 2022.3.22F1，该项目所有插件均聚焦于VRChat Avatars的编辑与调试。
- 允许并**应积极**访问`../`内的所有Packages，便于开发基于NDMF等
- 使用UnityMCP获取Unity内的相关信息，用于Test与Debug。
- 当用户要求设计新插件时，无论是否显式调用Openspec，都应检查当前Spec状态，并开新的Changes。
- 当调用`/opsx-explore`时，explore范围应扩展到`../`，分析整个Unity项目添加的Packages。
- 当代码生成结束，不得结束Session，应等待用户的下一步指令，除非用户明确要求结束Session。

- 除`com.vrchat.*`以外，允许依赖的第三方Packages：
  1. `nadena.dev.ndmf`
  2. `nadena.dev.modular-avatar`
  3. `com.vrcfury.vrcfury`
  > 如果出现需要依赖其他第三方Packages的情况，应提示用户并询问是否继续。 