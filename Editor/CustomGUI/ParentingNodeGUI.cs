using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

using marble810.AvatarTools.CustomGUI;
using marble810.AvatarTools.Utils;
using System;

namespace marble810.AvatarTools.CustomGUI
{

    [CustomPropertyDrawer(typeof(Utils.ParentingTree))]
    public class ParentingNodeGUI : PropertyDrawer
    {
        private const float INDENT_WIDTH = 15f;
        private const float TOGGLE_WIDTH = 18f;
        private const float ARROW_WIDTH = 20f;
        private const float PADDING = 4f;

        //Interfaces
        public static System.Func<SerializedProperty, NodeInfoResult> NodeInfoProvider { get; set; }

        private static event Action _onRequestRefresh;

        public static void TriggerRefresh()
        {
            // Debug.Log("[TriggerRefresh] has been called.");
            _onRequestRefresh?.Invoke();
        }

        public static int SubscriberCount => _onRequestRefresh?.GetInvocationList().Length ?? 0;

        //Main VisElement

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new Box();

            root.style.paddingTop = 4f;
            root.style.paddingBottom = 4f;
            root.style.paddingLeft = 4f;
            root.style.paddingRight = 4f;

            var contentContainer = new VisualElement();
            contentContainer.name = "tree-content";
            contentContainer.style.flexDirection = FlexDirection.Column;
            root.Add(contentContainer);
            NonePlaceholder(contentContainer);

            var nodeObjectProp = property.FindPropertyRelative("nodeObject");
            root.TrackPropertyValue(nodeObjectProp, _ =>
            {
                property.serializedObject.Update();
                RebuildTree(contentContainer, property);
            });

            // BuildNodeRecursive(root, property.Copy(), 0);
            return root;

        }

        private void RebuildTree(VisualElement container, SerializedProperty property)
        {
            container.Clear();

            if (property == null || property.serializedObject == null)
            {
                NonePlaceholder(container);
                return;
            }

            property.serializedObject.Update();
            BuildNodeRecursive(container, property.Copy(), 0);
        }

        private void BuildNodeRecursive(VisualElement container, SerializedProperty property, int depth)
        {
            SerializedProperty nodeObject = property.FindPropertyRelative("nodeObject");
            SerializedProperty isSelected = property.FindPropertyRelative("isSelected");
            SerializedProperty children = property.FindPropertyRelative("children");

            if (nodeObject == null || (nodeObject.objectReferenceValue == null && depth == 0))
            {
                NonePlaceholder(container);
                return;
            }

            bool hasChildren = children != null && children.arraySize > 0;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = depth * INDENT_WIDTH;
            row.style.height = EditorGUIUtility.singleLineHeight;
            container.Add(row);

            //foldout arrow
            // === Foldout 按钮 ===
            var foldoutBtn = new Button();
            foldoutBtn.style.backgroundColor = Color.clear;
            foldoutBtn.style.borderTopWidth = 0;
            foldoutBtn.style.borderBottomWidth = 0;
            foldoutBtn.style.borderLeftWidth = 0;
            foldoutBtn.style.borderRightWidth = 0;
            foldoutBtn.style.width = ARROW_WIDTH;
            foldoutBtn.style.height = EditorGUIUtility.singleLineHeight;
            foldoutBtn.style.fontSize = 10f;
            foldoutBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            foldoutBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            foldoutBtn.style.marginRight = 2;

            if (hasChildren)
            {
                foldoutBtn.text = property.isExpanded ? "▼" : "►";
                foldoutBtn.style.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            }
            else
            {
                foldoutBtn.text = "";
                foldoutBtn.style.visibility = Visibility.Hidden;
            }
            row.Add(foldoutBtn);

            var toggle = new Toggle();
            toggle.style.width = TOGGLE_WIDTH;
            toggle.style.marginRight = 4;
            if (isSelected != null)
            {
                toggle.BindProperty(isSelected);
            }
            row.Add(toggle);



            var nameLabel = new Label();
            nameLabel.style.flexGrow = 1;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.textOverflow = TextOverflow.Ellipsis;
            UpdateLabelText(nameLabel, nodeObject);

            row.TrackPropertyValue(nodeObject, prop => UpdateLabelText(nameLabel, prop));
            row.Add(nameLabel);

            var infoElement = new NodeInfoElement();
            row.Add(infoElement);

            UpdateNodeInfo(infoElement, property);

            //被RegisterRefreshCallback替代
            // row.TrackPropertyValue(property, prop => UpdateNodeInfo(infoElement, prop));
            RegisterRefreshCallback(row, infoElement, property);

            var childrenContainer = new VisualElement();
            childrenContainer.name = "children-container";
            childrenContainer.style.display = property.isExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            container.Add(childrenContainer);

            string propertyPath = property.propertyPath;
            SerializedObject serializedObj = property.serializedObject;
            foldoutBtn.clicked += () =>
            {
                serializedObj.Update();
                SerializedProperty currentProp = serializedObj.FindProperty(propertyPath);
                if (currentProp == null) return;
                SerializedProperty currentChildren = currentProp.FindPropertyRelative("children");
                if (currentChildren != null && currentChildren.arraySize > 0)
                {
                    currentProp.isExpanded = !currentProp.isExpanded;
                    serializedObj.ApplyModifiedProperties();

                    foldoutBtn.text = currentProp.isExpanded ? "▼" : "►";
                    childrenContainer.style.display = currentProp.isExpanded ?
                        DisplayStyle.Flex : DisplayStyle.None;
                }
            };

            if (hasChildren)
            {
                for (int i = 0; i < children.arraySize; i++)
                {
                    SerializedProperty childProp = children.GetArrayElementAtIndex(i);
                    BuildNodeRecursive(childrenContainer, childProp, depth + 1);
                }
            }

        }

        private void NonePlaceholder(VisualElement container)
        {
            var placeholder = new Label();
            placeholder.text = "None";
            placeholder.style.color = new Color(0.5f, 0.5f, 0.5f);
            placeholder.style.unityFontStyleAndWeight = FontStyle.Italic;
            container.Add(placeholder);
        }

        private void UpdateLabelText(Label label, SerializedProperty nodeObjectProp)
        {
            if (nodeObjectProp.objectReferenceValue != null)
            {
                label.text = nodeObjectProp.objectReferenceValue.name;
                label.style.color = StyleKeyword.Null;
            }
            else
            {
                label.text = "None";
                label.style.color = Color.red;
            }
        }

        private void UpdateNodeInfo(NodeInfoElement infoElement, SerializedProperty nodeObjectProp)
        {
            var info = NodeInfoProvider?.Invoke(nodeObjectProp);

            if (info != null && !info.IsEmpty)
            {
                infoElement.SetInfo(info);
            }
            else
            {
                infoElement.ClearInfo();
            }
        }

        private void RegisterRefreshCallback(VisualElement row, NodeInfoElement infoElement, SerializedProperty property)
        {
            string propertyPath = property.propertyPath;
            SerializedObject serializedObj = property.serializedObject;

            Action refreshHandler = () =>
            {

                // Debug.Log($"[RefreshHandler] 收到刷新请求: {propertyPath}");

                if (serializedObj == null || serializedObj.targetObject == null) return;

                serializedObj.Update();
                SerializedProperty currentProp = serializedObj.FindProperty(propertyPath);

                if (currentProp != null)
                {
                    UpdateNodeInfo(infoElement, currentProp);
                }
            };

            _onRequestRefresh += refreshHandler;

            row.RegisterCallback<DetachFromPanelEvent>(e =>
            {
                _onRequestRefresh -= refreshHandler;
            });

        }
    }
}
