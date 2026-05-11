// #if MA_VRCSDK3_AVATARS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using nadena.dev.modular_avatar.core;
using nadena.dev.modular_avatar.core.menu;
using nadena.dev.modular_avatar.core.editor;
using System.Collections.Immutable;
using UnityEditor.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;
using marble810.marbleavatartoolbox.components;

namespace marble810.MarbleAvatarToolbox.MaMenuSwitchBoard
{
    public class MaMenuSwitchBoard : EditorWindow
    {
        private ObjectField avatarField;
        private Label sceneAvatarListLabel;
        private Button showSceneAvatarListButton;
        private ListView sceneAvatarListView;
        private ListView menuItemsListView;
        private Toggle showEditorOnlyReactiveToggle;
        private List<GameObject> sceneAvatars = new List<GameObject>();
        private List<MenuItemNode> allMenuItems = new List<MenuItemNode>();
        private List<MenuItemNode> visibleMenuItems = new List<MenuItemNode>();
        private readonly HashSet<GameObject> favoriteMenuItemObjects = new HashSet<GameObject>();
        private MaMenuSwitchBoardAvatarState currentAvatarFavoriteStore;
        private Button clearAllButton;
        private Button focusAvatarButton;
        private Button refreshButton;
        private bool refreshPending;
        private object lastMenuItemOverridesValue;
        private object lastPropertyOverridesValue;
        private Delegate menuItemOverridesOnChangeHandler;
        private Delegate propertyOverridesOnChangeHandler;
        private bool sceneAvatarListManuallyOpened;
        private bool suppressSceneAvatarRefresh;

        private const string USS_PATH = "Packages/marble810.marbleavatartoolbox/Editor/Tools/MaMenuSwitchBoard/MaMenuSwitchBoard.uss";
        private const string EDITOR_ONLY_TAG = "EditorOnly";
        private static StyleSheet ussAsset;

        [MenuItem("MarbleAvatarToolbox/MaMenuSwitchBoard")]
        public static void ShowWindow()
        {
            var window = GetWindow<MaMenuSwitchBoard>();
            window.titleContent = new GUIContent("MA Menu SwitchBoard");
            window.minSize = new Vector2(400, 300);
        }

        private void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.update += OnEditorUpdate;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChangedInEditMode;
            SubscribeToOverrideChanges();
            CaptureOverrideValueReferences();

            if (ussAsset == null)
            {
                ussAsset = AssetDatabase.LoadAssetAtPath<StyleSheet>(USS_PATH);
            }
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.update -= OnEditorUpdate;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChangedInEditMode;
            UnsubscribeFromOverrideChanges();
        }

        private void OnHierarchyChanged()
        {
            if (suppressSceneAvatarRefresh) return;
            RefreshSceneAvatarList();
        }

        public void CreateGUI()
        {
            rootVisualElement.AddToClassList("ma-menu-switchboard");

            if (ussAsset != null)
            {
                rootVisualElement.styleSheets.Add(ussAsset);
            }

            // Create toolbar using VisualElement instead of Toolbar (which is internal)
            var toolbar = new VisualElement();
            toolbar.AddToClassList("top-panel");
            toolbar.style.flexDirection = FlexDirection.Column;
            toolbar.style.paddingLeft = 5;
            toolbar.style.paddingRight = 5;
            toolbar.style.paddingTop = 5;
            toolbar.style.paddingBottom = 5;
            // toolbar.style.gap = 5;

            avatarField = new ObjectField("Avatar");
            avatarField.AddToClassList("avatar-field");
            avatarField.objectType = typeof(GameObject);
            avatarField.RegisterValueChangedCallback(OnAvatarChanged);
            toolbar.Add(avatarField);

            sceneAvatarListLabel = new Label("场景Avatars列表");
            sceneAvatarListLabel.AddToClassList("scene-avatar-list-label");
            toolbar.Add(sceneAvatarListLabel);

            sceneAvatarListView = new ListView();
            sceneAvatarListView.makeItem = MakeSceneAvatarRow;
            sceneAvatarListView.bindItem = BindSceneAvatarRow;
            sceneAvatarListView.itemsSource = sceneAvatars;
            sceneAvatarListView.selectionType = SelectionType.Single;
            sceneAvatarListView.fixedItemHeight = 22;
            sceneAvatarListView.style.height = 104;
            sceneAvatarListView.style.borderTopWidth = 1;
            sceneAvatarListView.style.borderRightWidth = 1;
            sceneAvatarListView.style.borderBottomWidth = 1;
            sceneAvatarListView.style.borderLeftWidth = 1;
            sceneAvatarListView.style.borderTopColor = new Color(0.18f, 0.18f, 0.18f);
            sceneAvatarListView.style.borderRightColor = new Color(0.18f, 0.18f, 0.18f);
            sceneAvatarListView.style.borderBottomColor = new Color(0.18f, 0.18f, 0.18f);
            sceneAvatarListView.style.borderLeftColor = new Color(0.18f, 0.18f, 0.18f);
            sceneAvatarListView.style.paddingTop = 2;
            sceneAvatarListView.style.paddingBottom = 2;
            sceneAvatarListView.selectionChanged += OnSceneAvatarSelectionChanged;
            toolbar.Add(sceneAvatarListView);

            showSceneAvatarListButton = new Button(OnShowSceneAvatarListClicked);
            showSceneAvatarListButton.AddToClassList("show-scene-avatar-list-button");
            showSceneAvatarListButton.text = "打开Avatar列表";
            toolbar.Add(showSceneAvatarListButton);

            var buttonBar = new VisualElement();
            buttonBar.AddToClassList("button-bar");
            buttonBar.style.flexDirection = FlexDirection.Row;
            toolbar.Add(buttonBar);

            refreshButton = new Button(OnRefreshClicked);
            refreshButton.text = "Refresh";
            buttonBar.Add(refreshButton);

            focusAvatarButton = new Button(OnFocusAvatarClicked);
            focusAvatarButton.text = "定位到Avatar GameObject";
            buttonBar.Add(focusAvatarButton);

            clearAllButton = new Button(OnClearAllOverrides);
            clearAllButton.text = "Clear All Overrides";
            buttonBar.Add(clearAllButton);

            showEditorOnlyReactiveToggle = new Toggle("显示 EditorOnly 响应式内容");
            showEditorOnlyReactiveToggle.AddToClassList("show-editor-only-toggle");
            showEditorOnlyReactiveToggle.value = false;
            showEditorOnlyReactiveToggle.RegisterValueChangedCallback(_ =>
            {
                UpdateVisibleMenuItems();
                menuItemsListView?.Rebuild();
            });
            toolbar.Add(showEditorOnlyReactiveToggle);

            rootVisualElement.Add(toolbar);

            RefreshSceneAvatarList();

            menuItemsListView = new ListView();
            menuItemsListView.AddToClassList("menu-items-list");
            menuItemsListView.makeItem = MakeMenuItemRow;
            menuItemsListView.bindItem = BindMenuItemRow;
            menuItemsListView.itemsSource = visibleMenuItems;
            menuItemsListView.selectionType = SelectionType.None;
            menuItemsListView.fixedItemHeight = 22;
            menuItemsListView.style.flexGrow = 1;

            rootVisualElement.Add(menuItemsListView);

            if (Selection.activeGameObject != null)
            {
                var avatar = FindAvatarInSelection(Selection.activeGameObject);
                if (avatar != null)
                {
                    avatarField.value = avatar;
                    LoadMenuItems(avatar);
                }
            }
        }

        private void OnSelectionChanged()
        {
            if (Selection.activeGameObject != null)
            {
                var avatar = FindAvatarInSelection(Selection.activeGameObject);
                if (avatar != null && avatarField.value != avatar)
                {
                    avatarField.value = avatar;
                }
            }
        }

        private void OnAvatarChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            var avatar = evt.newValue as GameObject;
            if (avatar != null)
            {
                sceneAvatarListManuallyOpened = false;
                LoadMenuItems(avatar);
                UpdateSceneAvatarSelection(avatar);
            }
            else
            {
                sceneAvatarListManuallyOpened = false;
                ClearMenuItems();
                UpdateSceneAvatarSelection(null);
            }

            UpdateFocusAvatarButtonState();
            UpdateSceneAvatarListVisibility();
        }

        private void OnRefreshClicked()
        {
            RefreshSceneAvatarList();

            var avatar = avatarField.value as GameObject;
            if (avatar != null)
            {
                LoadMenuItems(avatar);
            }
        }

        private void OnFocusAvatarClicked()
        {
            var avatar = avatarField?.value as GameObject;
            if (avatar == null) return;

            Selection.activeGameObject = avatar;
            EditorGUIUtility.PingObject(avatar);
        }

        private VisualElement MakeSceneAvatarRow()
        {
            var label = new Label();
            label.AddToClassList("scene-avatar-row");
            label.style.height = 20;
            label.style.marginBottom = 2;
            label.style.paddingLeft = 3;
            label.style.paddingRight = 3;
            label.style.paddingTop = 0;
            label.style.paddingBottom = 0;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            return label;
        }

        private void BindSceneAvatarRow(VisualElement element, int index)
        {
            var label = element as Label;
            if (label == null) return;

            if (index < 0 || index >= sceneAvatars.Count)
            {
                label.text = string.Empty;
                label.userData = null;
                return;
            }

            var avatar = sceneAvatars[index];
            label.text = GetHierarchyPath(avatar);
            label.userData = avatar;
        }

        private void OnSceneAvatarSelectionChanged(IEnumerable<object> selectedItems)
        {
            var avatar = selectedItems.OfType<GameObject>().FirstOrDefault();
            if (avatar == null) return;

            sceneAvatarListManuallyOpened = false;
            avatarField.value = avatar;
        }

        private void OnShowSceneAvatarListClicked()
        {
            sceneAvatarListManuallyOpened = true;
            RefreshSceneAvatarList();
            UpdateSceneAvatarListVisibility();
        }

        private void OnActiveSceneChangedInEditMode(UnityEngine.SceneManagement.Scene oldScene,
            UnityEngine.SceneManagement.Scene newScene)
        {
            sceneAvatarListManuallyOpened = false;
            if (avatarField != null)
            {
                avatarField.value = null;
            }
            else
            {
                ClearMenuItems();
            }

            RefreshSceneAvatarList();
            UpdateSceneAvatarListVisibility();
        }

        private void RefreshSceneAvatarList()
        {
            sceneAvatars.Clear();

            var descriptors = UnityEngine.Object.FindObjectsOfType<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true);
            sceneAvatars.AddRange(descriptors
                .Where(descriptor => descriptor != null)
                .Select(descriptor => descriptor.gameObject)
                .Distinct()
                .OrderBy(gameObject => gameObject.scene.path)
                .ThenBy(GetHierarchyPath));

            if (sceneAvatarListView == null) return;

            sceneAvatarListView.itemsSource = sceneAvatars;
            sceneAvatarListView.Rebuild();
            UpdateSceneAvatarSelection(avatarField?.value as GameObject);
            UpdateFocusAvatarButtonState();
            UpdateSceneAvatarListVisibility();
        }

        private void UpdateSceneAvatarListVisibility()
        {
            if (sceneAvatarListView == null || sceneAvatarListLabel == null || showSceneAvatarListButton == null) return;

            var hasAvatar = avatarField?.value != null;
            var showList = !hasAvatar || sceneAvatarListManuallyOpened;

            sceneAvatarListLabel.style.display = showList ? DisplayStyle.Flex : DisplayStyle.None;
            sceneAvatarListView.style.display = showList ? DisplayStyle.Flex : DisplayStyle.None;
            showSceneAvatarListButton.style.display = hasAvatar && !showList ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateSceneAvatarSelection(GameObject currentAvatar)
        {
            if (sceneAvatarListView == null) return;

            if (currentAvatar != null)
            {
                var index = sceneAvatars.IndexOf(currentAvatar);
                if (index >= 0)
                {
                    sceneAvatarListView.SetSelectionWithoutNotify(new[] { index });
                }
                else
                {
                    sceneAvatarListView.SetSelectionWithoutNotify(new int[0]);
                }
            }
            else
            {
                sceneAvatarListView.SetSelectionWithoutNotify(new int[0]);
            }
        }

        private void UpdateFocusAvatarButtonState()
        {
            focusAvatarButton?.SetEnabled(avatarField?.value != null);
        }

        private static string GetHierarchyPath(GameObject gameObject)
        {
            if (gameObject == null) return string.Empty;

            var names = new Stack<string>();
            var current = gameObject.transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }

        private void LoadMenuItems(GameObject avatar)
        {
            LoadFavoriteMenuItemObjects(avatar);
            allMenuItems = MenuItemDiscoveryHelper.DiscoverMenuItems(avatar, favoriteMenuItemObjects);
            UpdateVisibleMenuItems();
            menuItemsListView?.Rebuild();
            RequestRefresh();
        }

        private void ClearMenuItems()
        {
            allMenuItems.Clear();
            visibleMenuItems.Clear();
            favoriteMenuItemObjects.Clear();
            currentAvatarFavoriteStore = null;
            menuItemsListView?.Rebuild();
            RequestRefresh();
        }

        private void UpdateVisibleMenuItems()
        {
            visibleMenuItems.Clear();

            var favoriteNodes = new List<MenuItemNode>();
            var regularNodes = new List<MenuItemNode>();
            foreach (var node in allMenuItems)
            {
                CollectVisibleNodes(node, favoriteNodes, regularNodes, 0, false);
            }

            visibleMenuItems.AddRange(favoriteNodes);
            if (favoriteNodes.Count > 0 && regularNodes.Count > 0)
            {
                visibleMenuItems.Add(MenuItemNode.CreateDivider());
            }

            visibleMenuItems.AddRange(regularNodes);
        }

        private void CollectVisibleNodes(
            MenuItemNode node,
            List<MenuItemNode> favoriteNodes,
            List<MenuItemNode> regularNodes,
            int depth,
            bool ancestorHiddenByEditorOnly)
        {
            if (node == null) return;

            var hiddenByEditorOnly = ancestorHiddenByEditorOnly || IsEditorOnlyNodeHidden(node);
            if (hiddenByEditorOnly) return;

            node.depth = depth;
            if (node.isFavorite)
            {
                favoriteNodes.Add(node);
            }
            else if (node.isVisible)
            {
                regularNodes.Add(node);
            }

            foreach (var child in node.children)
            {
                CollectVisibleNodes(child, favoriteNodes, regularNodes, depth + 1, hiddenByEditorOnly);
            }
        }

        private bool IsEditorOnlyNodeHidden(MenuItemNode node)
        {
            if (node == null) return false;
            if (showEditorOnlyReactiveToggle?.value == true) return false;
            return node.isReactiveContent && node.isEditorOnlyReactiveContent;
        }

        private GameObject FindAvatarInSelection(GameObject selected)
        {
            var avatar = selected;
            while (avatar != null)
            {
                if (avatar.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>() != null)
                {
                    return avatar;
                }
                avatar = avatar.transform.parent?.gameObject;
            }
            return null;
        }

        private void OnClearAllOverrides()
        {
            SimulatorAPIHelper.ClearAllOverrides();
            RequestRefresh();
        }

        private void OnEditorUpdate()
        {
            if (SimulatorAPIHelper.OverrideValueReferencesChanged(ref lastMenuItemOverridesValue, ref lastPropertyOverridesValue))
            {
                RequestRefresh();
            }

            if (!refreshPending) return;

            refreshPending = false;
            RefreshOverrideStates();
        }

        private void RequestRefresh()
        {
            refreshPending = true;
        }

        private void RefreshOverrideStates()
        {
            if (menuItemsListView == null) return;

            menuItemsListView.Query<StateOverrideController>().ForEach(UpdateOverrideControllerState);
        }

        private void CaptureOverrideValueReferences()
        {
            lastMenuItemOverridesValue = SimulatorAPIHelper.GetMenuItemOverridesValueReference();
            lastPropertyOverridesValue = SimulatorAPIHelper.GetPropertyOverridesValueReference();
        }

        private void SubscribeToOverrideChanges()
        {
            if (menuItemOverridesOnChangeHandler == null)
            {
                menuItemOverridesOnChangeHandler = CreateOverrideChangeHandler(
                    true,
                    nameof(OnMenuItemOverridesChanged));
                if (menuItemOverridesOnChangeHandler != null &&
                    !SimulatorAPIHelper.TryAddMenuItemOverridesOnChangeHandler(menuItemOverridesOnChangeHandler))
                {
                    menuItemOverridesOnChangeHandler = null;
                }
            }

            if (propertyOverridesOnChangeHandler == null)
            {
                propertyOverridesOnChangeHandler = CreateOverrideChangeHandler(
                    false,
                    nameof(OnPropertyOverridesChanged));
                if (propertyOverridesOnChangeHandler != null &&
                    !SimulatorAPIHelper.TryAddPropertyOverridesOnChangeHandler(propertyOverridesOnChangeHandler))
                {
                    propertyOverridesOnChangeHandler = null;
                }
            }
        }

        private Delegate CreateOverrideChangeHandler(bool menuItemOverrides, string methodName)
        {
            var handlerType = menuItemOverrides
                ? SimulatorAPIHelper.GetMenuItemOverridesOnChangeHandlerType()
                : SimulatorAPIHelper.GetPropertyOverridesOnChangeHandlerType();
            if (handlerType == null) return null;

            var method = GetType().GetMethod(methodName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (method == null) return null;

            try
            {
                return Delegate.CreateDelegate(handlerType, this, method);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"MaMenuSwitchBoard: Failed to create override change handler: {e.Message}");
                return null;
            }
        }

        private void UnsubscribeFromOverrideChanges()
        {
            if (menuItemOverridesOnChangeHandler != null)
            {
                SimulatorAPIHelper.TryRemoveMenuItemOverridesOnChangeHandler(menuItemOverridesOnChangeHandler);
                menuItemOverridesOnChangeHandler = null;
            }

            if (propertyOverridesOnChangeHandler != null)
            {
                SimulatorAPIHelper.TryRemovePropertyOverridesOnChangeHandler(propertyOverridesOnChangeHandler);
                propertyOverridesOnChangeHandler = null;
            }
        }

        private void OnMenuItemOverridesChanged(ImmutableDictionary<string, ModularAvatarMenuItem> _)
        {
            menuItemOverridesOnChangeHandler = null;
            CaptureOverrideValueReferences();
            RequestRefresh();
            SubscribeToOverrideChanges();
        }

        private void OnPropertyOverridesChanged(ImmutableDictionary<string, float> _)
        {
            propertyOverridesOnChangeHandler = null;
            CaptureOverrideValueReferences();
            RequestRefresh();
            SubscribeToOverrideChanges();
        }

        private VisualElement MakeMenuItemRow()
        {
            var container = new VisualElement();
            container.AddToClassList("menu-item-row");
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.minHeight = 22;
            container.style.paddingLeft = 2;
            container.style.paddingRight = 4;
            container.style.paddingTop = 0;
            container.style.paddingBottom = 0;

            var indent = new VisualElement();
            indent.AddToClassList("menu-item-indent");
            indent.style.width = 0;
            indent.style.height = 1;
            indent.style.flexShrink = 0;
            container.Add(indent);

            var foldoutButton = new Button();
            foldoutButton.AddToClassList("foldout-button");
            foldoutButton.text = "►";
            foldoutButton.style.width = 18;
            foldoutButton.style.height = 18;
            foldoutButton.style.paddingLeft = 0;
            foldoutButton.style.paddingRight = 0;
            foldoutButton.style.paddingTop = 0;
            foldoutButton.style.paddingBottom = 0;
            foldoutButton.style.marginLeft = 0;
            foldoutButton.style.marginRight = 3;
            foldoutButton.style.marginTop = 0;
            foldoutButton.style.marginBottom = 0;
            foldoutButton.style.flexShrink = 0;
            foldoutButton.clicked += () =>
            {
                if (container.userData is MenuItemNode node)
                {
                    OnToggleFoldout(node);
                }
            };
            container.Add(foldoutButton);

            var icon = new VisualElement();
            icon.AddToClassList("menu-item-icon");
            icon.style.width = 16;
            icon.style.height = 16;
            icon.style.marginRight = 4;
            icon.style.flexShrink = 0;
            container.Add(icon);

            var label = new Label();
            label.AddToClassList("menu-item-label");
            label.style.flexGrow = 0;
            label.style.flexShrink = 1;
            label.style.marginRight = 5;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            container.Add(label);

            var dividerLine = new VisualElement();
            dividerLine.AddToClassList("favorites-divider-line");
            dividerLine.style.display = DisplayStyle.None;
            container.Add(dividerLine);

            var favoriteButton = new Button();
            favoriteButton.AddToClassList("favorite-button");
            favoriteButton.style.width = 20;
            favoriteButton.style.height = 18;
            favoriteButton.style.paddingLeft = 0;
            favoriteButton.style.paddingRight = 0;
            favoriteButton.style.paddingTop = 0;
            favoriteButton.style.paddingBottom = 0;
            favoriteButton.style.marginRight = 4;
            favoriteButton.style.flexShrink = 0;
            favoriteButton.clicked += () =>
            {
                if (container.userData is MenuItemNode node)
                {
                    ToggleFavorite(node);
                }
            };
            container.Add(favoriteButton);

            var spacer = new VisualElement();
            spacer.AddToClassList("spacer");
            spacer.style.flexGrow = 1;
            container.Add(spacer);

            var overrideController = new StateOverrideController();
            overrideController.style.flexShrink = 0;
            container.Add(overrideController);

            return container;
        }

        private void BindMenuItemRow(VisualElement element, int index)
        {
            if (index < 0 || index >= visibleMenuItems.Count) return;

            var node = visibleMenuItems[index];
            var container = element;
            container.userData = node;

            var foldoutButton = container.Q<Button>(className: "foldout-button");
            var indent = container.Q(className: "menu-item-indent");
            var icon = container.Q(className: "menu-item-icon");
            var label = container.Q<Label>(className: "menu-item-label");
            var dividerLine = container.Q(className: "favorites-divider-line");
            var favoriteButton = container.Q<Button>(className: "favorite-button");
            var spacer = container.Q(className: "spacer");
            var overrideController = container.Q<StateOverrideController>();
            overrideController.userData = node;

            if (node.isDivider)
            {
                indent.style.width = 0;
                foldoutButton.style.display = DisplayStyle.None;
                icon.style.display = DisplayStyle.None;
                label.text = "Favorites";
                label.AddToClassList("favorites-divider-label");
                dividerLine.style.display = DisplayStyle.Flex;
                favoriteButton.style.display = DisplayStyle.None;
                spacer.style.display = DisplayStyle.None;
                overrideController.style.display = DisplayStyle.None;
                container.AddToClassList("favorites-divider-row");
                return;
            }

            label.RemoveFromClassList("favorites-divider-label");
            dividerLine.style.display = DisplayStyle.None;
            spacer.style.display = DisplayStyle.Flex;
            container.RemoveFromClassList("favorites-divider-row");

            indent.style.width = node.isFavorite ? 0 : node.depth * 14;

            if (node.children.Count > 0)
            {
                foldoutButton.style.display = DisplayStyle.Flex;
                foldoutButton.text = node.isExpanded ? "▼" : "►";
            }
            else
            {
                foldoutButton.style.display = DisplayStyle.None;
            }

            if (node.menuItem != null)
            {
                label.text = !string.IsNullOrEmpty(node.menuItem.label) ? node.menuItem.label : node.menuItem.gameObject.name;
            }
            else
            {
                label.text = node.displayName ?? string.Empty;
            }

            if (node.isReactiveContent)
            {
                favoriteButton.style.display = DisplayStyle.Flex;
                favoriteButton.text = node.isFavorite ? "★" : "☆";
                favoriteButton.tooltip = node.isFavorite ? "取消收藏" : "收藏并置顶";
            }
            else
            {
                favoriteButton.style.display = DisplayStyle.None;
                favoriteButton.text = string.Empty;
                favoriteButton.tooltip = string.Empty;
            }

            UpdateOverrideControllerState(overrideController);

            var control = node.menuItem?.Control;
            if (control?.icon != null)
            {
                icon.style.backgroundImage = new StyleBackground(control.icon);
                icon.style.display = DisplayStyle.Flex;
            }
            else
            {
                icon.style.backgroundImage = null;
                icon.style.display = DisplayStyle.None;
            }
        }

        private void UpdateOverrideControllerState(StateOverrideController overrideController)
        {
            if (overrideController == null) return;

            var node = overrideController.userData as MenuItemNode;
            if (node?.menuItem == null)
            {
                overrideController.OnStateOverrideChanged = null;
                overrideController.SetWithoutNotify(null);
                overrideController.style.display = DisplayStyle.None;
                return;
            }

            var paramName = SimulatorAPIHelper.GetParameterName(node.menuItem);
            if (string.IsNullOrEmpty(paramName))
            {
                node.parameterName = null;
                overrideController.OnStateOverrideChanged = null;
                overrideController.SetWithoutNotify(null);
                overrideController.style.display = DisplayStyle.None;
                return;
            }

            node.parameterName = paramName;
            overrideController.style.display = DisplayStyle.Flex;
            var currentState = SimulatorAPIHelper.GetCurrentOverrideState(paramName, node.menuItem);
            overrideController.SetWithoutNotify(currentState);
            overrideController.OnStateOverrideChanged = (state) => OnStateOverrideChanged(node, state);
        }

        private void OnToggleFoldout(MenuItemNode node)
        {
            node.isExpanded = !node.isExpanded;
            ToggleChildrenVisibility(node, node.isExpanded);
            UpdateVisibleMenuItems();
            menuItemsListView.Rebuild();
        }

        private void ToggleChildrenVisibility(MenuItemNode node, bool visible)
        {
            foreach (var child in node.children)
            {
                child.isVisible = visible;
                if (child.isExpanded && !visible)
                {
                    ToggleChildrenVisibility(child, false);
                }
            }
        }

        private void ToggleFavorite(MenuItemNode node)
        {
            if (node == null || node.isDivider || !node.isReactiveContent || node.menuItem == null) return;

            var favoriteTarget = node.menuItem.gameObject;
            if (favoriteTarget == null) return;
            var currentAvatar = avatarField?.value as GameObject;

            node.isFavorite = !node.isFavorite;
            if (node.isFavorite)
            {
                favoriteMenuItemObjects.Add(favoriteTarget);
            }
            else
            {
                favoriteMenuItemObjects.Remove(favoriteTarget);
            }

            suppressSceneAvatarRefresh = true;
            try
            {
                SaveFavoriteMenuItemObjects(currentAvatar);
            }
            finally
            {
                suppressSceneAvatarRefresh = false;
            }

            RefreshSceneAvatarList();
            if (currentAvatar != null && avatarField != null)
            {
                avatarField.SetValueWithoutNotify(currentAvatar);
                UpdateSceneAvatarSelection(currentAvatar);
            }

            UpdateFocusAvatarButtonState();
            UpdateSceneAvatarListVisibility();
            UpdateVisibleMenuItems();
            menuItemsListView?.Rebuild();
        }

        private void LoadFavoriteMenuItemObjects(GameObject avatar)
        {
            favoriteMenuItemObjects.Clear();
            currentAvatarFavoriteStore = FindFavoriteStore(avatar);
            if (currentAvatarFavoriteStore == null) return;

            foreach (var favoriteObject in currentAvatarFavoriteStore.favoriteMenuItemObjects)
            {
                if (favoriteObject != null)
                {
                    favoriteMenuItemObjects.Add(favoriteObject);
                }
            }
        }

        private void SaveFavoriteMenuItemObjects(GameObject avatar)
        {
            if (avatar == null) return;

            if (favoriteMenuItemObjects.Count == 0)
            {
                if (currentAvatarFavoriteStore?.gameObject != null)
                {
                    var storageObject = currentAvatarFavoriteStore.gameObject;
                    currentAvatarFavoriteStore = null;
                    Undo.DestroyObjectImmediate(storageObject);
                    MarkSceneDirty(avatar.scene);
                }

                return;
            }

            var store = EnsureFavoriteStore(avatar);
            if (store == null) return;

            Undo.RecordObject(store, "Update MA Menu SwitchBoard Favorites");
            store.favoriteMenuItemObjects.Clear();
            foreach (var favoriteObject in favoriteMenuItemObjects
                .Where(gameObject => gameObject != null)
                .OrderBy(GetHierarchyPath))
            {
                store.favoriteMenuItemObjects.Add(favoriteObject);
            }

            EditorUtility.SetDirty(store);
            currentAvatarFavoriteStore = store;
            MarkSceneDirty(store.gameObject.scene);
        }

        private static MaMenuSwitchBoardAvatarState FindFavoriteStore(GameObject avatar)
        {
            if (avatar == null) return null;

            return avatar.GetComponentsInChildren<MaMenuSwitchBoardAvatarState>(true)
                .FirstOrDefault(store => store != null && store.transform.parent == avatar.transform);
        }

        private static MaMenuSwitchBoardAvatarState EnsureFavoriteStore(GameObject avatar)
        {
            var existingStore = FindFavoriteStore(avatar);
            if (existingStore != null) return existingStore;

            var storageObject = new GameObject(MaMenuSwitchBoardAvatarState.StorageObjectName);
            storageObject.tag = EDITOR_ONLY_TAG;
            Undo.RegisterCreatedObjectUndo(storageObject, "Create MA Menu SwitchBoard Avatar State");
            Undo.SetTransformParent(storageObject.transform, avatar.transform, "Create MA Menu SwitchBoard Avatar State");
            storageObject.transform.localPosition = Vector3.zero;
            storageObject.transform.localRotation = Quaternion.identity;
            storageObject.transform.localScale = Vector3.one;

            var store = Undo.AddComponent<MaMenuSwitchBoardAvatarState>(storageObject);
            EditorUtility.SetDirty(store);
            MarkSceneDirty(avatar.scene);
            return store;
        }

        private static void MarkSceneDirty(UnityEngine.SceneManagement.Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private void OnStateOverrideChanged(MenuItemNode node, bool? state)
        {
            if (string.IsNullOrEmpty(node.parameterName)) return;

            if (state == null)
            {
                SimulatorAPIHelper.ClearOverride(node.parameterName);
            }
            else
            {
                SimulatorAPIHelper.SetOverrideState(node.parameterName, node.menuItem, state.Value);
            }

            RequestRefresh();
        }

        public class StateOverrideController : VisualElement
        {
            public Action<bool?> OnStateOverrideChanged;
            private Button btn_disable;
            private Button btn_default;
            private Button btn_enable;

            public StateOverrideController()
            {
                AddToClassList("state-override-controller");
                style.flexDirection = FlexDirection.Row;
                style.flexShrink = 0;

                btn_disable = new Button();
                btn_disable.AddToClassList("btn-disable");
                btn_disable.text = "-";
                ApplyButtonLayout(btn_disable);
                btn_disable.style.color = new Color(1f, 0.42f, 0.42f);
                btn_disable.clicked += () => SetStateOverride(false);
                Add(btn_disable);

                btn_default = new Button();
                btn_default.AddToClassList("btn-default");
                btn_default.text = " ";
                ApplyButtonLayout(btn_default);
                btn_default.style.color = new Color(0.8f, 0.8f, 0.8f);
                btn_default.clicked += () => SetStateOverride(null);
                Add(btn_default);

                btn_enable = new Button();
                btn_enable.AddToClassList("btn-enable");
                btn_enable.text = "+";
                ApplyButtonLayout(btn_enable);
                btn_enable.style.color = new Color(0.32f, 0.81f, 0.4f);
                btn_enable.clicked += () => SetStateOverride(true);
                Add(btn_enable);
            }

            private static void ApplyButtonLayout(Button button)
            {
                button.style.width = 30;
                button.style.height = 20;
                button.style.paddingLeft = 0;
                button.style.paddingRight = 0;
                button.style.paddingTop = 0;
                button.style.paddingBottom = 0;
                button.style.marginLeft = 0;
                button.style.marginRight = 1;
                button.style.marginTop = 0;
                button.style.marginBottom = 0;
                button.style.flexShrink = 0;
            }

            private static void ClearButtonActive(Button button)
            {
                button.RemoveFromClassList("active");
                button.style.backgroundColor = new StyleColor(StyleKeyword.Null);
            }

            private static void SetButtonActive(Button button, Color backgroundColor)
            {
                button.AddToClassList("active");
                button.style.backgroundColor = backgroundColor;
            }

            private void SetStateOverride(bool? state)
            {
                SetWithoutNotify(state);
                OnStateOverrideChanged?.Invoke(state);
            }

            public void SetWithoutNotify(bool? state)
            {
                RemoveFromClassList("override-enable");
                RemoveFromClassList("override-disable");
                RemoveFromClassList("override-default");
                ClearButtonActive(btn_default);
                ClearButtonActive(btn_disable);
                ClearButtonActive(btn_enable);

                if (state == null)
                {
                    AddToClassList("override-default");
                    SetButtonActive(btn_default, new Color(0.55f, 0.55f, 0.55f, 0.45f));
                }
                else if (state == true)
                {
                    AddToClassList("override-enable");
                    SetButtonActive(btn_enable, new Color(0.32f, 0.81f, 0.4f, 0.45f));
                }
                else
                {
                    AddToClassList("override-disable");
                    SetButtonActive(btn_disable, new Color(1f, 0.42f, 0.42f, 0.45f));
                }
            }
        }

        public class MenuItemNode
        {
            public ModularAvatarMenuItem menuItem;
            public string parameterName;
            public string displayName;
            public List<MenuItemNode> children = new List<MenuItemNode>();
            public int depth;
            public bool isExpanded = true;
            public bool isVisible = true;
            public bool isReactiveContent;
            public bool isEditorOnlyReactiveContent;
            public bool isFavorite;
            public bool isDivider;

            public static MenuItemNode CreateDivider()
            {
                return new MenuItemNode
                {
                    isDivider = true,
                    isVisible = true
                };
            }
        }

        public static class MenuItemDiscoveryHelper
        {
            private static readonly object RootMenuKey = new object();
            private const string MENU_INSTALL_TARGET_TYPE = "nadena.dev.modular_avatar.core.ModularAvatarMenuInstallTarget";

            public static List<MenuItemNode> DiscoverMenuItems(GameObject avatar, ISet<GameObject> favoriteMenuItemObjects)
            {
                if (avatar == null) return new List<MenuItemNode>();

                var avatarDescriptor = avatar.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                if (avatarDescriptor == null) return new List<MenuItemNode>();

                var discovery = new AvatarMenuTreeDiscovery(avatar, favoriteMenuItemObjects);
                return discovery.Build(avatarDescriptor);
            }

            private sealed class AvatarMenuTreeDiscovery
            {
                private readonly GameObject avatar;
                private readonly ISet<GameObject> favoriteMenuItemObjects;
                private readonly Dictionary<object, List<ModularAvatarMenuInstaller>> installersByTarget =
                    new Dictionary<object, List<ModularAvatarMenuInstaller>>();
                private readonly HashSet<ModularAvatarMenuInstaller> redirectedInstallers =
                    new HashSet<ModularAvatarMenuInstaller>();
                private readonly HashSet<ModularAvatarMenuInstaller> visitingInstallers =
                    new HashSet<ModularAvatarMenuInstaller>();
                private readonly HashSet<VRCExpressionsMenu> visitingMenus = new HashSet<VRCExpressionsMenu>();
                private readonly HashSet<object> visitingSources = new HashSet<object>();

                public AvatarMenuTreeDiscovery(GameObject avatar, ISet<GameObject> favoriteMenuItemObjects)
                {
                    this.avatar = avatar;
                    this.favoriteMenuItemObjects = favoriteMenuItemObjects;
                }

                public List<MenuItemNode> Build(VRC.SDK3.Avatars.Components.VRCAvatarDescriptor avatarDescriptor)
                {
                    IndexInstallers();

                    var rootNodes = new List<MenuItemNode>();
                    if (avatarDescriptor.expressionsMenu != null)
                    {
                        VisitMenuAsset(avatarDescriptor.expressionsMenu, rootNodes);
                    }

                    AppendInstallersForTarget(RootMenuKey, rootNodes);
                    return rootNodes;
                }

                private void IndexInstallers()
                {
                    foreach (var installTarget in GetMenuInstallTargets(avatar))
                    {
                        var installer = GetTargetedInstaller(installTarget);
                        if (installer != null)
                        {
                            redirectedInstallers.Add(installer);
                        }
                    }

                    foreach (var installer in avatar.GetComponentsInChildren<ModularAvatarMenuInstaller>(true))
                    {
                        if (installer == null || redirectedInstallers.Contains(installer)) continue;

                        var targetKey = installer.installTargetMenu != null
                            ? (object)installer.installTargetMenu
                            : RootMenuKey;

                        if (!installersByTarget.TryGetValue(targetKey, out var installers))
                        {
                            installers = new List<ModularAvatarMenuInstaller>();
                            installersByTarget[targetKey] = installers;
                        }

                        installers.Add(installer);
                    }
                }

                private void VisitMenuAsset(VRCExpressionsMenu menu, List<MenuItemNode> targetNodes)
                {
                    if (menu == null || !visitingMenus.Add(menu)) return;

                    try
                    {
                        foreach (var control in menu.controls)
                        {
                            targetNodes.Add(CreateAssetControlNode(control));
                        }

                        AppendInstallersForTarget(menu, targetNodes);
                    }
                    finally
                    {
                        visitingMenus.Remove(menu);
                    }
                }

                private void AppendInstallersForTarget(object targetKey, List<MenuItemNode> targetNodes)
                {
                    if (!installersByTarget.TryGetValue(targetKey, out var installers)) return;

                    foreach (var installer in installers)
                    {
                        VisitInstaller(installer, targetNodes);
                    }
                }

                private void VisitInstaller(ModularAvatarMenuInstaller installer, List<MenuItemNode> targetNodes)
                {
                    if (installer == null || !visitingInstallers.Add(installer)) return;

                    try
                    {
                        var source = GetFirstMenuSource(installer.gameObject);
                        if (source != null)
                        {
                            VisitMenuSource(source, targetNodes);
                        }
                        else if (installer.menuToAppend != null)
                        {
                            VisitMenuAsset(installer.menuToAppend, targetNodes);
                        }
                    }
                    finally
                    {
                        visitingInstallers.Remove(installer);
                    }
                }

                private void VisitMenuSource(MenuSource source, List<MenuItemNode> targetNodes)
                {
                    if (source == null || !visitingSources.Add(source)) return;

                    try
                    {
                        switch (source)
                        {
                            case ModularAvatarMenuItem menuItem:
                                targetNodes.Add(CreateMenuItemNode(menuItem));
                                break;
                            case ModularAvatarMenuGroup menuGroup:
                                VisitChildrenMenuSources(menuGroup.targetObject != null
                                    ? menuGroup.targetObject
                                    : menuGroup.gameObject, targetNodes);
                                break;
                            case Component component when IsMenuInstallTarget(component):
                                VisitInstaller(GetTargetedInstaller(component), targetNodes);
                                break;
                        }
                    }
                    finally
                    {
                        visitingSources.Remove(source);
                    }
                }

                private void VisitChildrenMenuSources(GameObject rootObject, List<MenuItemNode> targetNodes)
                {
                    if (rootObject == null) return;

                    foreach (Transform child in rootObject.transform)
                    {
                        var source = GetFirstMenuSource(child.gameObject);
                        if (source != null)
                        {
                            VisitMenuSource(source, targetNodes);
                        }
                    }
                }

                private MenuItemNode CreateMenuItemNode(ModularAvatarMenuItem menuItem)
                {
                    var (isReactiveContent, isEditorOnlyReactiveContent) = ClassifyReactiveContent(menuItem);
                    var node = new MenuItemNode
                    {
                        menuItem = menuItem,
                        displayName = !string.IsNullOrEmpty(menuItem.label) ? menuItem.label : menuItem.gameObject.name,
                        isReactiveContent = isReactiveContent,
                        isEditorOnlyReactiveContent = isEditorOnlyReactiveContent,
                        isFavorite = favoriteMenuItemObjects != null && favoriteMenuItemObjects.Contains(menuItem.gameObject)
                    };

                    if (menuItem.Control != null &&
                        menuItem.Control.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                    {
                        switch (menuItem.MenuSource)
                        {
                            case SubmenuSource.MenuAsset:
                                VisitMenuAsset(menuItem.Control.subMenu, node.children);
                                break;
                            case SubmenuSource.Children:
                            {
                                var rootObject = menuItem.menuSource_otherObjectChildren != null
                                    ? menuItem.menuSource_otherObjectChildren
                                    : menuItem.gameObject;
                                VisitChildrenMenuSources(rootObject, node.children);
                                break;
                            }
                        }
                    }

                    return node;
                }

                private MenuItemNode CreateAssetControlNode(VRCExpressionsMenu.Control control)
                {
                    var node = new MenuItemNode
                    {
                        displayName = control != null && !string.IsNullOrEmpty(control.name)
                            ? control.name
                            : string.Empty
                    };

                    if (control != null && control.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                    {
                        VisitMenuAsset(control.subMenu, node.children);
                    }

                    return node;
                }

                private static IEnumerable<Component> GetMenuInstallTargets(GameObject avatar)
                {
                    return avatar.GetComponentsInChildren<Component>(true)
                        .Where(component => IsMenuInstallTarget(component));
                }

                private static bool IsMenuInstallTarget(Component component)
                {
                    return component != null &&
                           string.Equals(component.GetType().FullName, MENU_INSTALL_TARGET_TYPE, StringComparison.Ordinal);
                }

                private static ModularAvatarMenuInstaller GetTargetedInstaller(Component installTarget)
                {
                    if (!IsMenuInstallTarget(installTarget)) return null;

                    var field = installTarget.GetType().GetField("installer");
                    return field?.GetValue(installTarget) as ModularAvatarMenuInstaller;
                }

                private static MenuSource GetFirstMenuSource(GameObject gameObject)
                {
                    if (gameObject == null) return null;

                    return gameObject.GetComponents<MonoBehaviour>()
                        .OfType<MenuSource>()
                        .FirstOrDefault();
                }
            }

            private static (bool isReactiveContent, bool isEditorOnlyReactiveContent) ClassifyReactiveContent(ModularAvatarMenuItem menuItem)
            {
                if (menuItem == null) return (false, false);

                var reactiveComponents = menuItem.GetComponentsInChildren<ReactiveComponent>(true);
                var hasReactiveContent = false;
                var allReactiveContentEditorOnly = true;

                foreach (var reactiveComponent in reactiveComponents)
                {
                    if (reactiveComponent == null) continue;

                    if (reactiveComponent.transform != menuItem.transform)
                    {
                        var parentMenuItem = reactiveComponent.GetComponentInParent<ModularAvatarMenuItem>();
                        if (parentMenuItem != menuItem)
                        {
                            continue;
                        }
                    }

                    hasReactiveContent = true;
                    if (!IsEditorOnlyReactiveComponent(reactiveComponent))
                    {
                        allReactiveContentEditorOnly = false;
                        break;
                    }
                }

                return (hasReactiveContent, hasReactiveContent && allReactiveContentEditorOnly);
            }

            private static bool IsEditorOnlyReactiveComponent(ReactiveComponent reactiveComponent)
            {
                if (reactiveComponent == null) return false;
                if (IsEditorOnlyHierarchy(reactiveComponent.transform)) return true;

                var getObjectReferencesMethod = reactiveComponent.GetType().GetMethod("GetObjectReferences");
                if (getObjectReferencesMethod == null || getObjectReferencesMethod.GetParameters().Length != 0)
                {
                    return false;
                }

                if (!(getObjectReferencesMethod.Invoke(reactiveComponent, null) is System.Collections.IEnumerable references))
                {
                    return false;
                }

                var foundReference = false;
                foreach (var reference in references)
                {
                    if (reference == null) continue;

                    var getMethod = reference.GetType().GetMethod("Get", new[] { typeof(Component) });
                    if (getMethod == null) continue;

                    var targetObject = getMethod.Invoke(reference, new object[] { reactiveComponent }) as GameObject;
                    if (targetObject == null) continue;

                    foundReference = true;
                    if (!IsEditorOnlyHierarchy(targetObject.transform))
                    {
                        return false;
                    }
                }

                return foundReference;
            }

            private static bool IsEditorOnlyHierarchy(Transform transform)
            {
                var current = transform;
                while (current != null)
                {
                    if (string.Equals(current.tag, EDITOR_ONLY_TAG, StringComparison.Ordinal))
                    {
                        return true;
                    }

                    current = current.parent;
                }

                return false;
            }
        }

        public static class SimulatorAPIHelper
        {
            private const string ROSIMULATOR_TYPE_NAME = "nadena.dev.modular_avatar.core.editor.Simulator.ROSimulator, nadena.dev.modular-avatar.core.editor";
            private const string MENU_ITEM_OVERRIDES_FIELD = "MenuItemOverrides";
            private const string PROPERTY_OVERRIDES_FIELD = "PropertyOverrides";
            private const string ON_CHANGE_EVENT = "OnChange";
            private const string VALUE_PROPERTY = "Value";

            private static object _menuItemOverridesField;
            private static object _propertyOverridesField;
            private static bool _initialized = false;
            private static bool _available = false;

            private static void InitializeReflection()
            {
                if (_initialized) return;
                _initialized = true;

                try
                {
                    // Use reflection to access ROSimulator's internal static fields
                    var roSimulatorType = Type.GetType(ROSIMULATOR_TYPE_NAME);
                    if (roSimulatorType == null)
                    {
                        Debug.LogWarning("MaMenuSwitchBoard: Could not find ROSimulator type. Modular Avatar may not be installed.");
                        return;
                    }

                    var menuItemOverridesFieldInfo = roSimulatorType.GetField(MENU_ITEM_OVERRIDES_FIELD,
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    var propertyOverridesFieldInfo = roSimulatorType.GetField(PROPERTY_OVERRIDES_FIELD,
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                    if (menuItemOverridesFieldInfo != null && propertyOverridesFieldInfo != null)
                    {
                        _menuItemOverridesField = menuItemOverridesFieldInfo.GetValue(null);
                        _propertyOverridesField = propertyOverridesFieldInfo.GetValue(null);
                        _available = true;
                    }
                    else
                    {
                        Debug.LogWarning("MaMenuSwitchBoard: Could not find MenuItemOverrides or PropertyOverrides fields in ROSimulator. Modular Avatar API may have changed.");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"MaMenuSwitchBoard: Failed to initialize ROSimulator reflection: {e.Message}");
                }
            }

            private static object GetMenuItemOverridesPublishedValueObject()
            {
                InitializeReflection();
                if (!_available || _menuItemOverridesField == null)
                    return null;
                return _menuItemOverridesField;
            }

            private static object GetPropertyOverridesPublishedValueObject()
            {
                InitializeReflection();
                if (!_available || _propertyOverridesField == null)
                    return null;
                return _propertyOverridesField;
            }

            private static dynamic GetMenuItemOverridesPublishedValue()
            {
                return (dynamic)GetMenuItemOverridesPublishedValueObject();
            }

            private static dynamic GetPropertyOverridesPublishedValue()
            {
                return (dynamic)GetPropertyOverridesPublishedValueObject();
            }

            private static object GetPublishedValueValue(object publishedValue)
            {
                return publishedValue?.GetType().GetProperty(VALUE_PROPERTY)?.GetValue(publishedValue);
            }

            private static Type GetOnChangeHandlerType(object publishedValue)
            {
                return publishedValue?.GetType().GetEvent(ON_CHANGE_EVENT)?.EventHandlerType;
            }

            private static bool TryAddOnChangeHandler(object publishedValue, Delegate handler)
            {
                if (publishedValue == null || handler == null) return false;

                try
                {
                    var eventInfo = publishedValue.GetType().GetEvent(ON_CHANGE_EVENT);
                    if (eventInfo == null) return false;

                    eventInfo.AddEventHandler(publishedValue, handler);
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"MaMenuSwitchBoard: Failed to subscribe to ROSimulator override changes: {e.Message}");
                    return false;
                }
            }

            private static bool TryRemoveOnChangeHandler(object publishedValue, Delegate handler)
            {
                if (publishedValue == null || handler == null) return false;

                try
                {
                    var eventInfo = publishedValue.GetType().GetEvent(ON_CHANGE_EVENT);
                    if (eventInfo == null) return false;

                    eventInfo.RemoveEventHandler(publishedValue, handler);
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"MaMenuSwitchBoard: Failed to unsubscribe from ROSimulator override changes: {e.Message}");
                    return false;
                }
            }

            public static object GetMenuItemOverridesValueReference()
            {
                return GetPublishedValueValue(GetMenuItemOverridesPublishedValueObject());
            }

            public static object GetPropertyOverridesValueReference()
            {
                return GetPublishedValueValue(GetPropertyOverridesPublishedValueObject());
            }

            public static bool OverrideValueReferencesChanged(ref object lastMenuItemOverrides, ref object lastPropertyOverrides)
            {
                var currentMenuItemOverrides = GetMenuItemOverridesValueReference();
                var currentPropertyOverrides = GetPropertyOverridesValueReference();
                var changed = !ReferenceEquals(lastMenuItemOverrides, currentMenuItemOverrides) ||
                              !ReferenceEquals(lastPropertyOverrides, currentPropertyOverrides);

                lastMenuItemOverrides = currentMenuItemOverrides;
                lastPropertyOverrides = currentPropertyOverrides;
                return changed;
            }

            public static Type GetMenuItemOverridesOnChangeHandlerType()
            {
                return GetOnChangeHandlerType(GetMenuItemOverridesPublishedValueObject());
            }

            public static Type GetPropertyOverridesOnChangeHandlerType()
            {
                return GetOnChangeHandlerType(GetPropertyOverridesPublishedValueObject());
            }

            public static bool TryAddMenuItemOverridesOnChangeHandler(Delegate handler)
            {
                return TryAddOnChangeHandler(GetMenuItemOverridesPublishedValueObject(), handler);
            }

            public static bool TryAddPropertyOverridesOnChangeHandler(Delegate handler)
            {
                return TryAddOnChangeHandler(GetPropertyOverridesPublishedValueObject(), handler);
            }

            public static bool TryRemoveMenuItemOverridesOnChangeHandler(Delegate handler)
            {
                return TryRemoveOnChangeHandler(GetMenuItemOverridesPublishedValueObject(), handler);
            }

            public static bool TryRemovePropertyOverridesOnChangeHandler(Delegate handler)
            {
                return TryRemoveOnChangeHandler(GetPropertyOverridesPublishedValueObject(), handler);
            }

            public static string GetParameterName(ModularAvatarMenuItem menuItem)
            {
                if (menuItem == null) return null;

                // Try to get explicitly assigned parameter name
                if (menuItem.Control != null && menuItem.Control.parameter != null &&
                    !string.IsNullOrWhiteSpace(menuItem.Control.parameter.name))
                {
                    return menuItem.Control.parameter.name;
                }

                // Handle auto-assigned parameters (same pattern as ParameterAssignerPass)
                if (menuItem.Control != null && ShouldAssignAutoParameter(menuItem))
                {
                    return "___AutoProp/" + menuItem.GetInstanceID();
                }

                return null;
            }

            private static bool ShouldAssignAutoParameter(ModularAvatarMenuItem menuItem)
            {
                // Only Button and Toggle types get auto-assigned parameters
                var controlType = menuItem.Control?.type;
                if (controlType != VRCExpressionsMenu.Control.ControlType.Button &&
                    controlType != VRCExpressionsMenu.Control.ControlType.Toggle)
                {
                    return false;
                }

                // Check if this menu item controls any ReactiveComponent children
                var reactiveComponents = menuItem.GetComponentsInChildren<ReactiveComponent>(true);
                foreach (var rc in reactiveComponents)
                {
                    // ReactiveComponent on same GameObject
                    if (rc.transform == menuItem.transform)
                        return true;

                    // ReactiveComponent's nearest parent MenuItem is this one
                    var parentMenuItem = rc.GetComponentInParent<ModularAvatarMenuItem>();
                    if (parentMenuItem == menuItem)
                        return true;
                }

                return false;
            }

            public static bool? GetCurrentOverrideState(string paramName, ModularAvatarMenuItem menuItem)
            {
                if (string.IsNullOrEmpty(paramName)) return null;

                try
                {
                    dynamic overridesPublishedValue = GetMenuItemOverridesPublishedValue();
                    if (overridesPublishedValue == null) return null;

                    var overrides = overridesPublishedValue.Value as ImmutableDictionary<string, ModularAvatarMenuItem>;
                    if (overrides == null || !overrides.TryGetValue(paramName, out var overrideItem))
                    {
                        return null;
                    }

                    if (overrideItem == null) return false;
                    return ReferenceEquals(overrideItem, menuItem);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"MaMenuSwitchBoard: Error getting override state: {e.Message}");
                    return null;
                }
            }

            public static void SetOverrideState(string paramName, ModularAvatarMenuItem targetItem, bool value)
            {
                if (string.IsNullOrEmpty(paramName)) return;

                try
                {
                    dynamic overridesPublishedValue = GetMenuItemOverridesPublishedValue();
                    if (overridesPublishedValue == null) return;

                    var currentOverrides = (ImmutableDictionary<string, ModularAvatarMenuItem>)overridesPublishedValue.Value
                        ?? ImmutableDictionary<string, ModularAvatarMenuItem>.Empty;

                    if (value)
                    {
                        overridesPublishedValue.Value = currentOverrides.SetItem(paramName, targetItem);
                    }
                    else
                    {
                        if (!currentOverrides.TryGetValue(paramName, out var existing) || ReferenceEquals(existing, targetItem))
                        {
                            overridesPublishedValue.Value = currentOverrides.SetItem(paramName, null);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"MaMenuSwitchBoard: Error setting override state: {e.Message}");
                }
            }

            public static void ClearOverride(string paramName)
            {
                if (string.IsNullOrEmpty(paramName)) return;

                try
                {
                    dynamic overridesPublishedValue = GetMenuItemOverridesPublishedValue();
                    if (overridesPublishedValue == null) return;

                    var currentOverrides = (ImmutableDictionary<string, ModularAvatarMenuItem>)overridesPublishedValue.Value
                        ?? ImmutableDictionary<string, ModularAvatarMenuItem>.Empty;
                    if (currentOverrides.ContainsKey(paramName))
                    {
                        overridesPublishedValue.Value = currentOverrides.Remove(paramName);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"MaMenuSwitchBoard: Error clearing override: {e.Message}");
                }
            }

            public static void ClearAllOverrides()
            {
                try
                {
                    dynamic menuItemOverridesPublishedValue = GetMenuItemOverridesPublishedValue();
                    dynamic propertyOverridesPublishedValue = GetPropertyOverridesPublishedValue();

                    if (menuItemOverridesPublishedValue != null)
                        menuItemOverridesPublishedValue.Value = ImmutableDictionary<string, ModularAvatarMenuItem>.Empty;

                    if (propertyOverridesPublishedValue != null)
                        propertyOverridesPublishedValue.Value = ImmutableDictionary<string, float>.Empty;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"MaMenuSwitchBoard: Error clearing all overrides: {e.Message}");
                }
            }
        }
    }
}
// #endif
