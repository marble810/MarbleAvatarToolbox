using UnityEngine;

namespace marble810.MarbleAvatarToolbox.PlayModeTools
{
    /// <summary>
    /// 仅添加到 QuickPlay 临时副本根对象的标记组件。
    /// 该组件必须位于「非 Editor-only 平台」程序集：Unity 2022.3 拒绝对 Editor-only 程序集中的
    /// MonoBehaviour 执行 AddComponent（会报 “it is an editor script”）。
    /// 通过 defineConstraints=UNITY_EDITOR 保证该程序集不会进入 Player 构建。
    /// 用于跨 Domain/Scene Reload 确认 clone 所有权，绝不用于按名字删除场景对象。
    /// 该组件不会被写入原 Avatar，也不会被保存到场景或项目资产。
    /// </summary>
    [AddComponentMenu("")]
    public sealed class QuickPlayCloneMarker : MonoBehaviour
    {
        [SerializeField] private string sessionId;
        [SerializeField] private GameObject originalAvatar;

        public string SessionId => sessionId;

        public GameObject OriginalAvatar => originalAvatar;

        public void Initialize(string id, GameObject original)
        {
            sessionId = id;
            originalAvatar = original;
        }
    }
}
