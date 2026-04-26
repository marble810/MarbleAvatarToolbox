#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace marble810.marbleavatartools.components
{
    [DisallowMultipleComponent]
    [ExecuteInEditMode]
    [AddComponentMenu("MarbleAvatarTools/MaSwitchBoardHook")]
    public class MaSwitchBoardHook : MonoBehaviour
    {
        public GameObject avatar;

        private void OnEnable() { }
    }
}
#endif