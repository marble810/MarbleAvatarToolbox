using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using marble810.MarbleAvatarTools.Utils;
using marble810.MarbleAvatarTools.CustomGUI;


namespace marble810.MarbleAvatarTools.AnimatingHelper
{
    public class BoneKeyMirror : EditorWindow
    {
        #region Fields

        private SerializedObject _serializedObject;
        private TempContainer _tempObject;
        private HierarchyNodeView _sourceTreeView;
        private HierarchyNodeView _targetTreeView;
        private Label _statusLabel;
        private ObjectField _clipField;

        // Animation Window reflection
        private static Type _animationWindowType;
        private static Type _animationWindowStateType;
        private static PropertyInfo _animationWindowStateProperty;
        private static PropertyInfo _activeAnimationClipProperty;

        #endregion

        #region Mirror Patterns

        private static readonly (string left, string right)[] MirrorPatterns = new[]
        {
            ("Left", "Right"),
            ("left", "right"),
            ("LEFT", "RIGHT"),
            ("_L", "_R"),
            ("_l", "_r"),
            (".L", ".R"),
            (".l", ".r"),
            ("L_", "R_"),
            ("l_", "r_"),
            ("-L", "-R"),
            ("-l", "-r"),
        };

        #endregion

        #region Unity Callbacks

        [MenuItem("MarbleAvatarTools/Bone Key Mirror")]
        public static void ShowWindow()
        {
            var window = GetWindow<BoneKeyMirror>("Bone Key Mirror");
            window.minSize = new Vector2(350, 500);
        }

        private void OnEnable()
        {
            InitializeReflection();

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
            _serializedObject?.Dispose();
            _sourceTreeView = null;
            _targetTreeView = null;
            if (_tempObject != null) DestroyImmediate(_tempObject);
            _tempObject = null;
        }

        private void OnUndoRedo()
        {
            _serializedObject?.Update();
            rootVisualElement?.Bind(_serializedObject);
        }

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();

            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1;
            root.Add(scrollView);

            var container = new VisualElement();
            container.style.paddingLeft = 5;
            container.style.paddingRight = 5;
            container.style.paddingTop = 5;
            container.style.paddingBottom = 5;
            scrollView.Add(container);

            // Title
            var mainTitle = new Label("Bone Key Mirror");
            mainTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            mainTitle.style.fontSize = 14;
            mainTitle.style.alignSelf = Align.Center;
            mainTitle.style.marginBottom = 10;
            container.Add(mainTitle);

            // Animation Clip Section
            var clipSection = new VisualElement();
            clipSection.style.marginBottom = 10;
            container.Add(clipSection);

            var clipLabel = new Label("Animation Clip");
            clipLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            clipSection.Add(clipLabel);

            var clipRow = new VisualElement();
            clipRow.style.flexDirection = FlexDirection.Row;
            clipSection.Add(clipRow);

            _clipField = new ObjectField();
            _clipField.objectType = typeof(AnimationClip);
            _clipField.bindingPath = "animationClip";
            _clipField.style.flexGrow = 1;
            clipRow.Add(_clipField);

            var getClipBtn = new Button(GetCurrentAnimationClip) { text = "Get Current" };
            getClipBtn.style.width = 80;
            clipRow.Add(getClipBtn);

            container.Add(new Separator());

            // Source Section
            var sourceLabel = new Label("Source Bones (Copy From)");
            sourceLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            sourceLabel.style.marginTop = 5;
            container.Add(sourceLabel);

            var sourceRootProp = _serializedObject.FindProperty("sourceArmatureRoot");
            var sourceRootField = new PropertyField(sourceRootProp, "Armature Root");
            container.Add(sourceRootField);

            _sourceTreeView = new HierarchyNodeView(
                _serializedObject.FindProperty("sourceChain"),
                null,
                showToggle: true,
                autoRefresh: true
            );
            container.Add(_sourceTreeView);

            container.Add(new Separator());

            // Target Section
            var targetLabel = new Label("Target Bones (Copy To)");
            targetLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            targetLabel.style.marginTop = 5;
            container.Add(targetLabel);

            var targetRootProp = _serializedObject.FindProperty("targetArmatureRoot");
            var targetRootField = new PropertyField(targetRootProp, "Armature Root");
            container.Add(targetRootField);

            _targetTreeView = new HierarchyNodeView(
                _serializedObject.FindProperty("targetChain"),
                null,
                showToggle: true,
                autoRefresh: true
            );
            container.Add(_targetTreeView);

            container.Add(new Separator());

            // Mirror Axis Section
            var mirrorLabel = new Label("Mirror Axis (Normal Axis Rule)");
            mirrorLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            mirrorLabel.style.marginTop = 5;
            container.Add(mirrorLabel);

            var mirrorContainer = new VisualElement();
            mirrorContainer.style.paddingLeft = 10;
            mirrorContainer.style.paddingTop = 5;
            mirrorContainer.style.paddingBottom = 5;
            container.Add(mirrorContainer);

            // Mirror Axis EnumField
            var axisField = new EnumField("Mirror Axis", MirrorAxis.X);
            axisField.bindingPath = "mirrorAxis";
            mirrorContainer.Add(axisField);

            // Rule description label
            var ruleLabel = new Label();
            ruleLabel.style.marginTop = 5;
            ruleLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            ruleLabel.style.fontSize = 11;
            mirrorContainer.Add(ruleLabel);

            // Update rule description when axis changes
            void UpdateRuleLabel(MirrorAxis axis)
            {
                ruleLabel.text = axis switch
                {
                    MirrorAxis.X => "X Axis (YZ Plane): Pos(-x, y, z) Rot(rx, -ry, -rz)",
                    MirrorAxis.Y => "Y Axis (XZ Plane): Pos(x, -y, z) Rot(-rx, ry, -rz)",
                    MirrorAxis.Z => "Z Axis (XY Plane): Pos(x, y, -z) Rot(-rx, -ry, rz)",
                    _ => ""
                };
            }

            axisField.RegisterValueChangedCallback(evt => UpdateRuleLabel((MirrorAxis)evt.newValue));
            UpdateRuleLabel(MirrorAxis.X);

            container.Add(new Separator());

            // Buttons
            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.justifyContent = Justify.Center;
            buttonRow.style.marginTop = 10;
            buttonRow.style.marginBottom = 10;
            container.Add(buttonRow);

            var autoMatchBtn = new Button(AutoMatchBones) { text = "Auto Match" };
            autoMatchBtn.style.width = 100;
            autoMatchBtn.style.marginRight = 10;
            buttonRow.Add(autoMatchBtn);

            var mirrorBtn = new Button(MirrorAllKeys) { text = "Mirror All Keys" };
            mirrorBtn.style.width = 120;
            mirrorBtn.style.backgroundColor = new Color(0.2f, 0.5f, 0.2f);
            buttonRow.Add(mirrorBtn);

            // Utility Buttons
            var utilRow = new VisualElement();
            utilRow.style.flexDirection = FlexDirection.Row;
            utilRow.style.justifyContent = Justify.Center;
            utilRow.style.marginBottom = 10;
            container.Add(utilRow);

            var clearSourceBtn = new Button(() => ClearSelection(true)) { text = "Clear Source" };
            clearSourceBtn.style.width = 80;
            clearSourceBtn.style.marginRight = 5;
            utilRow.Add(clearSourceBtn);

            var clearTargetBtn = new Button(() => ClearSelection(false)) { text = "Clear Target" };
            clearTargetBtn.style.width = 80;
            utilRow.Add(clearTargetBtn);

            // Status
            _statusLabel = new Label("Ready");
            _statusLabel.style.marginTop = 5;
            _statusLabel.style.color = Color.gray;
            _statusLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            container.Add(_statusLabel);

            // Track property changes
            root.TrackPropertyValue(sourceRootProp, OnSourceRootChanged);
            root.TrackPropertyValue(targetRootProp, OnTargetRootChanged);

            // Bind the entire root to SerializedObject
            root.Bind(_serializedObject);
        }

        #endregion

        #region Initialization

        private static void InitializeReflection()
        {
            if (_animationWindowType != null) return;

            _animationWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.AnimationWindow");
            _animationWindowStateType = typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.AnimationWindowState");

            if (_animationWindowType != null)
            {
                _animationWindowStateProperty = _animationWindowType.GetProperty(
                    "state",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
            }

            if (_animationWindowStateType != null)
            {
                _activeAnimationClipProperty = _animationWindowStateType.GetProperty("activeAnimationClip");
            }
        }

        #endregion

        #region Event Handlers

        private void OnSourceRootChanged(SerializedProperty prop)
        {
            var sourceRoot = prop.objectReferenceValue as GameObject;
            if (sourceRoot != null)
            {
                _tempObject.sourceChain = HierarchyNodeHandler.GetHierarchyNode(sourceRoot);
            }
            else
            {
                _tempObject.sourceChain = null;
            }
            _serializedObject.Update();
            _sourceTreeView?.Rebind(_serializedObject.FindProperty("sourceChain"));
        }

        private void OnTargetRootChanged(SerializedProperty prop)
        {
            var targetRoot = prop.objectReferenceValue as GameObject;
            if (targetRoot != null)
            {
                _tempObject.targetChain = HierarchyNodeHandler.GetHierarchyNode(targetRoot);
            }
            else
            {
                _tempObject.targetChain = null;
            }
            _serializedObject.Update();
            _targetTreeView?.Rebind(_serializedObject.FindProperty("targetChain"));
        }

        #endregion

        #region Animation Window Integration

        private void GetCurrentAnimationClip()
        {
            if (_animationWindowType == null || _animationWindowStateProperty == null || _activeAnimationClipProperty == null)
            {
                SetStatus("Animation window reflection not available", true);
                return;
            }

            var animationWindow = GetWindow(_animationWindowType, false, null, false);
            if (animationWindow == null)
            {
                SetStatus("Animation window not found", true);
                return;
            }

            var state = _animationWindowStateProperty.GetValue(animationWindow);
            if (state == null)
            {
                SetStatus("Animation window state not available", true);
                return;
            }

            var clip = _activeAnimationClipProperty.GetValue(state) as AnimationClip;
            if (clip == null)
            {
                SetStatus("No animation clip selected in Animation window", true);
                return;
            }

            _serializedObject.Update();
            _serializedObject.FindProperty("animationClip").objectReferenceValue = clip;
            _serializedObject.ApplyModifiedProperties();

            SetStatus($"Got clip: {clip.name}");
        }

        #endregion

        #region Bone Name Mirroring

        private static bool TryGetMirrorBoneName(string name, out string mirroredName)
        {
            mirroredName = null;

            foreach (var (left, right) in MirrorPatterns)
            {
                if (name.Contains(left))
                {
                    mirroredName = name.Replace(left, right);
                    return true;
                }
                if (name.Contains(right))
                {
                    mirroredName = name.Replace(right, left);
                    return true;
                }
            }

            return false;
        }

        private static bool IsLeftSideBone(string name)
        {
            foreach (var (left, _) in MirrorPatterns)
            {
                if (name.Contains(left))
                {
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region Auto Match

        private void AutoMatchBones()
        {
            _serializedObject.Update();

            if (_tempObject.sourceChain == null || _tempObject.targetChain == null)
            {
                SetStatus("Please set both source and target armature roots", true);
                return;
            }

            // Clear all selections first
            ClearSelectionRecursive(_tempObject.sourceChain);
            ClearSelectionRecursive(_tempObject.targetChain);

            // Build name-to-node dictionaries
            var sourceNodes = new Dictionary<string, HierarchyNode>();
            var targetNodes = new Dictionary<string, HierarchyNode>();
            BuildNodeDictionary(_tempObject.sourceChain, sourceNodes);
            BuildNodeDictionary(_tempObject.targetChain, targetNodes);

            int matchCount = 0;

            // Find matching pairs
            foreach (var kvp in sourceNodes)
            {
                string sourceName = kvp.Key;
                HierarchyNode sourceNode = kvp.Value;

                // Only select left side bones from source
                if (!IsLeftSideBone(sourceName)) continue;

                if (TryGetMirrorBoneName(sourceName, out string mirroredName))
                {
                    // Check if mirrored bone exists in target
                    if (targetNodes.TryGetValue(mirroredName, out HierarchyNode targetNode))
                    {
                        sourceNode.isSelected = true;
                        targetNode.isSelected = true;
                        matchCount++;
                    }
                }
            }

            _serializedObject.ApplyModifiedProperties();
            _sourceTreeView?.Refresh();
            _targetTreeView?.Refresh();

            SetStatus($"Auto-matched {matchCount} bone pairs");
        }

        private void BuildNodeDictionary(HierarchyNode node, Dictionary<string, HierarchyNode> dict)
        {
            if (node == null || node.nodeObject == null) return;

            string name = node.nodeObject.name;
            if (!dict.ContainsKey(name))
            {
                dict[name] = node;
            }

            if (node.children != null)
            {
                foreach (var child in node.children)
                {
                    BuildNodeDictionary(child, dict);
                }
            }
        }

        private void ClearSelectionRecursive(HierarchyNode node)
        {
            if (node == null) return;
            node.isSelected = false;
            if (node.children != null)
            {
                foreach (var child in node.children)
                {
                    ClearSelectionRecursive(child);
                }
            }
        }

        private void ClearSelection(bool isSource)
        {
            _serializedObject.Update();
            var chain = isSource ? _tempObject.sourceChain : _tempObject.targetChain;
            if (chain != null)
            {
                ClearSelectionRecursive(chain);
            }
            _serializedObject.ApplyModifiedProperties();

            if (isSource)
                _sourceTreeView?.Refresh();
            else
                _targetTreeView?.Refresh();

            SetStatus($"Cleared {(isSource ? "source" : "target")} selection");
        }

        #endregion

        #region Mirror Logic

        private void MirrorAllKeys()
        {
            // Apply UI changes to _tempObject (not Update, which does the opposite)
            _serializedObject.ApplyModifiedProperties();

            // Debug: Display current mirror configuration
            var axisInfo = _tempObject.mirrorAxis switch
            {
                MirrorAxis.X => "X Axis (YZ Plane) - Pos(-x,y,z) Rot(rx,-ry,-rz)",
                MirrorAxis.Y => "Y Axis (XZ Plane) - Pos(x,-y,z) Rot(-rx,ry,-rz)",
                MirrorAxis.Z => "Z Axis (XY Plane) - Pos(x,y,-z) Rot(-rx,-ry,rz)",
                _ => "Unknown"
            };
            Debug.Log($"[BoneKeyMirror] Mirror Config: {axisInfo}");

            var clip = _tempObject.animationClip;
            if (clip == null)
            {
                SetStatus("No animation clip selected", true);
                return;
            }

            if (_tempObject.sourceChain == null || _tempObject.targetChain == null)
            {
                SetStatus("Please set both source and target armature roots", true);
                return;
            }

            // Build path mappings from selected bones
            var boneMapping = BuildBoneMapping();
            if (boneMapping.Count == 0)
            {
                SetStatus("No bone pairs selected. Use Auto Match or manually select bones.", true);
                return;
            }

            Undo.RecordObject(clip, "Mirror Animation Keys");

            int mirroredCount = 0;
            var bindings = AnimationUtility.GetCurveBindings(clip);

            foreach (var (sourcePath, targetPath) in boneMapping)
            {
                var sourceBindings = bindings.Where(b => b.path == sourcePath).ToList();

                foreach (var binding in sourceBindings)
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null) continue;

                    var mirroredCurve = MirrorCurve(curve, binding.propertyName);

                    var targetBinding = new EditorCurveBinding
                    {
                        path = targetPath,
                        type = binding.type,
                        propertyName = binding.propertyName
                    };

                    AnimationUtility.SetEditorCurve(clip, targetBinding, mirroredCurve);
                    mirroredCount++;
                }
            }

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            SetStatus($"Mirrored {mirroredCount} curves across {boneMapping.Count} bone pairs");
        }

        private List<(string sourcePath, string targetPath)> BuildBoneMapping()
        {
            var mapping = new List<(string, string)>();

            if (_tempObject.sourceArmatureRoot == null || _tempObject.targetArmatureRoot == null)
                return mapping;

            var selectedSourcePaths = new List<string>();
            var selectedTargetPaths = new List<string>();

            CollectSelectedPaths(_tempObject.sourceChain, _tempObject.sourceArmatureRoot.transform.parent, selectedSourcePaths);
            CollectSelectedPaths(_tempObject.targetChain, _tempObject.targetArmatureRoot.transform.parent, selectedTargetPaths);

            // Build name to path dictionaries
            var sourceNameToPath = new Dictionary<string, string>();
            var targetNameToPath = new Dictionary<string, string>();

            foreach (var path in selectedSourcePaths)
            {
                string name = path.Split('/').Last();
                if (!sourceNameToPath.ContainsKey(name))
                    sourceNameToPath[name] = path;
            }

            foreach (var path in selectedTargetPaths)
            {
                string name = path.Split('/').Last();
                if (!targetNameToPath.ContainsKey(name))
                    targetNameToPath[name] = path;
            }

            // Match by mirrored names
            foreach (var kvp in sourceNameToPath)
            {
                string sourceName = kvp.Key;
                string sourcePath = kvp.Value;

                if (TryGetMirrorBoneName(sourceName, out string mirroredName))
                {
                    if (targetNameToPath.TryGetValue(mirroredName, out string targetPath))
                    {
                        mapping.Add((sourcePath, targetPath));
                    }
                }
            }

            return mapping;
        }

        private void CollectSelectedPaths(HierarchyNode node, Transform root, List<string> paths)
        {
            if (node == null || node.nodeObject == null) return;

            if (node.isSelected)
            {
                string path = GetRelativePath(root, node.nodeObject.transform);
                if (!string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }

            if (node.children != null)
            {
                foreach (var child in node.children)
                {
                    CollectSelectedPaths(child, root, paths);
                }
            }
        }

        private string GetRelativePath(Transform root, Transform target)
        {
            if (root == target) return "";

            var path = target.name;
            var current = target.parent;

            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        private AnimationCurve MirrorCurve(AnimationCurve original, string propertyName)
        {
            var mirrored = new AnimationCurve();

            bool shouldNegate = ShouldNegateProperty(propertyName);

            foreach (var key in original.keys)
            {
                var newKey = new Keyframe(
                    key.time,
                    shouldNegate ? -key.value : key.value,
                    shouldNegate ? -key.inTangent : key.inTangent,
                    shouldNegate ? -key.outTangent : key.outTangent,
                    key.inWeight,
                    key.outWeight
                );
                newKey.weightedMode = key.weightedMode;
                mirrored.AddKey(newKey);
            }

            return mirrored;
        }

        /// <summary>
        /// Determines if a property should be negated based on the Normal Axis Rule.
        /// Position: Invert the normal axis, keep others.
        /// Rotation: Keep the normal axis, invert others.
        /// Scale: Never invert (copy as-is).
        /// </summary>
        private bool ShouldNegateProperty(string propertyName)
        {
            var axis = _tempObject.mirrorAxis;

            // Check Position properties - Invert only the normal axis
            if (TryGetPropertyAxis(propertyName, "Position", out char posAxis))
            {
                // Position: Invert N, Keep Others
                return axis switch
                {
                    MirrorAxis.X => posAxis == 'x',
                    MirrorAxis.Y => posAxis == 'y',
                    MirrorAxis.Z => posAxis == 'z',
                    _ => false
                };
            }

            // Check Rotation properties (Euler or Quaternion) - Invert non-normal axes
            if (TryGetPropertyAxis(propertyName, "Rotation", out char rotAxis) ||
                TryGetPropertyAxis(propertyName, "EulerAngles", out rotAxis))
            {
                // Rotation: Keep N, Invert Others
                return axis switch
                {
                    MirrorAxis.X => rotAxis != 'x',
                    MirrorAxis.Y => rotAxis != 'y',
                    MirrorAxis.Z => rotAxis != 'z',
                    _ => false
                };
            }

            // Scale: Never invert (copy as-is)
            return false;
        }

        /// <summary>
        /// Tries to extract the axis (x, y, z) from a property name.
        /// </summary>
        private bool TryGetPropertyAxis(string propertyName, string propertyType, out char axis)
        {
            axis = '\0';

            // Check for property type match (case-insensitive contains)
            if (!propertyName.ToLowerInvariant().Contains(propertyType.ToLowerInvariant()))
                return false;

            // Extract the axis from the end of the property name (.x, .y, .z)
            if (propertyName.EndsWith(".x")) { axis = 'x'; return true; }
            if (propertyName.EndsWith(".y")) { axis = 'y'; return true; }
            if (propertyName.EndsWith(".z")) { axis = 'z'; return true; }

            return false;
        }

        #endregion

        #region Status

        private void SetStatus(string message, bool isError = false)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = message;
                _statusLabel.style.color = isError ? new Color(0.9f, 0.3f, 0.3f) : Color.gray;
            }
            Debug.Log($"[BoneKeyMirror] {message}");
        }

        #endregion

        #region Data Container

        /// <summary>
        /// Mirror axis representing the normal of the mirror plane.
        /// X = YZ Plane, Y = XZ Plane, Z = XY Plane
        /// </summary>
        public enum MirrorAxis { X, Y, Z }

        [Serializable]
        private class TempContainer : ScriptableObject
        {
            public AnimationClip animationClip;
            public GameObject sourceArmatureRoot;
            public GameObject targetArmatureRoot;
            public HierarchyNode sourceChain;
            public HierarchyNode targetChain;

            // Mirror axis (Normal Axis Rule)
            public MirrorAxis mirrorAxis = MirrorAxis.X;
        }

        #endregion
    }
}
