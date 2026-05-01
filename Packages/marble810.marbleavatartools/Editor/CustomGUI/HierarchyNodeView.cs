using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using marble810.MarbleAvatarTools.Utils;
using marble810.MarbleAvatarTools.CustomGUI.Helper;
using UnityEditor.UIElements;

namespace marble810.MarbleAvatarTools.CustomGUI
{
    /// <summary>
    /// 层级节点视图组件，用于在Unity Editor中展示和操作树形层级结构
    /// </summary>
    public class HierarchyNodeView : VisualElement
    {
        #region Constants

        private const float INDENT_WIDTH = 15f;
        private const float TOGGLE_WIDTH = 16f;
        private const float FOLDOUT_WIDTH = 12f;
        private const float ROW_MIN_HEIGHT = 16f;
        private const float HORIZONTAL_GAP = 1f;
        private const float MAX_HEIGHT = 300f;
        private const float MARGIN_DEFAULT = 3f;

        private const long REFRESH_INTERVAL_MS = 100;

        #endregion

        #region Fields

        private SerializedProperty _property;
        private readonly Func<SerializedProperty, VisualElement> _infoProvider;
        private readonly VisualElement _contentContainer;
        private readonly ScrollView _scrollView;
        private readonly bool _showToggle;
        private int _cachedChildCount;
        private int _trackingVersion = 0;

        private IVisualElementScheduledItem _scheduledRefresh;
        private readonly bool _enableScheduledRefresh;

        #endregion

        #region Constructor

        /// <summary>
        /// 构造一个新的层级节点视图
        /// </summary>
        /// <param name="property">绑定的SerializedProperty</param>
        /// <param name="infoProvider">信息提供者函数</param>
        /// <param name="showToggle">是否显示复选框</param>
        /// <param name="autoRefresh">是否自动追踪属性变化（TrackPropertyValue）</param>
        /// <param name="enableScheduledRefresh">是否启用定时刷新</param>
        public HierarchyNodeView(
            SerializedProperty property,
            Func<SerializedProperty, VisualElement> infoProvider = null,
            bool showToggle = true,
            bool autoRefresh = true,
            bool? enableScheduledRefresh = null
        )
        {
            _property = property;
            _infoProvider = infoProvider;
            _showToggle = showToggle;

            _enableScheduledRefresh = enableScheduledRefresh ?? (infoProvider != null);

            style.flexGrow = 0;
            style.flexShrink = 1;
            style.overflow = Overflow.Hidden;
            UniformSetter.SetMargin(this, MARGIN_DEFAULT);
            UniformSetter.SetBorderWidth(this, 1);
            UniformSetter.SetBorderColor(this, ColorPreset.Outline_Frame);
            UniformSetter.SetCornerRadius(this, 3f);
            style.backgroundColor = ColorPreset.BG_Frame;

            _contentContainer = new VisualElement();
            _contentContainer.style.flexGrow = 0;
            _contentContainer.style.flexShrink = 0;
            _contentContainer.style.overflow = Overflow.Hidden;
            UniformSetter.SetMargin(_contentContainer, MARGIN_DEFAULT);

            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.maxHeight = MAX_HEIGHT;
            _scrollView.contentContainer.style.flexGrow = 1;
            _scrollView.Add(_contentContainer);
            Add(_scrollView);

            BuildTree();

            if (autoRefresh)
            {
                SetupTracking();
            }

            if (_enableScheduledRefresh)
            {
                SetupScheduledRefresh();
            }

            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        #endregion

        #region Public Methods

        public void Refresh()
        {
            if (_property.serializedObject != null)
            {
                _property.serializedObject.Update();
            }
            BuildTree();
        }

        public void Rebind(SerializedProperty newProperty)
        {
            _property = newProperty;
            BuildTree();
            SetupTracking();
            
            if (_enableScheduledRefresh)
            {
                StopScheduledRefresh();
                SetupScheduledRefresh();
            }
        }

        public void SetAllExpanded(bool expanded)
        {
            if (_property == null) return;

            _property.serializedObject.Update();
            SetExpandedRecursive(_property, expanded);
            _property.serializedObject.ApplyModifiedProperties();

            BuildTree();
        }

        public void SetAllSelected(bool selected)
        {
            if (_property == null) return;

            _property.serializedObject.Update();
            SetSelectedRecursive(_property, selected);
            _property.serializedObject.ApplyModifiedProperties();
        }

        public void StopScheduledRefresh()
        {
            if (_scheduledRefresh != null)
            {
                _scheduledRefresh.Pause();
                _scheduledRefresh = null;
            }
        }

        public void StartScheduledRefresh()
        {
            if (_enableScheduledRefresh && _scheduledRefresh == null)
            {
                SetupScheduledRefresh();
            }
        }

        #endregion

        #region Scheduled Refresh

        private void SetupScheduledRefresh()
        {
            if (_property == null) return;

            _scheduledRefresh = schedule.Execute(() =>
            {
                if (_property?.serializedObject == null) return;

                if (_infoProvider != null)
                {
                    RefreshInfoProviders();
                }
            })
            .Every(REFRESH_INTERVAL_MS);
        }

        private void RefreshInfoProviders()
        {
            if (_contentContainer == null) return;

            var rows = _contentContainer.Query<VisualElement>("tree-row").ToList();
            
            foreach (var row in rows)
            {
                var infoContainer = row.Q<VisualElement>("info-container");
                if (infoContainer == null) continue;

                if (row.userData is SerializedProperty nodeProp)
                {
                    try
                    {
                        infoContainer.Clear();
                        var newInfo = _infoProvider.Invoke(nodeProp);
                        if (newInfo != null)
                        {
                            infoContainer.Add(newInfo);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"InfoProvider refresh failed: {e.Message}");
                    }
                }
            }
        }

        private void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            StopScheduledRefresh();
        }

        #endregion

        #region Tree Building

        private void BuildTree()
        {
            _contentContainer.Clear();

            if (_property == null)
            {
                AddPlaceHolder("No Property");
                return;
            }
            if (_property.serializedObject == null)
            {
                AddPlaceHolder("No SerializedObject");
                return;
            }

            _property.serializedObject.Update();

            var nodeObject = _property.FindPropertyRelative("nodeObject");
            if (nodeObject == null)
            {
                AddPlaceHolder("Invalid HierarchyNode Structure");
                return;
            }

            if (nodeObject.objectReferenceValue == null)
            {
                AddPlaceHolder("None");
                return;
            }

            BuildNode(_contentContainer, _property, 0);

            var children = _property.FindPropertyRelative("children");
            _cachedChildCount = children?.arraySize ?? 0;
        }

        private void BuildNode(VisualElement container, SerializedProperty nodeProp, int depth)
        {
            var nodeObject = nodeProp.FindPropertyRelative("nodeObject");
            var children = nodeProp.FindPropertyRelative("children");

            if (nodeObject == null || nodeObject.objectReferenceValue == null)
            {
                return;
            }

            bool hasChildren = children != null && children.arraySize > 0;

            var row = CreateRow(nodeProp, depth, hasChildren);
            container.Add(row);

            if (hasChildren)
            {
                var childrenContainer = new VisualElement();
                childrenContainer.name = "tree-children-container";

                bool isExpanded = nodeProp.isExpanded;
                childrenContainer.style.display = isExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                container.Add(childrenContainer);

                for (int i = 0; i < children.arraySize; i++)
                {
                    var childProp = children.GetArrayElementAtIndex(i);
                    BuildNode(childrenContainer, childProp, depth + 1);
                }
            }
        }

        private VisualElement CreateRow(SerializedProperty nodeProp, int depth, bool hasChildren)
        {
            var nodeObject = nodeProp.FindPropertyRelative("nodeObject");
            var isSelected = nodeProp.FindPropertyRelative("isSelected");

            var row = new VisualElement();
            row.name = "tree-row";
            row.userData = nodeProp;
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = ROW_MIN_HEIGHT;
            row.style.marginLeft = INDENT_WIDTH * depth;

            var foldoutBtn = new Button();
            foldoutBtn.name = "foldout-button";
            foldoutBtn.style.width = FOLDOUT_WIDTH;
            foldoutBtn.style.height = FOLDOUT_WIDTH;
            foldoutBtn.style.marginRight = HORIZONTAL_GAP;
            foldoutBtn.style.backgroundColor = Color.clear;
            UniformSetter.SetBorderWidth(foldoutBtn, 0);

            if (hasChildren)
            {
                foldoutBtn.text = nodeProp.isExpanded ? "▼" : "▶";
                foldoutBtn.style.unityFontStyleAndWeight = FontStyle.Normal;
                foldoutBtn.style.fontSize = FOLDOUT_WIDTH / 1.33f;

                string propertyPath = nodeProp.propertyPath;
                var serializedObj = nodeProp.serializedObject;

                foldoutBtn.clicked += () =>
                {
                    serializedObj.Update();
                    var currentProp = serializedObj.FindProperty(propertyPath);
                    if (currentProp == null) return;

                    currentProp.isExpanded = !currentProp.isExpanded;
                    serializedObj.ApplyModifiedProperties();

                    BuildTree();
                };
            }
            else
            {
                foldoutBtn.text = " ";
                foldoutBtn.SetEnabled(false);
                foldoutBtn.style.visibility = Visibility.Hidden;
            }

            row.Add(foldoutBtn);

            if (_showToggle && isSelected != null)
            {
                var toggle = new Toggle();
                toggle.name = "select-toggle";
                toggle.style.marginRight = HORIZONTAL_GAP;
                toggle.style.width = TOGGLE_WIDTH;

                toggle.BindProperty(isSelected);
                row.Add(toggle);
            }

            var nameLabel = new Label();
            nameLabel.name = "name-label";
            nameLabel.style.marginRight = HORIZONTAL_GAP;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;

            var go = nodeObject.objectReferenceValue as GameObject;
            nameLabel.text = go != null ? go.name : "(None)";

            if (go != null && !go.activeInHierarchy)
            {
                nameLabel.style.color = ColorPreset.Gray;
            }

            row.Add(nameLabel);

            if (_infoProvider != null)
            {
                var infoContainer = new VisualElement();
                infoContainer.name = "info-container";
                infoContainer.style.flexDirection = FlexDirection.Row;
                infoContainer.style.flexGrow = 1;
                infoContainer.style.flexShrink = 1;
                
                try
                {
                    var infoElement = _infoProvider.Invoke(nodeProp);
                    if (infoElement != null)
                    {
                        infoContainer.Add(infoElement);
                    }
                }
                catch (Exception e)
                {
                    var errorLabel = new Label("[Provider Error]");
                    Debug.LogWarning($"InfoProvider error: {e.Message}");
                    infoContainer.Add(errorLabel);
                }
                
                row.Add(infoContainer);
            }
            
            return row;
        }

        #endregion

        #region Property Tracking

        private void SetupTracking()
        {
            if (_property == null) return;

            _trackingVersion++;
            int capturedVersion = _trackingVersion;

            var nodeObject = _property.FindPropertyRelative("nodeObject");
            if (nodeObject != null)
            {
                this.TrackPropertyValue(nodeObject, prop =>
                {
                    if (_trackingVersion != capturedVersion) return;
                    BuildTree();
                });
            }

            var children = _property.FindPropertyRelative("children");
            if (children != null)
            {
                this.TrackPropertyValue(children, prop =>
                {
                    if (_trackingVersion != capturedVersion) return;

                    int currentCount = prop.arraySize;
                    if (currentCount != _cachedChildCount)
                    {
                        _cachedChildCount = currentCount;
                        BuildTree();
                    }
                });
            }
        }

        #endregion

        #region Utilities

        private void SetExpandedRecursive(SerializedProperty nodeProp, bool expanded)
        {
            nodeProp.isExpanded = expanded;

            var children = nodeProp.FindPropertyRelative("children");
            if (children != null)
            {
                for (int i = 0; i < children.arraySize; i++)
                {
                    SetExpandedRecursive(children.GetArrayElementAtIndex(i), expanded);
                }
            }
        }

        private void SetSelectedRecursive(SerializedProperty nodeProp, bool selected)
        {
            var isSelected = nodeProp.FindPropertyRelative("isSelected");
            if (isSelected != null)
            {
                isSelected.boolValue = selected;
            }

            var children = nodeProp.FindPropertyRelative("children");
            if (children != null)
            {
                for (int i = 0; i < children.arraySize; i++)
                {
                    SetSelectedRecursive(children.GetArrayElementAtIndex(i), selected);
                }
            }
        }

        private void AddPlaceHolder(string message)
        {
            var placeholder = new Label(message);
            placeholder.style.color = ColorPreset.Gray;
            placeholder.style.unityFontStyleAndWeight = FontStyle.Italic;
            placeholder.style.paddingLeft = 4;
            _contentContainer.Add(placeholder);
        }

        #endregion
    }
}