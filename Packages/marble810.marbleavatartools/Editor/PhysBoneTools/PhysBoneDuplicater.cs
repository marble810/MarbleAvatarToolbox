using UnityEngine;
using UnityEditor;
using marble810.MarbleAvatarTools.Utils;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using System;
using marble810.MarbleAvatarTools.CustomGUI;
using marble810.MarbleAvatarTools.CustomGUI.Helper;
using VRC.SDK3.Dynamics.PhysBone.Components;
using System.Collections.Generic;

namespace marble810.MarbleAvatarTools.PhysBoneTools
{
    public class PhysBoneDuplicater : EditorWindow
    {

        private SerializedObject _serializedObject;
        private TempContainer _tempObject;

        private HierarchyNodeView _sourceTreeView;

        private HierarchyNodeView _targetTreeView;

        // Store match info separately from isSelected
        private Dictionary<GameObject, GameObject> _matchedPairs = new Dictionary<GameObject, GameObject>();

        // For selection synchronization
        private bool _isSyncing = false;
        private IVisualElementScheduledItem _syncSchedule;

        [MenuItem("MarbleAvatarTools/PhysBone/PhysBone Duplicater")]
        public static void ShowWindow()
        {
            GetWindow<PhysBoneDuplicater>("PhysBone Duplicater");
        }

        private void OnEnable()
        {
            if (_tempObject == null)
            {
                _tempObject = ScriptableObject.CreateInstance<TempContainer>();
                _tempObject.hideFlags = HideFlags.DontSave;
            }
            _serializedObject = new SerializedObject(_tempObject);

            _serializedObject.Update();

            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            _syncSchedule?.Pause();
            _syncSchedule = null;
            _serializedObject?.Dispose();
            _sourceTreeView = null;
            _targetTreeView = null;
            _matchedPairs?.Clear();
            if (_tempObject != null) DestroyImmediate(_tempObject);
            _tempObject = null;
        }

        private void OnUndoRedo()
        {
            // Undo/Redo 后立即刷新（不等待定时器）
            _serializedObject?.Update();
            rootVisualElement?.Bind(_serializedObject);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;

            var mainTitle = new Label("Marble PhysBone Duplicater");
            mainTitle.style.unityFontStyleAndWeight = FontStyle.BoldAndItalic;
            mainTitle.style.alignSelf = Align.Center;
            root.Add(mainTitle);

            _serializedObject.Update();

            var sourceRootProp = _serializedObject.FindProperty("sourceArmatureRoot");
            var sourceRootField = new PropertyField(sourceRootProp);
            root.Add(sourceRootField);

            _sourceTreeView = new HierarchyNodeView(
                _serializedObject.FindProperty("sourceChain"),
                CreateSourceInfo
            );
            root.Add(_sourceTreeView);

            root.Add(new Separator());

            var targetRootProp = _serializedObject.FindProperty("targetArmatureRoot");
            var targetRootField = new PropertyField(targetRootProp);
            root.Add(targetRootField);

            _targetTreeView = new HierarchyNodeView(
                _serializedObject.FindProperty("targetChain"),
                CreateTargetInfo
            );
            root.Add(_targetTreeView);

            root.Add(new Separator());

            // Add copy button
            var copyButton = new Button(() => CopyPhysBonesToSelected())
            {
                text = "Copy PhysBones to Selected"
            };
            copyButton.style.height = 30;
            copyButton.style.marginTop = 10;
            copyButton.style.marginBottom = 10;
            root.Add(copyButton);

            //TrackRootFieldChanged
            root.TrackPropertyValue(sourceRootProp, OnSourceRootChanged);
            root.TrackPropertyValue(targetRootProp, OnTargetRootChanged);



            root.Bind(_serializedObject);

            // If both source and target are already set, perform initial comparison
            if (_tempObject.sourceArmatureRoot != null && _tempObject.targetArmatureRoot != null)
            {
                CompareAndMarkMatches();
                _sourceTreeView.Rebind(_serializedObject.FindProperty("sourceChain"));
                _targetTreeView.Rebind(_serializedObject.FindProperty("targetChain"));
            }

            // Setup selection sync scheduler
            SetupSelectionSync(root);
        }



        private void SetupSelectionSync(VisualElement root)
        {
            // Cache previous selection states for comparison
            Dictionary<string, bool> _prevSourceSelection = new Dictionary<string, bool>();
            Dictionary<string, bool> _prevTargetSelection = new Dictionary<string, bool>();

            _syncSchedule = root.schedule.Execute(() =>
            {
                if (_isSyncing) return;
                if (_tempObject?.sourceChain == null || _tempObject?.targetChain == null) return;

                _serializedObject.Update();

                // Check source changes and sync to target
                bool sourceChanged = CheckAndUpdateSelection(_tempObject.sourceChain, _prevSourceSelection);
                bool targetChanged = CheckAndUpdateSelection(_tempObject.targetChain, _prevTargetSelection);

                if (sourceChanged && !targetChanged)
                {
                    _isSyncing = true;
                    SyncSelection(_tempObject.sourceChain, _tempObject.targetChain);
                    _serializedObject.ApplyModifiedProperties();
                    UpdateSelectionCache(_tempObject.targetChain, _prevTargetSelection);
                    _targetTreeView.Refresh();
                    _isSyncing = false;
                }
                else if (targetChanged && !sourceChanged)
                {
                    _isSyncing = true;
                    SyncSelection(_tempObject.targetChain, _tempObject.sourceChain);
                    _serializedObject.ApplyModifiedProperties();
                    UpdateSelectionCache(_tempObject.sourceChain, _prevSourceSelection);
                    _sourceTreeView.Refresh();
                    _isSyncing = false;
                }
            }).Every(100);
        }

        private bool CheckAndUpdateSelection(HierarchyNode node, Dictionary<string, bool> prevSelection)
        {
            if (node == null) return false;

            bool changed = false;
            CheckSelectionRecursive(node, "", prevSelection, ref changed);
            return changed;
        }

        private void CheckSelectionRecursive(HierarchyNode node, string path, Dictionary<string, bool> prevSelection, ref bool changed)
        {
            if (node == null || node.nodeObject == null) return;

            string key = path + "/" + node.nodeObject.GetInstanceID();

            if (prevSelection.TryGetValue(key, out bool prevValue))
            {
                if (prevValue != node.isSelected)
                {
                    changed = true;
                    prevSelection[key] = node.isSelected;
                }
            }
            else
            {
                prevSelection[key] = node.isSelected;
            }

            for (int i = 0; i < node.children.Count; i++)
            {
                CheckSelectionRecursive(node.children[i], key, prevSelection, ref changed);
            }
        }

        private void UpdateSelectionCache(HierarchyNode node, Dictionary<string, bool> cache)
        {
            if (node == null) return;
            UpdateSelectionCacheRecursive(node, "", cache);
        }

        private void UpdateSelectionCacheRecursive(HierarchyNode node, string path, Dictionary<string, bool> cache)
        {
            if (node == null || node.nodeObject == null) return;

            string key = path + "/" + node.nodeObject.GetInstanceID();
            cache[key] = node.isSelected;

            for (int i = 0; i < node.children.Count; i++)
            {
                UpdateSelectionCacheRecursive(node.children[i], key, cache);
            }
        }

        private void SyncSelection(HierarchyNode fromNode, HierarchyNode toNode)
        {
            if (fromNode == null || toNode == null) return;

            toNode.isSelected = fromNode.isSelected;

            if (fromNode.children.Count == toNode.children.Count)
            {
                for (int i = 0; i < fromNode.children.Count; i++)
                {
                    SyncSelection(fromNode.children[i], toNode.children[i]);
                }
            }
        }

        private void OnSourceRootChanged(SerializedProperty prop)
        {
            var go = prop.objectReferenceValue as GameObject;

            _tempObject.sourceChain = HierarchyNodeHandler.GetHierarchyNode(go);
            _serializedObject.Update();
            _sourceTreeView.Rebind(_serializedObject.FindProperty("sourceChain"));

            // If target is already set, update matches
            if (_tempObject.targetArmatureRoot != null)
            {
                CompareAndMarkMatches();
                _sourceTreeView.Rebind(_serializedObject.FindProperty("sourceChain"));
                _targetTreeView.Rebind(_serializedObject.FindProperty("targetChain"));
            }
        }
        private void OnTargetRootChanged(SerializedProperty prop)
        {
            var go = prop.objectReferenceValue as GameObject;

            _tempObject.targetChain = HierarchyNodeHandler.GetHierarchyNode(go);
            _serializedObject.Update();

            // Compare hierarchies and update matches
            CompareAndMarkMatches();

            _sourceTreeView.Rebind(_serializedObject.FindProperty("sourceChain"));
            _targetTreeView.Rebind(_serializedObject.FindProperty("targetChain"));
        }

        private void CompareAndMarkMatches()
        {
            if (_tempObject.sourceChain == null || _tempObject.targetChain == null) return;

            // Clear previous matches
            _matchedPairs.Clear();

            // Reset all selections first
            ResetAllSelections(_tempObject.sourceChain);
            ResetAllSelections(_tempObject.targetChain);

            // Compare hierarchies, mark matches and auto-select
            CompareHierarchies(_tempObject.sourceChain, _tempObject.targetChain);

            _serializedObject.Update();
        }

        private void ResetAllSelections(HierarchyNode node)
        {
            if (node == null) return;

            node.isSelected = false;

            foreach (var child in node.children)
            {
                ResetAllSelections(child);
            }
        }

        private void CompareHierarchies(HierarchyNode sourceNode, HierarchyNode targetNode)
        {
            if (sourceNode == null || targetNode == null) return;
            if (sourceNode.nodeObject == null || targetNode.nodeObject == null) return;

            // Check if source node has PhysBone
            var sourcePhysBone = sourceNode.nodeObject.GetComponent<VRCPhysBone>();
            if (sourcePhysBone != null)
            {
                // Store match pair (target -> source)
                _matchedPairs[targetNode.nodeObject] = sourceNode.nodeObject;

                // Auto-select both source and target nodes that have PhysBone
                sourceNode.isSelected = true;
                targetNode.isSelected = true;
            }

            // Compare children
            if (sourceNode.children.Count == targetNode.children.Count)
            {
                for (int i = 0; i < sourceNode.children.Count; i++)
                {
                    CompareHierarchies(sourceNode.children[i], targetNode.children[i]);
                }
            }
        }

        private VisualElement CreateSourceInfo(SerializedProperty nodeProp)
        {
            var nodeObj = nodeProp.FindPropertyRelative("nodeObject");
            var go = nodeObj?.objectReferenceValue as GameObject;
            if (go == null) return null;

            var physBone = go.GetComponent<VRCPhysBone>();
            if (physBone == null) return null;

            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;

            var label = new Label($"Pull: {physBone.pull}");
            label.style.color = ColorPreset.Blue;

            container.Add(label);
            return container;
        }

        private VisualElement CreateTargetInfo(SerializedProperty nodeProp)
        {
            var nodeObj = nodeProp.FindPropertyRelative("nodeObject");
            var go = nodeObj?.objectReferenceValue as GameObject;
            if (go == null) return null;

            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;

            // Check if this target node has a matched source node
            if (_matchedPairs.ContainsKey(go))
            {
                var sourceGo = _matchedPairs[go];
                bool namesMatch = sourceGo.name == go.name;

                var label = new Label("Matched");
                label.style.color = namesMatch ? ColorPreset.Green : ColorPreset.Yellow;
                container.Add(label);
            }

            return container;
        }

        private void CopyPhysBonesToSelected()
        {
            if (_tempObject.sourceChain == null || _tempObject.targetChain == null)
            {
                EditorUtility.DisplayDialog("Error", "Source or target chain is not set", "OK");
                return;
            }

            // Start undo group
            Undo.SetCurrentGroupName("Copy PhysBones");
            int undoGroup = Undo.GetCurrentGroup();

            int copiedCount = 0;
            CopyPhysBonesRecursive(_tempObject.sourceChain, _tempObject.targetChain, ref copiedCount);

            // Collapse all undo operations into one group
            Undo.CollapseUndoOperations(undoGroup);

            if (copiedCount > 0)
            {
                EditorUtility.DisplayDialog("Success", $"Successfully copied {copiedCount} PhysBone component(s)", "OK");
                // Refresh the UI
                _targetTreeView.Rebind(_serializedObject.FindProperty("targetChain"));
            }
            else
            {
                EditorUtility.DisplayDialog("Warning", "No selected target nodes to copy PhysBones to.\nPlease select at least one node with a corresponding PhysBone in the source.", "OK");
            }
        }

        private void CopyPhysBonesRecursive(HierarchyNode sourceNode, HierarchyNode targetNode, ref int copiedCount)
        {
            if (sourceNode == null || targetNode == null) return;
            if (sourceNode.nodeObject == null || targetNode.nodeObject == null) return;

            // Check if target node is selected
            if (targetNode.isSelected)
            {
                var sourcePhysBone = sourceNode.nodeObject.GetComponent<VRCPhysBone>();
                if (sourcePhysBone != null)
                {
                    // Check if target already has a PhysBone component
                    var targetPhysBone = targetNode.nodeObject.GetComponent<VRCPhysBone>();
                    if (targetPhysBone != null)
                    {
                        // Remove existing component
                        Undo.DestroyObjectImmediate(targetPhysBone);
                    }

                    // Copy component with undo support
                    targetPhysBone = Undo.AddComponent<VRCPhysBone>(targetNode.nodeObject);
                    EditorUtility.CopySerialized(sourcePhysBone, targetPhysBone);

                    copiedCount++;
                }
            }

            // Process children
            if (sourceNode.children.Count == targetNode.children.Count)
            {
                for (int i = 0; i < sourceNode.children.Count; i++)
                {
                    CopyPhysBonesRecursive(sourceNode.children[i], targetNode.children[i], ref copiedCount);
                }
            }
        }



        [Serializable]
        private class TempContainer : ScriptableObject
        {
            public GameObject sourceArmatureRoot;
            public GameObject targetArmatureRoot;
            public HierarchyNode sourceChain;
            public HierarchyNode targetChain;
        }
    }

}