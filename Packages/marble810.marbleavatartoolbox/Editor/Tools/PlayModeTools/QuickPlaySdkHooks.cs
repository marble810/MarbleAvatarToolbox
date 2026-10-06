using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace marble810.MarbleAvatarToolbox.PlayModeTools
{
    /// <summary>
    /// 目标会话的最早 SDK preprocess 回调。
    /// 仅作用于当前登记的 QuickPlay 副本：在任何 d4rk 回调之前确认阻断组件。
    /// 非目标对象一律 no-op。
    /// 回调顺序取 -20000：晚于 VRCFury 的 int.MinValue 系列，早于 NDMF（-11000）与 d4rk（-1025 或 -15）。
    /// </summary>
    internal sealed class QuickPlayEarlySdkHook : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -20000;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            // 目标上的抑制失败必须同步中止 SDK callback 链（返回 false）；非目标对象返回 true（no-op）。
            // SDK 的 OnPreprocessAvatar 在任一回调返回 false 时立即以 false 结束，因此后续回调不会继续执行。
            return QuickPlaySession.OnSdkPreprocessEntered(avatarGameObject);
        }
    }

    /// <summary>
    /// 目标会话的收尾 SDK preprocess 回调（位于 Modular Avatar 的 IEditorOnly 清理之前）。
    /// 只做目标副本的可播放性协作，不修改其它对象，也不作为构建结果信号。
    /// </summary>
    internal sealed class QuickPlayLateSdkHook : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue - 2;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            QuickPlaySession.OnSdkPreprocessCompleted(avatarGameObject);
            return true;
        }
    }
}
