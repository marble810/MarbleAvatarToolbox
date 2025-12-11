using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using marble810.AvatarTools.Utils;
using UnityEditor.Experimental.GraphView;

namespace marble810.AvatarTools.CustomGUI
{
    public class NodeInfoElement : VisualElement
    {
        private NodeInfoResult _currentInfo;

        private const float DEFAULT_FONT_SIZE = 9f;

        public NodeInfoElement()
        {
            name = "node-info-element";

            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.flexShrink = 0;
            style.marginLeft = 4;
        }

        public void SetInfo(NodeInfoResult info)
        {
            _currentInfo = info;
            Rebuild();
        }

        public void ClearInfo()
        {
            _currentInfo = null;
            Rebuild();
        }

        public void Rebuild()
        {
            Clear();
            if (_currentInfo == null || _currentInfo.IsEmpty)
            {
                style.display = DisplayStyle.None;
                return;
            }
            style.display = DisplayStyle.Flex;
            foreach (var segment in _currentInfo.Segments)
            {
                if (segment.IsSpace())
                {
                    AddSpace();
                }
                else
                {
                    AddTextSegment(segment);
                }
            }
        }

        private void AddTextSegment(TextSegment segment)
        {
            var label = new Label(segment.text);
            label.style.fontSize = DEFAULT_FONT_SIZE;

            label.style.color = segment.color;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;

            Add(label);
        }

        private void AddSpace()
        {
            var space = new VisualElement();
            space.style.width = 4;
            Add(space);
        }

    }
}
