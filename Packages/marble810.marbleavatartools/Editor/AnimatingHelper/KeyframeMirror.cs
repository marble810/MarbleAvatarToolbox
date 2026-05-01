using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using marble810.MarbleAvatarTools.Utils;
using marble810.MarbleAvatarTools.CustomGUI;
using UnityEditor.UIElements;




namespace marble810.MarbleAvatarTools.AnimatingHelper
{
    public class KeyframeMirror : EditorWindow
    {
        #region Fields

        private SerializedObject _serializedObject;
        private TempContainer _tempObject;
        private HierarchyNodeView _sourceTreeView;
        private HierarchyNodeView _targetTreeView;
        private Label _statusLabel;
        private ObjectField _clipField;
        private Label _referenceRootLabel;

        // Animation Window reflection
        private static Type _animationWindowType;
        private static Type _animationWindowStateType;
        private static Type _animationWindowCurveType;
        private static Type _animationWindowKeyframeType;
        private static PropertyInfo _animationWindowStateProperty;
        private static PropertyInfo _activeAnimationClipProperty;
        private static PropertyInfo _activeRootGameObjectProperty;
        private static PropertyInfo _selectedKeysProperty;
        private static PropertyInfo _animationWindowCurveBindingProperty;
        private static PropertyInfo _animationWindowCurvePropertyNameProperty;
        private static PropertyInfo _animationWindowKeyframeCurveProperty;
        private static PropertyInfo _animationWindowKeyframeTimeProperty;
        private static PropertyInfo _animationWindowKeyframeValueProperty;
        private static PropertyInfo _animationWindowKeyframeInTangentProperty;
        private static PropertyInfo _animationWindowKeyframeOutTangentProperty;
        private static FieldInfo _animEditorField;
        private static MethodInfo _saveCurveEditorKeySelectionMethod;
        private static MethodInfo _clearKeySelectionsMethod;
        private static MethodInfo _selectKeyMethod;
        private static MethodInfo _saveCurvesMethod;

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

        [MenuItem("MarbleAvatarTools/Keyframe Mirror")]
        public static void ShowWindow()
        {
            var window = GetWindow<KeyframeMirror>("Keyframe Mirror");
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
            EditorApplication.update += RefreshAnimationWindowContextDisplay;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= RefreshAnimationWindowContextDisplay;
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

            var mainTitle = new Label("Keyframe Mirror");
            mainTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            mainTitle.style.fontSize = 14;
            mainTitle.style.alignSelf = Align.Center;
            mainTitle.style.marginBottom = 10;
            container.Add(mainTitle);

            var contextSection = CreateSection(container, "Animation Context");
            var clipRow = new VisualElement();
            clipRow.style.flexDirection = FlexDirection.Row;
            contextSection.Add(clipRow);

            _clipField = new ObjectField();
            _clipField.objectType = typeof(AnimationClip);
            _clipField.bindingPath = "animationClip";
            _clipField.style.flexGrow = 1;
            clipRow.Add(_clipField);

            var getClipBtn = new Button(GetCurrentAnimationClip) { text = "Get Current" };
            getClipBtn.style.width = 80;
            clipRow.Add(getClipBtn);

            var referenceRootRow = new VisualElement();
            referenceRootRow.style.flexDirection = FlexDirection.Row;
            referenceRootRow.style.marginTop = 4;
            contextSection.Add(referenceRootRow);

            var referenceRootTitle = new Label("Reference Root");
            referenceRootTitle.style.minWidth = 95;
            referenceRootRow.Add(referenceRootTitle);

            _referenceRootLabel = new Label("Unavailable");
            _referenceRootLabel.style.flexGrow = 1;
            _referenceRootLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            _referenceRootLabel.style.color = new Color(0.8f, 0.45f, 0.45f);
            referenceRootRow.Add(_referenceRootLabel);

            container.Add(new Separator());

            var settingsSection = CreateSection(container, "Mirror Settings");
            var axisField = new EnumField("Mirror Axis", _tempObject.mirrorAxis);
            axisField.bindingPath = "mirrorAxis";
            settingsSection.Add(axisField);

            var ruleLabel = new Label();
            ruleLabel.style.marginTop = 5;
            ruleLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            ruleLabel.style.fontSize = 11;
            settingsSection.Add(ruleLabel);

            void UpdateRuleLabel(MirrorAxis axis)
            {
                ruleLabel.text = axis switch
                {
                    MirrorAxis.X => "X Axis: Reflect against the reference root's local YZ plane.",
                    MirrorAxis.Y => "Y Axis: Reflect against the reference root's local XZ plane.",
                    MirrorAxis.Z => "Z Axis: Reflect against the reference root's local XY plane.",
                    _ => ""
                };
            }

            axisField.RegisterValueChangedCallback(evt => UpdateRuleLabel((MirrorAxis)evt.newValue));
            UpdateRuleLabel(_tempObject.mirrorAxis);

            container.Add(new Separator());

            var keySymmetrySection = CreateSection(container, "Key Symmetry");
            var sourceLabel = new Label("Source Bones (Copy From)");
            sourceLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            sourceLabel.style.marginTop = 5;
            keySymmetrySection.Add(sourceLabel);

            var sourceRootProp = _serializedObject.FindProperty("sourceArmatureRoot");
            var sourceRootField = new PropertyField(sourceRootProp, "Armature Root");
            keySymmetrySection.Add(sourceRootField);

            _sourceTreeView = new HierarchyNodeView(
                _serializedObject.FindProperty("sourceChain"),
                null,
                showToggle: true,
                autoRefresh: true
            );
            keySymmetrySection.Add(_sourceTreeView);

            keySymmetrySection.Add(new Separator());

            var targetLabel = new Label("Target Bones (Copy To)");
            targetLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            targetLabel.style.marginTop = 5;
            keySymmetrySection.Add(targetLabel);

            var targetRootProp = _serializedObject.FindProperty("targetArmatureRoot");
            var targetRootField = new PropertyField(targetRootProp, "Armature Root");
            keySymmetrySection.Add(targetRootField);

            _targetTreeView = new HierarchyNodeView(
                _serializedObject.FindProperty("targetChain"),
                null,
                showToggle: true,
                autoRefresh: true
            );
            keySymmetrySection.Add(_targetTreeView);

            keySymmetrySection.Add(new Separator());

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.justifyContent = Justify.Center;
            buttonRow.style.marginTop = 10;
            buttonRow.style.marginBottom = 10;
            keySymmetrySection.Add(buttonRow);

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
            keySymmetrySection.Add(utilRow);

            var clearSourceBtn = new Button(() => ClearSelection(true)) { text = "Clear Source" };
            clearSourceBtn.style.width = 80;
            clearSourceBtn.style.marginRight = 5;
            utilRow.Add(clearSourceBtn);

            var clearTargetBtn = new Button(() => ClearSelection(false)) { text = "Clear Target" };
            clearTargetBtn.style.width = 80;
            utilRow.Add(clearTargetBtn);

            container.Add(new Separator());

            var mirrorKeyframesSection = CreateSection(container, "Mirror Keyframes");
            var mirrorKeyframesHelp = new Label("Aggregates selected Transform channels by Transform + time, mirrors the full pose in the Animation Window reference root local space, and writes the mirrored result back to the same Transform.");
            mirrorKeyframesHelp.style.whiteSpace = WhiteSpace.Normal;
            mirrorKeyframesHelp.style.color = new Color(0.6f, 0.6f, 0.6f);
            mirrorKeyframesHelp.style.marginBottom = 8;
            mirrorKeyframesSection.Add(mirrorKeyframesHelp);

            var mirrorSelectedButton = new Button(MirrorSelectedKeyframes) { text = "Mirror Selected Keyframes" };
            mirrorSelectedButton.style.width = 190;
            mirrorSelectedButton.style.alignSelf = Align.Center;
            mirrorSelectedButton.style.backgroundColor = new Color(0.2f, 0.35f, 0.6f);
            mirrorKeyframesSection.Add(mirrorSelectedButton);

            container.Add(new Separator());

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
            RefreshAnimationWindowContextDisplay();
        }

        private static VisualElement CreateSection(VisualElement parent, string title)
        {
            var section = new VisualElement();
            section.style.marginBottom = 10;
            parent.Add(section);

            var header = new Label(title);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.fontSize = 13;
            header.style.marginBottom = 5;
            section.Add(header);

            return section;
        }

        #endregion

        #region Initialization

        private static void InitializeReflection()
        {
            if (_animationWindowType != null) return;

            var bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            _animationWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.AnimationWindow");
            _animationWindowStateType = typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.AnimationWindowState");
            _animationWindowCurveType = typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.AnimationWindowCurve");
            _animationWindowKeyframeType = typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.AnimationWindowKeyframe");
            var animEditorType = typeof(EditorWindow).Assembly.GetType("UnityEditor.AnimEditor");

            if (_animationWindowType != null)
            {
                _animationWindowStateProperty = _animationWindowType.GetProperty(
                    "state",
                    bindingFlags
                );
            }

            if (_animationWindowStateType != null)
            {
                _activeAnimationClipProperty = _animationWindowStateType.GetProperty("activeAnimationClip", bindingFlags);
                _activeRootGameObjectProperty = _animationWindowStateType.GetProperty("activeRootGameObject", bindingFlags);
                _selectedKeysProperty = _animationWindowStateType.GetProperty("selectedKeys", bindingFlags);
                _animEditorField = _animationWindowStateType.GetField("animEditor", bindingFlags);
                _clearKeySelectionsMethod = _animationWindowStateType.GetMethod("ClearKeySelections", bindingFlags);
                _saveCurvesMethod = _animationWindowStateType.GetMethods(bindingFlags)
                    .FirstOrDefault(method => method.Name == "SaveCurves" && method.GetParameters().Length == 3);

                if (_animationWindowKeyframeType != null)
                {
                    _selectKeyMethod = _animationWindowStateType.GetMethod(
                        "SelectKey",
                        bindingFlags,
                        null,
                        new[] { _animationWindowKeyframeType },
                        null
                    );
                }
            }

            if (animEditorType != null)
            {
                _saveCurveEditorKeySelectionMethod = animEditorType.GetMethod("SaveCurveEditorKeySelection", bindingFlags);
            }

            if (_animationWindowCurveType != null)
            {
                _animationWindowCurveBindingProperty = _animationWindowCurveType.GetProperty("binding", bindingFlags);
                _animationWindowCurvePropertyNameProperty = _animationWindowCurveType.GetProperty("propertyName", bindingFlags);
            }

            if (_animationWindowKeyframeType != null)
            {
                _animationWindowKeyframeCurveProperty = _animationWindowKeyframeType.GetProperty("curve", bindingFlags);
                _animationWindowKeyframeTimeProperty = _animationWindowKeyframeType.GetProperty("time", bindingFlags);
                _animationWindowKeyframeValueProperty = _animationWindowKeyframeType.GetProperty("value", bindingFlags);
                _animationWindowKeyframeInTangentProperty = _animationWindowKeyframeType.GetProperty("inTangent", bindingFlags);
                _animationWindowKeyframeOutTangentProperty = _animationWindowKeyframeType.GetProperty("outTangent", bindingFlags);
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
            if (!TryGetAnimationWindowContext(out _, out var state, out _))
            {
                return;
            }

            var clip = _activeAnimationClipProperty?.GetValue(state) as AnimationClip;
            if (clip == null)
            {
                SetStatus("No animation clip selected in Animation window", true);
                return;
            }

            _serializedObject.Update();
            _serializedObject.FindProperty("animationClip").objectReferenceValue = clip;
            _serializedObject.ApplyModifiedProperties();

            RefreshAnimationWindowContextDisplay();

            SetStatus($"Got clip: {clip.name}");
        }

        private void RefreshAnimationWindowContextDisplay()
        {
            if (_referenceRootLabel == null)
            {
                return;
            }

            if (!TryGetAnimationWindowContext(out _, out var animationWindowState, out _, false))
            {
                _referenceRootLabel.text = "Unavailable";
                _referenceRootLabel.style.color = new Color(0.8f, 0.45f, 0.45f);
                return;
            }

            var activeRoot = _activeRootGameObjectProperty?.GetValue(animationWindowState) as GameObject;
            if (activeRoot == null)
            {
                _referenceRootLabel.text = "Unavailable";
                _referenceRootLabel.style.color = new Color(0.8f, 0.45f, 0.45f);
                return;
            }

            _referenceRootLabel.text = activeRoot.name;
            _referenceRootLabel.style.color = Color.gray;
        }

        private bool TryGetAnimationWindowContext(out EditorWindow animationWindow, out object animationWindowState, out object animEditor, bool reportErrors = true)
        {
            animationWindow = null;
            animationWindowState = null;
            animEditor = null;

            if (_animationWindowType == null || _animationWindowStateProperty == null)
            {
                if (reportErrors)
                {
                    SetStatus("Animation window reflection not available", true);
                }
                return false;
            }

            animationWindow = FindOpenAnimationWindow();
            if (animationWindow == null)
            {
                if (reportErrors)
                {
                    SetStatus("Animation window not found", true);
                }
                return false;
            }

            animationWindowState = _animationWindowStateProperty.GetValue(animationWindow);
            if (animationWindowState == null)
            {
                if (reportErrors)
                {
                    SetStatus("Animation window state not available", true);
                }
                return false;
            }

            animEditor = _animEditorField?.GetValue(animationWindowState);
            return true;
        }

        private bool TryGetActiveReferenceRoot(out GameObject activeRoot, bool reportErrors = true)
        {
            activeRoot = null;

            if (!TryGetAnimationWindowContext(out _, out var animationWindowState, out _, reportErrors))
            {
                return false;
            }

            activeRoot = _activeRootGameObjectProperty?.GetValue(animationWindowState) as GameObject;
            if (activeRoot == null && reportErrors)
            {
                SetStatus("Mirror reference root is unavailable", true);
            }

            return activeRoot != null;
        }

        private static EditorWindow FindOpenAnimationWindow()
        {
            if (_animationWindowType == null) return null;

            return Resources.FindObjectsOfTypeAll(_animationWindowType)
                .OfType<EditorWindow>()
                .FirstOrDefault();
        }

        private void SyncAnimationWindowKeySelection(object animEditor)
        {
            if (animEditor == null || _saveCurveEditorKeySelectionMethod == null) return;

            try
            {
                _saveCurveEditorKeySelectionMethod.Invoke(animEditor, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"KeyframeMirror: Failed to sync Animation Window selection: {e.Message}");
            }
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

        private const float TimeQuantizationScale = 10000f;
        private const float TimeComparisonEpsilon = 0.0001f;
        private const string DefaultPositionXProperty = "m_LocalPosition.x";
        private const string DefaultPositionYProperty = "m_LocalPosition.y";
        private const string DefaultPositionZProperty = "m_LocalPosition.z";
        private const string DefaultEulerXProperty = "localEulerAnglesRaw.x";
        private const string DefaultEulerYProperty = "localEulerAnglesRaw.y";
        private const string DefaultEulerZProperty = "localEulerAnglesRaw.z";
        private const string DefaultQuaternionXProperty = "m_LocalRotation.x";
        private const string DefaultQuaternionYProperty = "m_LocalRotation.y";
        private const string DefaultQuaternionZProperty = "m_LocalRotation.z";
        private const string DefaultQuaternionWProperty = "m_LocalRotation.w";

        private enum RotationRepresentation
        {
            None,
            Euler,
            Quaternion
        }

        private enum SupportedTransformBinding
        {
            None,
            PositionX,
            PositionY,
            PositionZ,
            EulerX,
            EulerY,
            EulerZ,
            QuaternionX,
            QuaternionY,
            QuaternionZ,
            QuaternionW
        }

        private sealed class TransformCurveSet
        {
            public string Path;
            public Transform SceneTransform;
            public EditorCurveBinding? PositionXBinding;
            public EditorCurveBinding? PositionYBinding;
            public EditorCurveBinding? PositionZBinding;
            public EditorCurveBinding? EulerXBinding;
            public EditorCurveBinding? EulerYBinding;
            public EditorCurveBinding? EulerZBinding;
            public EditorCurveBinding? QuaternionXBinding;
            public EditorCurveBinding? QuaternionYBinding;
            public EditorCurveBinding? QuaternionZBinding;
            public EditorCurveBinding? QuaternionWBinding;

            public bool HasPositionBindings
            {
                get
                {
                    return PositionXBinding.HasValue || PositionYBinding.HasValue || PositionZBinding.HasValue;
                }
            }

            public RotationRepresentation RotationMode
            {
                get
                {
                    if (QuaternionXBinding.HasValue || QuaternionYBinding.HasValue || QuaternionZBinding.HasValue || QuaternionWBinding.HasValue)
                    {
                        return RotationRepresentation.Quaternion;
                    }

                    if (EulerXBinding.HasValue || EulerYBinding.HasValue || EulerZBinding.HasValue)
                    {
                        return RotationRepresentation.Euler;
                    }

                    return RotationRepresentation.None;
                }
            }

            public IEnumerable<EditorCurveBinding> EnumerateExistingBindings()
            {
                if (PositionXBinding.HasValue) yield return PositionXBinding.Value;
                if (PositionYBinding.HasValue) yield return PositionYBinding.Value;
                if (PositionZBinding.HasValue) yield return PositionZBinding.Value;
                if (EulerXBinding.HasValue) yield return EulerXBinding.Value;
                if (EulerYBinding.HasValue) yield return EulerYBinding.Value;
                if (EulerZBinding.HasValue) yield return EulerZBinding.Value;
                if (QuaternionXBinding.HasValue) yield return QuaternionXBinding.Value;
                if (QuaternionYBinding.HasValue) yield return QuaternionYBinding.Value;
                if (QuaternionZBinding.HasValue) yield return QuaternionZBinding.Value;
                if (QuaternionWBinding.HasValue) yield return QuaternionWBinding.Value;
            }
        }

        private sealed class TransformEvaluationContext
        {
            public AnimationClip Clip;
            public Transform ReferenceRoot;
            public readonly Dictionary<string, TransformCurveSet> CurveSets = new Dictionary<string, TransformCurveSet>(StringComparer.Ordinal);
            public readonly Dictionary<string, Transform> SceneTransforms = new Dictionary<string, Transform>(StringComparer.Ordinal);
            public readonly Dictionary<EditorCurveBinding, AnimationCurve> CurveCache = new Dictionary<EditorCurveBinding, AnimationCurve>();
            public readonly Dictionary<QuantizedPathKey, LocalPoseSample> LocalPoseCache = new Dictionary<QuantizedPathKey, LocalPoseSample>();
            public readonly Dictionary<QuantizedPathKey, ReferencePoseSample> ReferencePoseCache = new Dictionary<QuantizedPathKey, ReferencePoseSample>();
        }

        private sealed class TransformSampleOperation
        {
            public string SourcePath;
            public string TargetPath;
            public float Time;
            public RotationRepresentation PreferredRotationRepresentation;
        }

        private struct LocalPoseSample
        {
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
        }

        private struct ReferencePoseSample
        {
            public Vector3 Position;
            public Quaternion Rotation;

            public static ReferencePoseSample Identity
            {
                get
                {
                    return new ReferencePoseSample
                    {
                        Position = Vector3.zero,
                        Rotation = Quaternion.identity
                    };
                }
            }
        }

        private struct QuantizedPathKey : IEquatable<QuantizedPathKey>
        {
            public readonly string Path;
            public readonly int QuantizedTime;

            public QuantizedPathKey(string path, float time)
            {
                Path = path ?? string.Empty;
                QuantizedTime = QuantizeTime(time);
            }

            public bool Equals(QuantizedPathKey other)
            {
                return QuantizedTime == other.QuantizedTime &&
                    string.Equals(Path, other.Path, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is QuantizedPathKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Path != null ? Path.GetHashCode() : 0) * 397) ^ QuantizedTime;
                }
            }
        }

        private struct KeyframeMetadata
        {
            public bool Broken;
            public AnimationUtility.TangentMode LeftTangentMode;
            public AnimationUtility.TangentMode RightTangentMode;
        }

        private struct MirrorDebugSample
        {
            public string Path;
            public float Time;
            public LocalPoseSample LocalPose;
            public ReferencePoseSample ParentReferencePose;
            public ReferencePoseSample ReferencePose;
        }

        private sealed class PlannedMirrorOperation
        {
            public TransformSampleOperation Operation;
            public LocalPoseSample MirroredPose;
            public MirrorDebugSample BeforeDebug;
            public MirrorDebugSample MirroredDebug;
        }

        private void MirrorSelectedKeyframes()
        {
            if (!TryGetAnimationWindowContext(out var animationWindow, out var animationWindowState, out var animEditor))
            {
                return;
            }

            if (_selectedKeysProperty == null ||
                _animationWindowKeyframeCurveProperty == null ||
                _animationWindowCurveBindingProperty == null ||
                _animationWindowKeyframeTimeProperty == null)
            {
                SetStatus("Animation key reflection is not available", true);
                return;
            }

            if (!TryGetActiveReferenceRoot(out var activeRoot))
            {
                return;
            }

            SyncAnimationWindowKeySelection(animEditor);

            var clip = _activeAnimationClipProperty?.GetValue(animationWindowState) as AnimationClip;
            if (clip == null)
            {
                SetStatus("No animation clip selected in Animation window", true);
                return;
            }

            var selectedKeysValue = _selectedKeysProperty.GetValue(animationWindowState) as IEnumerable;
            if (selectedKeysValue == null)
            {
                SetStatus("Animation key selection is not available", true);
                return;
            }

            var selectedKeys = selectedKeysValue.Cast<object>().Where(key => key != null).ToList();
            if (selectedKeys.Count == 0)
            {
                SetStatus("No keyframes selected in Animation window", true);
                return;
            }

            var evaluationContext = BuildEvaluationContext(clip, activeRoot.transform);
            var operations = AggregateSelectedTransformOperations(selectedKeys, evaluationContext);
            if (operations.Count == 0)
            {
                SetStatus("No selected keyframes belong to supported Transform position or rotation curves", true);
                return;
            }

            var orderedOperations = operations.Values
                .OrderBy(entry => entry.Time)
                .ThenBy(entry => GetPathDepth(entry.TargetPath))
                .ThenBy(entry => entry.TargetPath, StringComparer.Ordinal)
                .ToList();

            var mirroredReferencePoses = BuildMirroredReferencePoseMap(evaluationContext, orderedOperations);
            var plannedOperations = new List<PlannedMirrorOperation>();
            int debugIndex = 0;

            foreach (var operation in orderedOperations)
            {
                var beforeDebug = CaptureMirrorDebugSample(evaluationContext, operation.SourcePath, operation.Time);

                if (!TryPlanMirrorLocalPose(evaluationContext, operation, mirroredReferencePoses, out var mirroredPose, out var targetParentReferencePose))
                {
                    LogMirrorDebugSample(clip, activeRoot, operation, beforeDebug, null, null, debugIndex, false, "Mirror planning failed");
                    debugIndex++;
                    continue;
                }

                var mirroredDebug = CaptureMirrorDebugSample(evaluationContext, operation.TargetPath, operation.Time, mirroredPose, targetParentReferencePose);
                plannedOperations.Add(new PlannedMirrorOperation
                {
                    Operation = operation,
                    MirroredPose = mirroredPose,
                    BeforeDebug = beforeDebug,
                    MirroredDebug = mirroredDebug
                });
            }

            if (plannedOperations.Count == 0)
            {
                SetStatus("Selected keyframes produced no mirror plan", true);
                return;
            }

            Undo.RecordObject(clip, "Mirror Selected Keyframes");

            var changedBindings = new HashSet<EditorCurveBinding>();
            int mirroredSamples = 0;
            debugIndex = 0;

            foreach (var plannedOperation in plannedOperations)
            {
                var operation = plannedOperation.Operation;
                if (WriteTransformSample(evaluationContext, operation.TargetPath, operation.Time, plannedOperation.MirroredPose, operation.PreferredRotationRepresentation, changedBindings))
                {
                    mirroredSamples++;
                    var afterDebug = CaptureMirrorDebugSample(evaluationContext, operation.TargetPath, operation.Time);
                    LogMirrorDebugSample(clip, activeRoot, operation, plannedOperation.BeforeDebug, plannedOperation.MirroredDebug, afterDebug, debugIndex, true, null);
                }
                else
                {
                    LogMirrorDebugSample(clip, activeRoot, operation, plannedOperation.BeforeDebug, plannedOperation.MirroredDebug, null, debugIndex, false, "WriteTransformSample produced no curve change");
                }

                debugIndex++;
            }

            if (changedBindings.Count == 0)
            {
                SetStatus("Selected keyframes produced no mirrored transform changes", true);
                return;
            }

            CommitCurveChanges(clip, evaluationContext, changedBindings);
            _clearKeySelectionsMethod?.Invoke(animationWindowState, null);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            animationWindow.Repaint();

            SetStatus($"Mirrored {mirroredSamples} transform samples in reference root {activeRoot.name}");
        }

        private Dictionary<QuantizedPathKey, ReferencePoseSample> BuildMirroredReferencePoseMap(
            TransformEvaluationContext evaluationContext,
            IEnumerable<TransformSampleOperation> operations)
        {
            var mirroredReferencePoses = new Dictionary<QuantizedPathKey, ReferencePoseSample>();

            foreach (var operation in operations)
            {
                var sourceReferencePose = EvaluateReferencePose(evaluationContext, operation.SourcePath, operation.Time);
                mirroredReferencePoses[new QuantizedPathKey(operation.TargetPath, operation.Time)] = new ReferencePoseSample
                {
                    Position = ReflectVector(sourceReferencePose.Position, _tempObject.mirrorAxis),
                    Rotation = ReflectRotation(sourceReferencePose.Rotation, _tempObject.mirrorAxis)
                };
            }

            return mirroredReferencePoses;
        }

        private bool TryPlanMirrorLocalPose(
            TransformEvaluationContext evaluationContext,
            TransformSampleOperation operation,
            Dictionary<QuantizedPathKey, ReferencePoseSample> mirroredReferencePoses,
            out LocalPoseSample mirroredPose,
            out ReferencePoseSample targetParentReferencePose)
        {
            mirroredPose = default;
            targetParentReferencePose = default;

            var sourceCurveSet = EnsureCurveSet(evaluationContext, operation.SourcePath);
            var targetCurveSet = EnsureCurveSet(evaluationContext, operation.TargetPath);
            if (sourceCurveSet == null || targetCurveSet == null ||
                sourceCurveSet.SceneTransform == null || targetCurveSet.SceneTransform == null)
            {
                return false;
            }

            if (!mirroredReferencePoses.TryGetValue(new QuantizedPathKey(operation.TargetPath, operation.Time), out var mirroredReferencePose))
            {
                return false;
            }

            targetParentReferencePose = EvaluatePlannedReferencePose(
                evaluationContext,
                GetParentPath(operation.TargetPath),
                operation.Time,
                mirroredReferencePoses);

            mirroredPose.LocalPosition = Quaternion.Inverse(targetParentReferencePose.Rotation) * (mirroredReferencePose.Position - targetParentReferencePose.Position);
            mirroredPose.LocalRotation = NormalizeQuaternion(Quaternion.Inverse(targetParentReferencePose.Rotation) * mirroredReferencePose.Rotation);
            return true;
        }

        private ReferencePoseSample EvaluatePlannedReferencePose(
            TransformEvaluationContext evaluationContext,
            string path,
            float time,
            Dictionary<QuantizedPathKey, ReferencePoseSample> mirroredReferencePoses)
        {
            path = path ?? string.Empty;

            if (mirroredReferencePoses.TryGetValue(new QuantizedPathKey(path, time), out var mirroredReferencePose))
            {
                return mirroredReferencePose;
            }

            if (string.IsNullOrEmpty(path))
            {
                return ReferencePoseSample.Identity;
            }

            var parentPose = EvaluatePlannedReferencePose(evaluationContext, GetParentPath(path), time, mirroredReferencePoses);
            var localPose = EvaluateLocalPose(evaluationContext, path, time);
            return new ReferencePoseSample
            {
                Position = parentPose.Position + parentPose.Rotation * localPose.LocalPosition,
                Rotation = NormalizeQuaternion(parentPose.Rotation * localPose.LocalRotation)
            };
        }

        private MirrorDebugSample CaptureMirrorDebugSample(
            TransformEvaluationContext evaluationContext,
            string path,
            float time)
        {
            return CaptureMirrorDebugSample(evaluationContext, path, time, EvaluateLocalPose(evaluationContext, path, time));
        }

        private MirrorDebugSample CaptureMirrorDebugSample(
            TransformEvaluationContext evaluationContext,
            string path,
            float time,
            LocalPoseSample localPose)
        {
            var parentReferencePose = EvaluateReferencePose(evaluationContext, GetParentPath(path), time);
            return CaptureMirrorDebugSample(evaluationContext, path, time, localPose, parentReferencePose);
        }

        private MirrorDebugSample CaptureMirrorDebugSample(
            TransformEvaluationContext evaluationContext,
            string path,
            float time,
            LocalPoseSample localPose,
            ReferencePoseSample parentReferencePose)
        {
            return new MirrorDebugSample
            {
                Path = path ?? string.Empty,
                Time = time,
                LocalPose = localPose,
                ParentReferencePose = parentReferencePose,
                ReferencePose = new ReferencePoseSample
                {
                    Position = parentReferencePose.Position + parentReferencePose.Rotation * localPose.LocalPosition,
                    Rotation = NormalizeQuaternion(parentReferencePose.Rotation * localPose.LocalRotation)
                }
            };
        }

        private void LogMirrorDebugSample(
            AnimationClip clip,
            GameObject activeRoot,
            TransformSampleOperation operation,
            MirrorDebugSample? before,
            MirrorDebugSample? mirrored,
            MirrorDebugSample? after,
            int sampleIndex,
            bool changed,
            string reason)
        {
            var result = changed ? "changed" : "unchanged";
            var reasonText = string.IsNullOrEmpty(reason) ? string.Empty : $" reason=\"{reason}\"";
            Debug.Log(
                $"[KeyframeMirror.Debug] sample={sampleIndex} result={result}{reasonText}\n" +
                $"clip=\"{(clip != null ? clip.name : "<null>")}\" root=\"{(activeRoot != null ? activeRoot.name : "<null>")}\" " +
                $"axis={_tempObject.mirrorAxis} time={operation.Time:F6} source=\"{operation.SourcePath}\" target=\"{operation.TargetPath}\" rotationMode={operation.PreferredRotationRepresentation}\n" +
                FormatMirrorDebugSample("before", before) + "\n" +
                FormatMirrorDebugSample("mirrored", mirrored) + "\n" +
                FormatMirrorDebugSample("after", after));
        }

        private static string FormatMirrorDebugSample(string label, MirrorDebugSample? sample)
        {
            if (!sample.HasValue)
            {
                return $"{label}=<none>";
            }

            var value = sample.Value;
            return
                $"{label}.path=\"{value.Path}\" {label}.time={value.Time:F6} " +
                $"{label}.localPos={FormatVector(value.LocalPose.LocalPosition)} " +
                $"{label}.localEuler={FormatVector(value.LocalPose.LocalRotation.eulerAngles)} " +
                $"{label}.localQuat={FormatQuaternion(value.LocalPose.LocalRotation)} " +
                $"{label}.parentRefPos={FormatVector(value.ParentReferencePose.Position)} " +
                $"{label}.parentRefEuler={FormatVector(value.ParentReferencePose.Rotation.eulerAngles)} " +
                $"{label}.refPos={FormatVector(value.ReferencePose.Position)} " +
                $"{label}.refEuler={FormatVector(value.ReferencePose.Rotation.eulerAngles)} " +
                $"{label}.refQuat={FormatQuaternion(value.ReferencePose.Rotation)}";
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({value.x:F6},{value.y:F6},{value.z:F6})";
        }

        private static string FormatQuaternion(Quaternion value)
        {
            return $"({value.x:F6},{value.y:F6},{value.z:F6},{value.w:F6})";
        }

        private void MirrorAllKeys()
        {
            _serializedObject.ApplyModifiedProperties();

            var clip = _tempObject.animationClip;
            if (clip == null)
            {
                SetStatus("No animation clip selected", true);
                return;
            }

            if (_tempObject.sourceChain == null ||
                _tempObject.targetChain == null ||
                _tempObject.sourceArmatureRoot == null ||
                _tempObject.targetArmatureRoot == null)
            {
                SetStatus("Please set both source and target armature roots", true);
                return;
            }

            var commonAncestor = FindCommonAncestor(_tempObject.sourceArmatureRoot.transform, _tempObject.targetArmatureRoot.transform);
            if (commonAncestor == null)
            {
                SetStatus("Source and target armatures do not share a common reference root", true);
                return;
            }

            var referenceRoot = ResolveKeySymmetryReferenceRoot(clip, commonAncestor);
            if (referenceRoot == null)
            {
                SetStatus("Could not resolve a reference root for the animation clip bindings", true);
                return;
            }

            var boneMapping = BuildBoneMapping(referenceRoot);
            if (boneMapping.Count == 0)
            {
                SetStatus("No bone pairs selected. Use Auto Match or manually select bones.", true);
                return;
            }

            var evaluationContext = BuildEvaluationContext(clip, referenceRoot);
            var operations = BuildKeySymmetryOperations(boneMapping, evaluationContext);
            if (operations.Count == 0)
            {
                LogKeySymmetryNoOperationsDebug(clip, referenceRoot, boneMapping, evaluationContext);
                SetStatus("No supported transform keys were found on the selected source bones", true);
                return;
            }

            Undo.RecordObject(clip, "Mirror Animation Keys");

            var changedBindings = new HashSet<EditorCurveBinding>();
            int mirroredSamples = 0;

            foreach (var operation in operations.Values
                .OrderBy(entry => entry.Time)
                .ThenBy(entry => GetPathDepth(entry.TargetPath))
                .ThenBy(entry => entry.TargetPath, StringComparer.Ordinal))
            {
                if (!TryMirrorLocalPose(evaluationContext, operation.SourcePath, operation.TargetPath, operation.Time, out var mirroredPose))
                {
                    continue;
                }

                if (WriteTransformSample(evaluationContext, operation.TargetPath, operation.Time, mirroredPose, operation.PreferredRotationRepresentation, changedBindings))
                {
                    mirroredSamples++;
                }
            }

            if (changedBindings.Count == 0)
            {
                SetStatus("Key Symmetry produced no mirrored transform changes", true);
                return;
            }

            CommitCurveChanges(clip, evaluationContext, changedBindings);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            SetStatus($"Mirrored {mirroredSamples} transform samples across {boneMapping.Count} mapped bone pairs");
        }

        private void LogKeySymmetryNoOperationsDebug(
            AnimationClip clip,
            Transform referenceRoot,
            List<(string sourcePath, string targetPath)> boneMapping,
            TransformEvaluationContext evaluationContext)
        {
            var transformCurvePaths = AnimationUtility.GetCurveBindings(clip)
                .Where(binding => GetSupportedTransformBinding(binding) != SupportedTransformBinding.None)
                .Select(binding => binding.path ?? string.Empty)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            var sourceRootPath = GetRelativePath(referenceRoot, _tempObject.sourceArmatureRoot.transform) ?? "<outside-reference-root>";
            var targetRootPath = GetRelativePath(referenceRoot, _tempObject.targetArmatureRoot.transform) ?? "<outside-reference-root>";
            var mappingText = string.Join(" ; ", boneMapping.Take(40).Select(pair => FormatKeySymmetryMappingDebug(pair, evaluationContext, transformCurvePaths)));
            var clipPathText = string.Join(" ; ", transformCurvePaths.Take(80));

            Debug.Log(
                $"[KeyframeMirror.KeySymmetryDebug] reason=NoSupportedTransformKeys clip=\"{(clip != null ? clip.name : "<null>")}\" clipAsset=\"{(clip != null ? AssetDatabase.GetAssetPath(clip) : "<null>")}\" " +
                $"referenceRoot=\"{(referenceRoot != null ? referenceRoot.name : "<null>")}\" sourceRootPath=\"{sourceRootPath}\" targetRootPath=\"{targetRootPath}\" " +
                $"mappingCount={boneMapping.Count} transformCurvePathCount={transformCurvePaths.Count} mappings=[{mappingText}] clipTransformPaths=[{clipPathText}]");
        }

        private string FormatKeySymmetryMappingDebug(
            (string sourcePath, string targetPath) pair,
            TransformEvaluationContext evaluationContext,
            List<string> transformCurvePaths)
        {
            var sourceCurveSet = EnsureCurveSet(evaluationContext, pair.sourcePath);
            var targetCurveSet = EnsureCurveSet(evaluationContext, pair.targetPath);
            var sourceBindings = sourceCurveSet?.EnumerateExistingBindings().Select(binding => binding.propertyName).OrderBy(name => name, StringComparer.Ordinal).ToList() ?? new List<string>();
            var targetBindings = targetCurveSet?.EnumerateExistingBindings().Select(binding => binding.propertyName).OrderBy(name => name, StringComparer.Ordinal).ToList() ?? new List<string>();
            var sourceKeyTimes = sourceCurveSet != null ? EnumerateTransformKeyTimes(evaluationContext, sourceCurveSet).OrderBy(time => time).Take(8).Select(time => time.ToString("F6")).ToList() : new List<string>();
            var sourceName = string.IsNullOrEmpty(pair.sourcePath) ? string.Empty : pair.sourcePath.Split('/').Last();
            var sourceSuggestions = transformCurvePaths
                .Where(path => string.Equals(path.Split('/').LastOrDefault(), sourceName, StringComparison.Ordinal))
                .Take(5)
                .ToList();

            return
                $"src=\"{pair.sourcePath}\" tgt=\"{pair.targetPath}\" " +
                $"srcScene={(sourceCurveSet != null && sourceCurveSet.SceneTransform != null)} tgtScene={(targetCurveSet != null && targetCurveSet.SceneTransform != null)} " +
                $"srcBindings={FormatDebugList(sourceBindings)} tgtBindings={FormatDebugList(targetBindings)} " +
                $"srcTimes={FormatDebugList(sourceKeyTimes)} srcNameMatches={FormatDebugList(sourceSuggestions)}";
        }

        private static string FormatDebugList(IReadOnlyList<string> values)
        {
            return values == null || values.Count == 0 ? "<none>" : string.Join(",", values.Select(value => $"\"{value}\""));
        }

        private Transform ResolveKeySymmetryReferenceRoot(AnimationClip clip, Transform commonAncestor)
        {
            if (clip == null || commonAncestor == null)
            {
                return commonAncestor;
            }

            var transformCurvePaths = AnimationUtility.GetCurveBindings(clip)
                .Where(binding => GetSupportedTransformBinding(binding) != SupportedTransformBinding.None)
                .Select(binding => binding.path ?? string.Empty)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (TryGetActiveReferenceRoot(out var activeRootObject, false))
            {
                var activeRoot = activeRootObject.transform;
                if (IsAncestorOrSelf(activeRoot, _tempObject.sourceArmatureRoot.transform) &&
                    IsAncestorOrSelf(activeRoot, _tempObject.targetArmatureRoot.transform) &&
                    CountResolvableTransformCurvePaths(activeRoot, transformCurvePaths) > 0)
                {
                    return activeRoot;
                }
            }

            var bestRoot = commonAncestor;
            var bestScore = CountResolvableTransformCurvePaths(bestRoot, transformCurvePaths);

            for (var candidate = commonAncestor.parent; candidate != null; candidate = candidate.parent)
            {
                if (!IsAncestorOrSelf(candidate, _tempObject.sourceArmatureRoot.transform) ||
                    !IsAncestorOrSelf(candidate, _tempObject.targetArmatureRoot.transform))
                {
                    continue;
                }

                var score = CountResolvableTransformCurvePaths(candidate, transformCurvePaths);
                if (score > bestScore)
                {
                    bestRoot = candidate;
                    bestScore = score;
                }
            }

            return bestRoot;
        }

        private static int CountResolvableTransformCurvePaths(Transform root, IEnumerable<string> transformCurvePaths)
        {
            if (root == null || transformCurvePaths == null)
            {
                return 0;
            }

            int count = 0;
            foreach (var path in transformCurvePaths)
            {
                if (string.IsNullOrEmpty(path) || root.Find(path) != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsAncestorOrSelf(Transform ancestor, Transform descendant)
        {
            for (var current = descendant; current != null; current = current.parent)
            {
                if (current == ancestor)
                {
                    return true;
                }
            }

            return false;
        }

        private Dictionary<QuantizedPathKey, TransformSampleOperation> AggregateSelectedTransformOperations(
            IEnumerable<object> selectedKeys,
            TransformEvaluationContext evaluationContext)
        {
            var operations = new Dictionary<QuantizedPathKey, TransformSampleOperation>();

            foreach (var keyframe in selectedKeys)
            {
                var curve = _animationWindowKeyframeCurveProperty.GetValue(keyframe);
                if (curve == null)
                {
                    continue;
                }

                if (!(_animationWindowCurveBindingProperty.GetValue(curve) is EditorCurveBinding binding))
                {
                    continue;
                }

                var bindingKind = GetSupportedTransformBinding(binding);
                if (bindingKind == SupportedTransformBinding.None)
                {
                    continue;
                }

                var rawTime = _animationWindowKeyframeTimeProperty.GetValue(keyframe);
                if (rawTime == null)
                {
                    continue;
                }

                var time = Convert.ToSingle(rawTime);
                var path = binding.path ?? string.Empty;
                var curveSet = EnsureCurveSet(evaluationContext, path);
                if (curveSet == null || curveSet.SceneTransform == null)
                {
                    continue;
                }

                var key = new QuantizedPathKey(path, time);
                if (!operations.TryGetValue(key, out var operation))
                {
                    operation = new TransformSampleOperation
                    {
                        SourcePath = path,
                        TargetPath = path,
                        Time = time,
                        PreferredRotationRepresentation = curveSet.RotationMode
                    };
                    operations.Add(key, operation);
                }

                var bindingRepresentation = GetRotationRepresentation(bindingKind);
                if (bindingRepresentation != RotationRepresentation.None)
                {
                    operation.PreferredRotationRepresentation = bindingRepresentation;
                }
                else if (operation.PreferredRotationRepresentation == RotationRepresentation.None)
                {
                    operation.PreferredRotationRepresentation = curveSet.RotationMode;
                }
            }

            return operations;
        }

        private Dictionary<QuantizedPathKey, TransformSampleOperation> BuildKeySymmetryOperations(
            List<(string sourcePath, string targetPath)> boneMapping,
            TransformEvaluationContext evaluationContext)
        {
            var operations = new Dictionary<QuantizedPathKey, TransformSampleOperation>();

            foreach (var (sourcePath, targetPath) in boneMapping)
            {
                var sourceCurveSet = EnsureCurveSet(evaluationContext, sourcePath);
                var targetCurveSet = EnsureCurveSet(evaluationContext, targetPath);
                if (sourceCurveSet == null || targetCurveSet == null ||
                    sourceCurveSet.SceneTransform == null || targetCurveSet.SceneTransform == null)
                {
                    continue;
                }

                foreach (var time in EnumerateTransformKeyTimes(evaluationContext, sourceCurveSet))
                {
                    var key = new QuantizedPathKey(targetPath, time);
                    if (operations.ContainsKey(key))
                    {
                        continue;
                    }

                    operations.Add(key, new TransformSampleOperation
                    {
                        SourcePath = sourcePath,
                        TargetPath = targetPath,
                        Time = time,
                        PreferredRotationRepresentation = sourceCurveSet.RotationMode
                    });
                }
            }

            return operations;
        }

        private TransformEvaluationContext BuildEvaluationContext(AnimationClip clip, Transform referenceRoot)
        {
            var evaluationContext = new TransformEvaluationContext
            {
                Clip = clip,
                ReferenceRoot = referenceRoot
            };

            BuildSceneTransformLookup(referenceRoot, string.Empty, evaluationContext.SceneTransforms);

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var bindingKind = GetSupportedTransformBinding(binding);
                if (bindingKind == SupportedTransformBinding.None)
                {
                    continue;
                }

                var curveSet = EnsureCurveSet(evaluationContext, binding.path);
                AssignBinding(curveSet, binding, bindingKind);
            }

            return evaluationContext;
        }

        private static void BuildSceneTransformLookup(Transform current, string path, Dictionary<string, Transform> lookup)
        {
            if (current == null)
            {
                return;
            }

            lookup[path] = current;

            for (int i = 0; i < current.childCount; i++)
            {
                var child = current.GetChild(i);
                var childPath = string.IsNullOrEmpty(path) ? child.name : path + "/" + child.name;
                BuildSceneTransformLookup(child, childPath, lookup);
            }
        }

        private static TransformCurveSet EnsureCurveSet(TransformEvaluationContext evaluationContext, string path)
        {
            path = path ?? string.Empty;

            if (!evaluationContext.CurveSets.TryGetValue(path, out var curveSet))
            {
                evaluationContext.SceneTransforms.TryGetValue(path, out var sceneTransform);
                curveSet = new TransformCurveSet
                {
                    Path = path,
                    SceneTransform = sceneTransform
                };
                evaluationContext.CurveSets.Add(path, curveSet);
            }
            else if (curveSet.SceneTransform == null && evaluationContext.SceneTransforms.TryGetValue(path, out var sceneTransform))
            {
                curveSet.SceneTransform = sceneTransform;
            }

            return curveSet;
        }

        private static IEnumerable<float> EnumerateTransformKeyTimes(TransformEvaluationContext evaluationContext, TransformCurveSet curveSet)
        {
            var seenTimes = new HashSet<int>();

            foreach (var binding in curveSet.EnumerateExistingBindings())
            {
                var curve = GetCurve(evaluationContext, binding, false);
                if (curve == null)
                {
                    continue;
                }

                foreach (var key in curve.keys)
                {
                    var quantizedTime = QuantizeTime(key.time);
                    if (seenTimes.Add(quantizedTime))
                    {
                        yield return key.time;
                    }
                }
            }
        }

        private bool TryMirrorLocalPose(
            TransformEvaluationContext evaluationContext,
            string sourcePath,
            string targetPath,
            float time,
            out LocalPoseSample mirroredPose)
        {
            mirroredPose = default;

            var sourceCurveSet = EnsureCurveSet(evaluationContext, sourcePath);
            var targetCurveSet = EnsureCurveSet(evaluationContext, targetPath);
            if (sourceCurveSet == null || targetCurveSet == null ||
                sourceCurveSet.SceneTransform == null || targetCurveSet.SceneTransform == null)
            {
                return false;
            }

            var sourceReferencePose = EvaluateReferencePose(evaluationContext, sourcePath, time);
            var targetParentReferencePose = EvaluateReferencePose(evaluationContext, GetParentPath(targetPath), time);

            var mirroredReferencePosition = ReflectVector(sourceReferencePose.Position, _tempObject.mirrorAxis);
            var mirroredReferenceRotation = ReflectRotation(sourceReferencePose.Rotation, _tempObject.mirrorAxis);

            mirroredPose.LocalPosition = Quaternion.Inverse(targetParentReferencePose.Rotation) * (mirroredReferencePosition - targetParentReferencePose.Position);
            mirroredPose.LocalRotation = NormalizeQuaternion(Quaternion.Inverse(targetParentReferencePose.Rotation) * mirroredReferenceRotation);
            return true;
        }

        private ReferencePoseSample EvaluateReferencePose(TransformEvaluationContext evaluationContext, string path, float time)
        {
            path = path ?? string.Empty;
            var cacheKey = new QuantizedPathKey(path, time);
            if (evaluationContext.ReferencePoseCache.TryGetValue(cacheKey, out var cachedPose))
            {
                return cachedPose;
            }

            ReferencePoseSample result;
            if (string.IsNullOrEmpty(path))
            {
                result = ReferencePoseSample.Identity;
            }
            else
            {
                var parentPose = EvaluateReferencePose(evaluationContext, GetParentPath(path), time);
                var localPose = EvaluateLocalPose(evaluationContext, path, time);
                result = new ReferencePoseSample
                {
                    Position = parentPose.Position + parentPose.Rotation * localPose.LocalPosition,
                    Rotation = NormalizeQuaternion(parentPose.Rotation * localPose.LocalRotation)
                };
            }

            evaluationContext.ReferencePoseCache[cacheKey] = result;
            return result;
        }

        private LocalPoseSample EvaluateLocalPose(TransformEvaluationContext evaluationContext, string path, float time)
        {
            path = path ?? string.Empty;
            var cacheKey = new QuantizedPathKey(path, time);
            if (evaluationContext.LocalPoseCache.TryGetValue(cacheKey, out var cachedPose))
            {
                return cachedPose;
            }

            var curveSet = EnsureCurveSet(evaluationContext, path);
            if (curveSet == null || curveSet.SceneTransform == null)
            {
                return default;
            }

            var localPosition = curveSet.SceneTransform.localPosition;
            localPosition.x = EvaluateFloatBinding(evaluationContext, curveSet.PositionXBinding, time, localPosition.x);
            localPosition.y = EvaluateFloatBinding(evaluationContext, curveSet.PositionYBinding, time, localPosition.y);
            localPosition.z = EvaluateFloatBinding(evaluationContext, curveSet.PositionZBinding, time, localPosition.z);

            Quaternion localRotation;
            switch (curveSet.RotationMode)
            {
                case RotationRepresentation.Quaternion:
                    var quaternion = curveSet.SceneTransform.localRotation;
                    quaternion.x = EvaluateFloatBinding(evaluationContext, curveSet.QuaternionXBinding, time, quaternion.x);
                    quaternion.y = EvaluateFloatBinding(evaluationContext, curveSet.QuaternionYBinding, time, quaternion.y);
                    quaternion.z = EvaluateFloatBinding(evaluationContext, curveSet.QuaternionZBinding, time, quaternion.z);
                    quaternion.w = EvaluateFloatBinding(evaluationContext, curveSet.QuaternionWBinding, time, quaternion.w);
                    localRotation = NormalizeQuaternion(quaternion);
                    break;

                case RotationRepresentation.Euler:
                    var eulerAngles = curveSet.SceneTransform.localEulerAngles;
                    eulerAngles.x = EvaluateFloatBinding(evaluationContext, curveSet.EulerXBinding, time, eulerAngles.x);
                    eulerAngles.y = EvaluateFloatBinding(evaluationContext, curveSet.EulerYBinding, time, eulerAngles.y);
                    eulerAngles.z = EvaluateFloatBinding(evaluationContext, curveSet.EulerZBinding, time, eulerAngles.z);
                    localRotation = Quaternion.Euler(eulerAngles);
                    break;

                default:
                    localRotation = curveSet.SceneTransform.localRotation;
                    break;
            }

            var result = new LocalPoseSample
            {
                LocalPosition = localPosition,
                LocalRotation = NormalizeQuaternion(localRotation)
            };

            evaluationContext.LocalPoseCache[cacheKey] = result;
            return result;
        }

        private bool WriteTransformSample(
            TransformEvaluationContext evaluationContext,
            string targetPath,
            float time,
            LocalPoseSample mirroredPose,
            RotationRepresentation preferredRotationRepresentation,
            HashSet<EditorCurveBinding> changedBindings)
        {
            var targetCurveSet = EnsureCurveSet(evaluationContext, targetPath);
            if (targetCurveSet == null || targetCurveSet.SceneTransform == null)
            {
                return false;
            }

            bool changed = false;

            if (targetCurveSet.HasPositionBindings || !Approximately(targetCurveSet.SceneTransform.localPosition, mirroredPose.LocalPosition))
            {
                changed |= WritePositionSample(evaluationContext, targetCurveSet, time, mirroredPose.LocalPosition, changedBindings);
            }

            var rotationWriteMode = GetRotationWriteMode(targetCurveSet, preferredRotationRepresentation);
            if (rotationWriteMode != RotationRepresentation.None &&
                (targetCurveSet.RotationMode != RotationRepresentation.None || Quaternion.Angle(targetCurveSet.SceneTransform.localRotation, mirroredPose.LocalRotation) > 0.001f))
            {
                changed |= WriteRotationSample(evaluationContext, targetCurveSet, time, mirroredPose.LocalRotation, rotationWriteMode, changedBindings);
            }

            if (changed)
            {
                evaluationContext.LocalPoseCache.Clear();
                evaluationContext.ReferencePoseCache.Clear();
            }

            return changed;
        }

        private bool WritePositionSample(
            TransformEvaluationContext evaluationContext,
            TransformCurveSet targetCurveSet,
            float time,
            Vector3 localPosition,
            HashSet<EditorCurveBinding> changedBindings)
        {
            bool changed = false;
            changed |= WriteFloatKey(evaluationContext, GetOrCreatePositionBinding(targetCurveSet, SupportedTransformBinding.PositionX), time, localPosition.x, changedBindings);
            changed |= WriteFloatKey(evaluationContext, GetOrCreatePositionBinding(targetCurveSet, SupportedTransformBinding.PositionY), time, localPosition.y, changedBindings);
            changed |= WriteFloatKey(evaluationContext, GetOrCreatePositionBinding(targetCurveSet, SupportedTransformBinding.PositionZ), time, localPosition.z, changedBindings);
            return changed;
        }

        private bool WriteRotationSample(
            TransformEvaluationContext evaluationContext,
            TransformCurveSet targetCurveSet,
            float time,
            Quaternion localRotation,
            RotationRepresentation rotationRepresentation,
            HashSet<EditorCurveBinding> changedBindings)
        {
            bool changed = false;

            if (rotationRepresentation == RotationRepresentation.Quaternion)
            {
                var normalized = NormalizeQuaternion(localRotation);
                changed |= WriteFloatKey(evaluationContext, GetOrCreateQuaternionBinding(targetCurveSet, SupportedTransformBinding.QuaternionX), time, normalized.x, changedBindings);
                changed |= WriteFloatKey(evaluationContext, GetOrCreateQuaternionBinding(targetCurveSet, SupportedTransformBinding.QuaternionY), time, normalized.y, changedBindings);
                changed |= WriteFloatKey(evaluationContext, GetOrCreateQuaternionBinding(targetCurveSet, SupportedTransformBinding.QuaternionZ), time, normalized.z, changedBindings);
                changed |= WriteFloatKey(evaluationContext, GetOrCreateQuaternionBinding(targetCurveSet, SupportedTransformBinding.QuaternionW), time, normalized.w, changedBindings);
                return changed;
            }

            var eulerAngles = localRotation.eulerAngles;
            changed |= WriteFloatKey(evaluationContext, GetOrCreateEulerBinding(targetCurveSet, SupportedTransformBinding.EulerX), time, eulerAngles.x, changedBindings);
            changed |= WriteFloatKey(evaluationContext, GetOrCreateEulerBinding(targetCurveSet, SupportedTransformBinding.EulerY), time, eulerAngles.y, changedBindings);
            changed |= WriteFloatKey(evaluationContext, GetOrCreateEulerBinding(targetCurveSet, SupportedTransformBinding.EulerZ), time, eulerAngles.z, changedBindings);
            return changed;
        }

        private static float EvaluateFloatBinding(
            TransformEvaluationContext evaluationContext,
            EditorCurveBinding? binding,
            float time,
            float fallbackValue)
        {
            if (!binding.HasValue)
            {
                return fallbackValue;
            }

            var curve = GetCurve(evaluationContext, binding.Value, false);
            if (curve == null || curve.length == 0)
            {
                return fallbackValue;
            }

            return curve.Evaluate(time);
        }

        private static AnimationCurve GetCurve(
            TransformEvaluationContext evaluationContext,
            EditorCurveBinding binding,
            bool createIfMissing)
        {
            if (evaluationContext.CurveCache.TryGetValue(binding, out var curve))
            {
                return curve;
            }

            curve = AnimationUtility.GetEditorCurve(evaluationContext.Clip, binding);
            if (curve == null && createIfMissing)
            {
                curve = new AnimationCurve();
            }

            if (curve != null)
            {
                evaluationContext.CurveCache[binding] = curve;
            }

            return curve;
        }

        private static void CommitCurveChanges(
            AnimationClip clip,
            TransformEvaluationContext evaluationContext,
            IEnumerable<EditorCurveBinding> changedBindings)
        {
            foreach (var binding in changedBindings)
            {
                if (evaluationContext.CurveCache.TryGetValue(binding, out var curve))
                {
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
            }
        }

        private static bool WriteFloatKey(
            TransformEvaluationContext evaluationContext,
            EditorCurveBinding binding,
            float time,
            float value,
            ISet<EditorCurveBinding> changedBindings)
        {
            var curve = GetCurve(evaluationContext, binding, true);
            if (curve == null)
            {
                return false;
            }

            var keyIndex = FindKeyIndex(curve, time);
            if (keyIndex >= 0)
            {
                var existingKey = curve.keys[keyIndex];
                if (Mathf.Abs(existingKey.value - value) <= 0.000001f)
                {
                    return false;
                }

                var metadata = CaptureKeyframeMetadata(curve, keyIndex);
                var updatedKey = existingKey;
                updatedKey.value = value;
                updatedKey.weightedMode = existingKey.weightedMode;
                updatedKey.inWeight = existingKey.inWeight;
                updatedKey.outWeight = existingKey.outWeight;
                updatedKey.inTangent = existingKey.inTangent;
                updatedKey.outTangent = existingKey.outTangent;

                var updatedIndex = curve.MoveKey(keyIndex, updatedKey);
                ApplyKeyframeMetadata(curve, updatedIndex, metadata);
            }
            else
            {
                var newIndex = curve.AddKey(new Keyframe(time, value));
                if (newIndex >= 0)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, newIndex, AnimationUtility.TangentMode.ClampedAuto);
                    AnimationUtility.SetKeyRightTangentMode(curve, newIndex, AnimationUtility.TangentMode.ClampedAuto);
                }
            }

            evaluationContext.CurveCache[binding] = curve;
            changedBindings.Add(binding);
            return true;
        }

        private static int FindKeyIndex(AnimationCurve curve, float time)
        {
            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                if (Mathf.Abs(keys[i].time - time) <= TimeComparisonEpsilon)
                {
                    return i;
                }
            }

            return -1;
        }

        private static KeyframeMetadata CaptureKeyframeMetadata(AnimationCurve curve, int keyIndex)
        {
            return new KeyframeMetadata
            {
                Broken = AnimationUtility.GetKeyBroken(curve, keyIndex),
                LeftTangentMode = AnimationUtility.GetKeyLeftTangentMode(curve, keyIndex),
                RightTangentMode = AnimationUtility.GetKeyRightTangentMode(curve, keyIndex)
            };
        }

        private static void ApplyKeyframeMetadata(AnimationCurve curve, int keyIndex, KeyframeMetadata metadata)
        {
            AnimationUtility.SetKeyBroken(curve, keyIndex, metadata.Broken);
            AnimationUtility.SetKeyLeftTangentMode(curve, keyIndex, metadata.LeftTangentMode);
            AnimationUtility.SetKeyRightTangentMode(curve, keyIndex, metadata.RightTangentMode);
        }

        private static EditorCurveBinding GetOrCreatePositionBinding(TransformCurveSet curveSet, SupportedTransformBinding bindingKind)
        {
            switch (bindingKind)
            {
                case SupportedTransformBinding.PositionX:
                    if (!curveSet.PositionXBinding.HasValue)
                    {
                        curveSet.PositionXBinding = CreateTransformBinding(curveSet.Path, DefaultPositionXProperty);
                    }
                    return curveSet.PositionXBinding.Value;

                case SupportedTransformBinding.PositionY:
                    if (!curveSet.PositionYBinding.HasValue)
                    {
                        curveSet.PositionYBinding = CreateTransformBinding(curveSet.Path, DefaultPositionYProperty);
                    }
                    return curveSet.PositionYBinding.Value;

                default:
                    if (!curveSet.PositionZBinding.HasValue)
                    {
                        curveSet.PositionZBinding = CreateTransformBinding(curveSet.Path, DefaultPositionZProperty);
                    }
                    return curveSet.PositionZBinding.Value;
            }
        }

        private static EditorCurveBinding GetOrCreateEulerBinding(TransformCurveSet curveSet, SupportedTransformBinding bindingKind)
        {
            switch (bindingKind)
            {
                case SupportedTransformBinding.EulerX:
                    if (!curveSet.EulerXBinding.HasValue)
                    {
                        curveSet.EulerXBinding = CreateTransformBinding(curveSet.Path, GetEulerPropertyName(curveSet, DefaultEulerXProperty));
                    }
                    return curveSet.EulerXBinding.Value;

                case SupportedTransformBinding.EulerY:
                    if (!curveSet.EulerYBinding.HasValue)
                    {
                        curveSet.EulerYBinding = CreateTransformBinding(curveSet.Path, GetEulerPropertyName(curveSet, DefaultEulerYProperty));
                    }
                    return curveSet.EulerYBinding.Value;

                default:
                    if (!curveSet.EulerZBinding.HasValue)
                    {
                        curveSet.EulerZBinding = CreateTransformBinding(curveSet.Path, GetEulerPropertyName(curveSet, DefaultEulerZProperty));
                    }
                    return curveSet.EulerZBinding.Value;
            }
        }

        private static EditorCurveBinding GetOrCreateQuaternionBinding(TransformCurveSet curveSet, SupportedTransformBinding bindingKind)
        {
            switch (bindingKind)
            {
                case SupportedTransformBinding.QuaternionX:
                    if (!curveSet.QuaternionXBinding.HasValue)
                    {
                        curveSet.QuaternionXBinding = CreateTransformBinding(curveSet.Path, GetQuaternionPropertyName(curveSet, DefaultQuaternionXProperty));
                    }
                    return curveSet.QuaternionXBinding.Value;

                case SupportedTransformBinding.QuaternionY:
                    if (!curveSet.QuaternionYBinding.HasValue)
                    {
                        curveSet.QuaternionYBinding = CreateTransformBinding(curveSet.Path, GetQuaternionPropertyName(curveSet, DefaultQuaternionYProperty));
                    }
                    return curveSet.QuaternionYBinding.Value;

                case SupportedTransformBinding.QuaternionZ:
                    if (!curveSet.QuaternionZBinding.HasValue)
                    {
                        curveSet.QuaternionZBinding = CreateTransformBinding(curveSet.Path, GetQuaternionPropertyName(curveSet, DefaultQuaternionZProperty));
                    }
                    return curveSet.QuaternionZBinding.Value;

                default:
                    if (!curveSet.QuaternionWBinding.HasValue)
                    {
                        curveSet.QuaternionWBinding = CreateTransformBinding(curveSet.Path, GetQuaternionPropertyName(curveSet, DefaultQuaternionWProperty));
                    }
                    return curveSet.QuaternionWBinding.Value;
            }
        }

        private static EditorCurveBinding CreateTransformBinding(string path, string propertyName)
        {
            return new EditorCurveBinding
            {
                path = path ?? string.Empty,
                type = typeof(Transform),
                propertyName = propertyName
            };
        }

        private static string GetEulerPropertyName(TransformCurveSet curveSet, string fallbackProperty)
        {
            var existingProperty = GetExistingPropertyName(curveSet.EulerXBinding, curveSet.EulerYBinding, curveSet.EulerZBinding);
            return string.IsNullOrEmpty(existingProperty) ? fallbackProperty : ReplaceAxisSuffix(existingProperty, fallbackProperty);
        }

        private static string GetQuaternionPropertyName(TransformCurveSet curveSet, string fallbackProperty)
        {
            var existingProperty = GetExistingPropertyName(curveSet.QuaternionXBinding, curveSet.QuaternionYBinding, curveSet.QuaternionZBinding, curveSet.QuaternionWBinding);
            return string.IsNullOrEmpty(existingProperty) ? fallbackProperty : ReplaceAxisSuffix(existingProperty, fallbackProperty);
        }

        private static string GetExistingPropertyName(params EditorCurveBinding?[] bindings)
        {
            foreach (var binding in bindings)
            {
                if (binding.HasValue)
                {
                    return binding.Value.propertyName;
                }
            }

            return null;
        }

        private static string ReplaceAxisSuffix(string existingProperty, string fallbackProperty)
        {
            if (string.IsNullOrEmpty(existingProperty))
            {
                return fallbackProperty;
            }

            var axisSeparator = existingProperty.LastIndexOf('.');
            var fallbackSeparator = fallbackProperty.LastIndexOf('.');
            if (axisSeparator < 0 || fallbackSeparator < 0)
            {
                return fallbackProperty;
            }

            return existingProperty.Substring(0, axisSeparator + 1) + fallbackProperty.Substring(fallbackSeparator + 1);
        }

        private static RotationRepresentation GetRotationWriteMode(
            TransformCurveSet targetCurveSet,
            RotationRepresentation preferredRotationRepresentation)
        {
            if (targetCurveSet.RotationMode != RotationRepresentation.None)
            {
                return targetCurveSet.RotationMode;
            }

            if (preferredRotationRepresentation != RotationRepresentation.None)
            {
                return preferredRotationRepresentation;
            }

            return RotationRepresentation.Euler;
        }

        private static SupportedTransformBinding GetSupportedTransformBinding(EditorCurveBinding binding)
        {
            var propertyName = binding.propertyName ?? string.Empty;
            if (MatchesAxisProperty(propertyName, 'x'))
            {
                if (propertyName.IndexOf("LocalPosition", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return SupportedTransformBinding.PositionX;
                }

                if (IsEulerProperty(propertyName))
                {
                    return SupportedTransformBinding.EulerX;
                }

                if (IsQuaternionProperty(propertyName))
                {
                    return SupportedTransformBinding.QuaternionX;
                }
            }

            if (MatchesAxisProperty(propertyName, 'y'))
            {
                if (propertyName.IndexOf("LocalPosition", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return SupportedTransformBinding.PositionY;
                }

                if (IsEulerProperty(propertyName))
                {
                    return SupportedTransformBinding.EulerY;
                }

                if (IsQuaternionProperty(propertyName))
                {
                    return SupportedTransformBinding.QuaternionY;
                }
            }

            if (MatchesAxisProperty(propertyName, 'z'))
            {
                if (propertyName.IndexOf("LocalPosition", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return SupportedTransformBinding.PositionZ;
                }

                if (IsEulerProperty(propertyName))
                {
                    return SupportedTransformBinding.EulerZ;
                }

                if (IsQuaternionProperty(propertyName))
                {
                    return SupportedTransformBinding.QuaternionZ;
                }
            }

            if (MatchesAxisProperty(propertyName, 'w') && IsQuaternionProperty(propertyName))
            {
                return SupportedTransformBinding.QuaternionW;
            }

            return SupportedTransformBinding.None;
        }

        private static bool MatchesAxisProperty(string propertyName, char axis)
        {
            return propertyName.EndsWith("." + axis, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEulerProperty(string propertyName)
        {
            return propertyName.IndexOf("Euler", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsQuaternionProperty(string propertyName)
        {
            return propertyName.IndexOf("LocalRotation", StringComparison.OrdinalIgnoreCase) >= 0 &&
                propertyName.IndexOf("Euler", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static RotationRepresentation GetRotationRepresentation(SupportedTransformBinding bindingKind)
        {
            switch (bindingKind)
            {
                case SupportedTransformBinding.EulerX:
                case SupportedTransformBinding.EulerY:
                case SupportedTransformBinding.EulerZ:
                    return RotationRepresentation.Euler;

                case SupportedTransformBinding.QuaternionX:
                case SupportedTransformBinding.QuaternionY:
                case SupportedTransformBinding.QuaternionZ:
                case SupportedTransformBinding.QuaternionW:
                    return RotationRepresentation.Quaternion;

                default:
                    return RotationRepresentation.None;
            }
        }

        private static void AssignBinding(TransformCurveSet curveSet, EditorCurveBinding binding, SupportedTransformBinding bindingKind)
        {
            switch (bindingKind)
            {
                case SupportedTransformBinding.PositionX:
                    curveSet.PositionXBinding = binding;
                    break;
                case SupportedTransformBinding.PositionY:
                    curveSet.PositionYBinding = binding;
                    break;
                case SupportedTransformBinding.PositionZ:
                    curveSet.PositionZBinding = binding;
                    break;
                case SupportedTransformBinding.EulerX:
                    curveSet.EulerXBinding = binding;
                    break;
                case SupportedTransformBinding.EulerY:
                    curveSet.EulerYBinding = binding;
                    break;
                case SupportedTransformBinding.EulerZ:
                    curveSet.EulerZBinding = binding;
                    break;
                case SupportedTransformBinding.QuaternionX:
                    curveSet.QuaternionXBinding = binding;
                    break;
                case SupportedTransformBinding.QuaternionY:
                    curveSet.QuaternionYBinding = binding;
                    break;
                case SupportedTransformBinding.QuaternionZ:
                    curveSet.QuaternionZBinding = binding;
                    break;
                case SupportedTransformBinding.QuaternionW:
                    curveSet.QuaternionWBinding = binding;
                    break;
            }
        }

        private static Vector3 ReflectVector(Vector3 value, MirrorAxis mirrorAxis)
        {
            switch (mirrorAxis)
            {
                case MirrorAxis.X:
                    return new Vector3(-value.x, value.y, value.z);
                case MirrorAxis.Y:
                    return new Vector3(value.x, -value.y, value.z);
                default:
                    return new Vector3(value.x, value.y, -value.z);
            }
        }

        private static Quaternion ReflectRotation(Quaternion rotation, MirrorAxis mirrorAxis)
        {
            var reflectionMatrix = GetReflectionMatrix(mirrorAxis);
            var reflectedMatrix = reflectionMatrix * Matrix4x4.Rotate(NormalizeQuaternion(rotation)) * reflectionMatrix;
            return NormalizeQuaternion(Quaternion.LookRotation(reflectedMatrix.GetColumn(2), reflectedMatrix.GetColumn(1)));
        }

        private static Matrix4x4 GetReflectionMatrix(MirrorAxis mirrorAxis)
        {
            var reflection = Matrix4x4.identity;
            switch (mirrorAxis)
            {
                case MirrorAxis.X:
                    reflection.m00 = -1f;
                    break;
                case MirrorAxis.Y:
                    reflection.m11 = -1f;
                    break;
                default:
                    reflection.m22 = -1f;
                    break;
            }

            return reflection;
        }

        private static Quaternion NormalizeQuaternion(Quaternion rotation)
        {
            var magnitude = Mathf.Sqrt(
                rotation.x * rotation.x +
                rotation.y * rotation.y +
                rotation.z * rotation.z +
                rotation.w * rotation.w);

            if (magnitude <= Mathf.Epsilon)
            {
                return Quaternion.identity;
            }

            var inverseMagnitude = 1f / magnitude;
            return new Quaternion(
                rotation.x * inverseMagnitude,
                rotation.y * inverseMagnitude,
                rotation.z * inverseMagnitude,
                rotation.w * inverseMagnitude);
        }

        private static bool Approximately(Vector3 left, Vector3 right)
        {
            return (left - right).sqrMagnitude <= 0.000001f;
        }

        private static int QuantizeTime(float time)
        {
            return Mathf.RoundToInt(time * TimeQuantizationScale);
        }

        private static int GetPathDepth(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return 0;
            }

            return path.Count(character => character == '/') + 1;
        }

        private static string GetParentPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var separatorIndex = path.LastIndexOf('/');
            return separatorIndex < 0 ? string.Empty : path.Substring(0, separatorIndex);
        }

        private List<(string sourcePath, string targetPath)> BuildBoneMapping(Transform referenceRoot)
        {
            var mapping = new List<(string, string)>();

            if (_tempObject.sourceArmatureRoot == null || _tempObject.targetArmatureRoot == null || referenceRoot == null)
            {
                return mapping;
            }

            var selectedSourcePaths = new List<string>();
            var selectedTargetPaths = new List<string>();

            CollectSelectedPaths(_tempObject.sourceChain, referenceRoot, selectedSourcePaths);
            CollectSelectedPaths(_tempObject.targetChain, referenceRoot, selectedTargetPaths);

            var sourceNameToPath = new Dictionary<string, string>();
            var targetNameToPath = new Dictionary<string, string>();

            foreach (var path in selectedSourcePaths)
            {
                var name = path.Split('/').Last();
                if (!sourceNameToPath.ContainsKey(name))
                {
                    sourceNameToPath[name] = path;
                }
            }

            foreach (var path in selectedTargetPaths)
            {
                var name = path.Split('/').Last();
                if (!targetNameToPath.ContainsKey(name))
                {
                    targetNameToPath[name] = path;
                }
            }

            foreach (var kvp in sourceNameToPath)
            {
                if (!TryGetMirrorBoneName(kvp.Key, out var mirroredName))
                {
                    continue;
                }

                if (targetNameToPath.TryGetValue(mirroredName, out var targetPath))
                {
                    mapping.Add((kvp.Value, targetPath));
                }
            }

            return mapping;
        }

        private static Transform FindCommonAncestor(Transform first, Transform second)
        {
            if (first == null || second == null)
            {
                return null;
            }

            var ancestors = new HashSet<Transform>();
            var current = first;
            while (current != null)
            {
                ancestors.Add(current);
                current = current.parent;
            }

            current = second;
            while (current != null)
            {
                if (ancestors.Contains(current))
                {
                    return current;
                }

                current = current.parent;
            }

            return null;
        }

        private void CollectSelectedPaths(HierarchyNode node, Transform root, List<string> paths)
        {
            if (node == null || node.nodeObject == null || root == null)
            {
                return;
            }

            if (node.isSelected)
            {
                var path = GetRelativePath(root, node.nodeObject.transform);
                if (path != null)
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
            if (root == null || target == null)
            {
                return null;
            }

            if (root == target)
            {
                return string.Empty;
            }

            var names = new Stack<string>();
            var current = target;

            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return current == root ? string.Join("/", names) : null;
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
            Debug.Log($"[KeyframeMirror] {message}");
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

            // Mirror axis selects the reference-space mirror plane normal.
            public MirrorAxis mirrorAxis = MirrorAxis.X;
        }

        #endregion
    }
}
