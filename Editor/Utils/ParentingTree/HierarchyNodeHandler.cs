using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace marble810.MarbleAvatarTools.Utils
{
    internal static class HierarchyNodeHandler
    {

        internal static HierarchyNode GetHierarchyNode(GameObject parent)
        {
            if (parent == null)
            {
                return null;
            }

            HierarchyNode node = new HierarchyNode
            {
                nodeObject = parent,
                children = new List<HierarchyNode>()
            };

            int childCount = parent.transform.childCount;

            List<string> childNames = new List<string>();
            for (int i = 0; i < childCount; i++)
            {
                GameObject child = parent.transform.GetChild(i).gameObject;
                childNames.Add(child.name);
                HierarchyNode childNode = GetHierarchyNode(child);
                node.children.Add(childNode);
            }
            
            // Debug.Log($"Got Children:{string.Join(", ",childNames)}");

            return node;
        }
    }

    [System.Serializable]
    public class HierarchyNode
    {
        public GameObject nodeObject;
        public bool isSelected;

        [SerializeReference]
        public List<HierarchyNode> children = new List<HierarchyNode>();
    }

}