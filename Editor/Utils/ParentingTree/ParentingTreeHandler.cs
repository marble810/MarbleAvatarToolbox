using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace marble810.AvatarTools.Utils
{
    internal static class ParentingTreeHandler
    {

        internal static ParentingTree GetParentingChain(GameObject parent)
        {
            if (parent == null)
            {
                return null;
            }

            ParentingTree chain = new ParentingTree
            {
                nodeObject = parent,
                children = new List<ParentingTree>()
            };

            int childCount = parent.transform.childCount;

            List<string> childNames = new List<string>();
            for (int i = 0; i < childCount; i++)
            {
                GameObject child = parent.transform.GetChild(i).gameObject;
                childNames.Add(child.name);
                ParentingTree childNode = GetParentingChain(child);
                chain.children.Add(childNode);
            }
            
            // Debug.Log($"Got Children:{string.Join(", ",childNames)}");

            return chain;
        }
    }

    [System.Serializable]
    public class ParentingTree
    {
        public GameObject nodeObject;
        public bool isSelected;
        public List<ParentingTree> children = new List<ParentingTree>();
    }

}