using UnityEngine;

namespace marble810.MarbleAvatarToolbox.PlayModeTools
{
    /// <summary>
    /// QuickPlay 临时副本的可播放性协作：只对本会话登记的目标副本启用被禁用的 root Animator。
    /// 不修改原 Avatar 或共享控制器资产，也不推断 SDK/NDMF 处理结果。
    /// </summary>
    internal static class QuickPlayAvatarPostProcess
    {
        /// <summary>
        /// 启用被禁用的 root Animator，保证临时副本在 Play 中可播放（VRChat 运行时需要）。
        /// </summary>
        public static void EnsurePlayableRootAnimator(GameObject cloneAvatar)
        {
            if (cloneAvatar == null) return;

            var animator = cloneAvatar.GetComponent<Animator>();
            if (animator == null || animator.enabled) return;
            if (animator.avatar == null) return;
            if (animator.runtimeAnimatorController == null) return;

            animator.enabled = true;
            Debug.Log("[QuickPlay] 已启用临时副本的 root Animator。");
        }
    }
}
