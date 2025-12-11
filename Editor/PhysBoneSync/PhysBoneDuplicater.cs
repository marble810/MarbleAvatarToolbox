using UnityEngine;
using UnityEditor;
using marble810.AvatarTools.Utils;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using marble810.AvatarTools.CustomGUI;

namespace marble810.AvatarTools.PhysBoneSync
{
    public class PhysBoneDuplicater : EditorWindow
    {

        private SerializedObject serializedObject;
        private TempContainer tempObject;

        private NodeInfoRefreshScheduler _refreshScheduler;

        [MenuItem("MarbleTools/PhysBone Duplicater")]
        public static void ShowWindow()
        {
            GetWindow<PhysBoneDuplicater>("PhysBone Duplicater");
        }

        private void OnEnable()
        {
            EnsureInitialized();

            _refreshScheduler = new NodeInfoRefreshScheduler(() =>
            {
                if (serializedObject != null && serializedObject.targetObject != null)
                {
                    serializedObject.Update();
                }
            }, intervalMs: 200);

            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            _refreshScheduler?.Dispose();
            _refreshScheduler = null;

            ParentingNodeGUI.NodeInfoProvider = null;

            Undo.undoRedoPerformed -= OnUndoRedo;

            if (tempObject != null) DestroyImmediate(tempObject);
        }

        private void OnUndoRedo()
        {
            // Undo/Redo 后立即刷新（不等待定时器）
            serializedObject?.Update();
            _refreshScheduler?.TriggerNow();
        }

        private void EnsureInitialized()
        {
            if (tempObject == null)
            {
                tempObject = ScriptableObject.CreateInstance<TempContainer>();
                tempObject.hideFlags = HideFlags.DontSave; // 防止保存到场景
            }

            if (serializedObject == null || serializedObject.targetObject == null)
            {
                serializedObject = new SerializedObject(tempObject);
            }

            ParentingNodeGUI.NodeInfoProvider = CreateNodeInfo;
        }

        private NodeInfoResult CreateNodeInfo(SerializedProperty property)
        {

            var result = NodeInfoResult.Create();
            var isSelectedProp = property.FindPropertyRelative("isSelected");
            var nodeObjectProp = property.FindPropertyRelative("nodeObject");

            // Debug.Log($"[CreateNodeInfo] Node: {nodeObjectProp?.objectReferenceValue?.name ?? "null"} || IsSelected: {isSelectedProp?.boolValue ?? false}");

            result.Add(TextSegment.Red("Test"));
            result.Space();
            result.Add(TextSegment.Green("Test2"));

            if (isSelectedProp != null && isSelectedProp.boolValue)
            {
                result.Add(TextSegment.White("isChecked"));
                Debug.Log(result.ToString());
            }

            return result;
        }

        public void CreateGUI()
        {

            EnsureInitialized();

            VisualElement root = rootVisualElement;

            root.style.paddingTop = 4f;
            root.style.paddingBottom = 4f;
            root.style.paddingLeft = 4f;
            root.style.paddingRight = 4f;

            var marbleLabel = new Label("marbleAvatarTools");
            marbleLabel.style.fontSize = 12f;
            marbleLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            marbleLabel.style.marginBottom = 4f;
            root.Add(marbleLabel);

            var titleLabel = new Label("PhysBone Duplicater");
            titleLabel.style.fontSize = 18f;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.marginBottom = 10f;
            root.Add(titleLabel);

            var sourceRootProp = serializedObject.FindProperty("sourceArmatureRoot");
            var targetRootProp = serializedObject.FindProperty("targetArmatureRoot");
            var sourceChainProp = serializedObject.FindProperty("sourceChain");
            var targetChainProp = serializedObject.FindProperty("targetChain");


            var sourceRootField = new PropertyField(sourceRootProp);
            var targetRootField = new PropertyField(targetRootProp);

            var sourceChainField = new PropertyField(sourceChainProp);
            var targetChainField = new PropertyField(targetChainProp);

            root.Add(sourceRootField);
            root.Add(sourceChainField);

            var space = new VisualElement();
            space.style.height = 10f;
            root.Add(space);

            root.Add(targetRootField);
            root.Add(targetChainField);

            sourceRootField.TrackPropertyValue(sourceRootProp, (prop) =>
            {
                GameObject go = prop.objectReferenceValue as GameObject;
                serializedObject.ApplyModifiedProperties();
                if (go != null)
                {
                    var container = (TempContainer)serializedObject.targetObject;
                    container.sourceChain = GetChain(go);
                    EditorUtility.SetDirty(container);
                    serializedObject.Update();

                    Debug.Log($"Source Root: {go.name}");
                }
                else
                {
                    var container = (TempContainer)serializedObject.targetObject;
                    container.sourceChain = null;
                    EditorUtility.SetDirty(container);
                    serializedObject.Update();

                }
                rootVisualElement.MarkDirtyRepaint();
            });

            root.Bind(serializedObject);

            root.schedule.Execute(() =>
            {
                Debug.Log($"RefreshScheduler Started after delay.");
                _refreshScheduler?.Start(root);
            }).ExecuteLater(100);
        }

        private ParentingTree GetChain(GameObject root)
        {
            if (root == null) return null;
            return ParentingTreeHandler.GetParentingChain(root);
        }

        // private void SetupPeriodicRefresh(VisualElement root)
        // {
        //     _refreshSchedule = root.schedule.Execute(() =>
        //     {
        //         if(serializedObject != null && serializedObject.targetObject != null)
        //         {
        //             serializedObject.Update();
        //             ParentingNodeGUI.TriggerRefresh();
        //         }
        //     }).Every(200);
        // }

        private class TempContainer : ScriptableObject
        {
            public GameObject sourceArmatureRoot;
            public GameObject targetArmatureRoot;
            public ParentingTree sourceChain;
            public ParentingTree targetChain;
        }
    }

}