#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditorInternal;
using System.Collections.Generic;
using System.Linq;
using VRC.SDK3.Avatars.Components;
using System;

namespace marble810.AvatarTools.PoseMenuMaker
{
    public class PoseMenuMaker : EditorWindow
{
    /* ---------- 新增：目标 Avatar ---------- */
    private SerializedObject serializedObj;
    private SerializedProperty avatarRootProp;
    private SerializedProperty groupNamesProp;
    private ReorderableList groupNamesRL;

    private readonly Dictionary<int, List<AnimationClip>> groupClips = new();
    private readonly Dictionary<int, ReorderableList> groupRL = new();

    /* ---------- 打开窗口 ---------- */
    [MenuItem("MarbleTools/PoseMenuMaker")]
    static void Open() => GetWindow<PoseMenuMaker>("Pose Menu Maker");

    /* ---------- GameObject 右键菜单 ---------- */
    [MenuItem("GameObject/marbleTools/PoseMenuMaker", false, 0)]
    static void OpenWithSelected(MenuCommand cmd)
    {
        GameObject go = cmd.context as GameObject;

        // 1) 验证 VRCAvatarDescriptor
        if (go == null || go.GetComponent<VRCAvatarDescriptor>() == null)
        {
            EditorUtility.DisplayDialog(
                "选中物体不是 Avatar",
                "请选择一个带有 VRCAvatarDescriptor 组件的 GameObject。",
                "确定");
            return;   // 直接终止，不打开窗口也不填数据
        }

        // 2) 拿到/创建窗口并填数据
        var win = GetWindow<PoseMenuMaker>("Pose Menu Maker");
        win.avatarRootProp.objectReferenceValue = go;
        win.serializedObj.ApplyModifiedProperties();
        win.Repaint();
    }

    [MenuItem("GameObject/marbleTools/PoseMenuMaker", true)]
    static bool ValidateOpenWithSelected() => Selection.activeGameObject != null;

    private void OnEnable()
    {
        var container = ScriptableObject.CreateInstance<FakeContainer>();
        serializedObj = new SerializedObject(container);
        avatarRootProp = serializedObj.FindProperty("avatarRoot");
        groupNamesProp = serializedObj.FindProperty("names");

        groupNamesRL = new ReorderableList(serializedObj, groupNamesProp,
            true, true, true, true)
        {
            drawHeaderCallback = rect => EditorGUI.LabelField(rect, "分组名称列表", EditorStyles.boldLabel),
            drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                var element = groupNamesProp.GetArrayElementAtIndex(index);
                rect.y += 2;
                element.stringValue = EditorGUI.TextField(rect, element.stringValue);
            },
            onChangedCallback = _ => RebuildGroupLists(),
            onAddCallback = list =>
            {
                serializedObj.Update();
                list.serializedProperty.InsertArrayElementAtIndex(list.serializedProperty.arraySize);
                var newEle = list.serializedProperty.GetArrayElementAtIndex(list.serializedProperty.arraySize - 1);
                newEle.stringValue = GetUniqueGroupName();
                serializedObj.ApplyModifiedProperties();
            }
        };

        EnsureDefaultGroup();

    }

    private void EnsureDefaultGroup()
    {
        serializedObj.Update();
        if (groupNamesProp.arraySize == 0)
        {
            groupNamesProp.InsertArrayElementAtIndex(0);
            groupNamesProp.GetArrayElementAtIndex(0).stringValue = "Default";
        }
        serializedObj.ApplyModifiedProperties();
        RebuildGroupLists();   // 刷新 ReorderableList 缓存
    }

    private void OnDisable()
    {
        if (serializedObj?.targetObject) DestroyImmediate(serializedObj.targetObject);
    }

    private void OnGUI()
    {
        serializedObj.Update();

        /* ① 目标 Avatar（最顶部） */
        EditorGUILayout.PropertyField(avatarRootProp, new GUIContent("Target Avatar"));
        EditorGUILayout.Space();

        /* ② 分组名称 */
        groupNamesRL.DoLayoutList();

        /* ③ 每个分组的子设置 */
        EditorGUILayout.Space();
        for (int i = 0; i < groupNamesProp.arraySize; i++)
        {
            string name = groupNamesProp.GetArrayElementAtIndex(i).stringValue;
            DrawSingleGroup(i, name);
        }

        serializedObj.ApplyModifiedProperties();
    }

    private void DrawSingleGroup(int idx, string title)
    {
        if (!groupClips.TryGetValue(idx, out var clips))
        {
            clips = new List<AnimationClip>();
            groupClips[idx] = clips;
        }
        if (!groupRL.TryGetValue(idx, out var rl))
        {
            rl = new ReorderableList(clips, typeof(AnimationClip), true, true, true, true)
            {
                drawHeaderCallback = r => EditorGUI.LabelField(r, "AnimationClips"),
                drawElementCallback = (r, i, a, f) =>
                {
                    r.y += 2;
                    clips[i] = (AnimationClip)EditorGUI.ObjectField(r, clips[i], typeof(AnimationClip), false);
                }
            };
            groupRL[idx] = rl;
        }

        Rect box = EditorGUILayout.BeginVertical(GUI.skin.box);
        {
            EditorGUILayout.LabelField($"分组：{title}", EditorStyles.boldLabel);

            Rect zone = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
            GUI.Box(zone, "拖 AnimationClip 到这里（仅该分组）");
            HandleClipDrop(zone, clips);

            rl.DoLayoutList();

            if (GUILayout.Button("Clear All", GUILayout.Width(80)))
            {
                var undoProxy = ScriptableObject.CreateInstance<UndoProxy>();
                undoProxy.clips = new List<AnimationClip>(clips);
                Undo.RegisterCompleteObjectUndo(undoProxy, $"Clear Group {idx} Clips");
                clips.Clear();
                UndoProxy.SetRevertAction(undoProxy, () =>
                {
                    clips.Clear();
                    clips.AddRange(undoProxy.clips);
                });
            }

            if (GUILayout.Button("Create Animator Test"))
            {
                    string path = Domain.Path.AnimatorPath;
                    UnityEditor.Animations.AnimatorController animator = Utils.AnimatorFactory.CreateAnimator(path,)
            }
            
        }
        EditorGUILayout.EndVertical();
        GUILayout.Space(10);
    }

    private static void HandleClipDrop(Rect zone, List<AnimationClip> targetList)
    {
        var e = Event.current;
        if (!zone.Contains(e.mousePosition)) return;
        if (e.type == EventType.DragUpdated)
        {
            bool allClips = DragAndDrop.objectReferences.All(o => o is AnimationClip);
            DragAndDrop.visualMode = allClips ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            e.Use();
        }
        else if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            e.Use();
            targetList.AddRange(DragAndDrop.objectReferences.OfType<AnimationClip>());
        }
    }

    private void RebuildGroupLists()
    {
        int newCount = groupNamesProp.arraySize;
        var extra = groupClips.Keys.Where(k => k >= newCount).ToList();
        foreach (var k in extra) { groupClips.Remove(k); groupRL.Remove(k); }
    }

    private class FakeContainer : ScriptableObject
    {
        public GameObject avatarRoot;
        public List<string> names = new();
    }

    private class UndoProxy : ScriptableObject
    {
        public List<AnimationClip> clips;
        private System.Action revertAction;
        void OnDisable() => revertAction?.Invoke();
        public static void SetRevertAction(UndoProxy proxy, System.Action action)
            => proxy.revertAction = action;
    }

    private string GetUniqueGroupName()
    {
        int index = 0;
        string candidate;
        do
        {
            candidate = $"Group{index:D2}";
            ++index;
        }
        while (NameExistsInGroups(candidate));
        return candidate;
    }

    private bool NameExistsInGroups(string name)
    {
        for (int i = 0; i < groupNamesProp.arraySize; i++)
            if (groupNamesProp.GetArrayElementAtIndex(i).stringValue == name)
                return true;
        return false;
    }
    
    private List<AnimationClip> GetAllClips()
    {
        // 创建一个空列表来存放最终结果
        var allClips = new List<AnimationClip>();
        // groupClips.Values 包含了字典中所有的值 (即所有 List<AnimationClip>)
        foreach (var clipListInGroup in groupClips.Values)
        {
            // 将当前分组的列表中的所有元素添加到我们的大列表中
            allClips.AddRange(clipListInGroup);
        }
        
        // 返回一个移除了所有 null 项的干净列表，这更健壮
        return allClips.Where(clip => clip != null).ToList();
    }

    
}
}

#endif
