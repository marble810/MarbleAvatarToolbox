#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace marble810.marbleavatartoolbox.components
{
    [DisallowMultipleComponent]
    [ExecuteInEditMode]
    [AddComponentMenu("")]
    public class MaMenuSwitchBoardAvatarState : MonoBehaviour
    {
        public const string StorageObjectName = "__MA Menu SwitchBoard State";

        [HideInInspector]
        public List<GameObject> favoriteMenuItemObjects = new List<GameObject>();
    }
}
#endif