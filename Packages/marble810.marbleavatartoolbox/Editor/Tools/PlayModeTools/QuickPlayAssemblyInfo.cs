using System.Runtime.CompilerServices;

// QuickPlay 的内部类型对预定义编辑器程序集可见，仅用于同包内的 Editor 测试（Tests/Editor/QuickPlay）。
// 生产代码不依赖该可见性；对外仍只暴露 QuickPlay 菜单入口与 QuickPlay Settings 窗口。
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
