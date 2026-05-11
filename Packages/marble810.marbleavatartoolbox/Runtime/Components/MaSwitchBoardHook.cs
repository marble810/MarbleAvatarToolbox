#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace marble810.marbleavatartoolbox.components
{
    [DisallowMultipleComponent]
    [ExecuteInEditMode]
    [AddComponentMenu("MarbleAvatarToolbox/MaSwitchBoardHook")]
    public class MaSwitchBoardHook : MonoBehaviour
    {
        public GameObject avatar;

        private void OnEnable() { }
    }
}
#endif