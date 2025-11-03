using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using VRC.SDK3.Dynamics.PhysBone.Components;
using System.Drawing.Text;
using HarmonyLib;

public class PhysBoneCollidersBatchSetup : EditorWindow
{
    private List<GameObject> colliderObjects = new List<GameObject>();
    private List<GameObject> targetObjects = new List<GameObject>();

    private ReorderableList colliderList;
    private ReorderableList targetList;
    private SerializedObject serializedObject;
    private SerializedProperty colliderProperty;
    private SerializedProperty targetProperty;

    [MenuItem("MarbleTools/PhysBone Colliders Batch Setup")]
    public static void ShowWindow()
    {
        GetWindow<PhysBoneCollidersBatchSetup>("PhysBone Colliders Batch Setup");
    }

    private void OnEnable()
    {
        // Create a temporary scriptable object to hold our lists for serialization
        var tempObject = ScriptableObject.CreateInstance<TempContainer>();
        tempObject.colliderObjects = colliderObjects;
        tempObject.targetObjects = targetObjects;

        serializedObject = new SerializedObject(tempObject);
        colliderProperty = serializedObject.FindProperty("colliderObjects");
        targetProperty = serializedObject.FindProperty("targetObjects");

        // Initialize ReorderableList for colliders
        colliderList = new ReorderableList(serializedObject, colliderProperty, true, true, true, true)
        {
            drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "Collider GameObjects (Drag & Drop or Add)");
            },

            drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var element = colliderProperty.GetArrayElementAtIndex(index);
                EditorGUI.ObjectField(new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                    element, typeof(GameObject), GUIContent.none);
            },

            elementHeight = EditorGUIUtility.singleLineHeight + 2,

            onAddCallback = (list) =>
            {
                colliderProperty.arraySize++;
                var newElement = colliderProperty.GetArrayElementAtIndex(colliderProperty.arraySize - 1);
                newElement.objectReferenceValue = null;
            }
        };

        // Initialize ReorderableList for targets
        targetList = new ReorderableList(serializedObject, targetProperty, true, true, true, true)
        {
            drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "Target GameObjects (Drag & Drop or Add)");
            },

            drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var element = targetProperty.GetArrayElementAtIndex(index);
                EditorGUI.ObjectField(new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                    element, typeof(GameObject), GUIContent.none);
            },

            elementHeight = EditorGUIUtility.singleLineHeight + 2,

            onAddCallback = (list) =>
            {
                targetProperty.arraySize++;
                var newElement = targetProperty.GetArrayElementAtIndex(targetProperty.arraySize - 1);
                newElement.objectReferenceValue = null;
            }
        };


    }

#nullable enable

    private void DoYouHavePhysBoneComponent()
    {
        VRCPhysBone? component = targetObjects[0].GetComponent<VRCPhysBone>();

        if (component != null)
        {
            Debug.Log("Found VRCPhysBone Component");
        }

        return;

    }

    private void AddCollider()
    {
        VRCPhysBone[] pbs = new VRCPhysBone[targetObjects.Count];
        int pb_count = 0;
        foreach (GameObject go in targetObjects)
        {
            if (go) { VRCPhysBone pb = go.GetComponent<VRCPhysBone>(); if (pb) pbs[pb_count++] = pb; }
        }

        VRCPhysBoneCollider[] colliders = new VRCPhysBoneCollider[colliderObjects.Count];
        int c_count = 0;
        foreach (GameObject go in colliderObjects)
        {
            if (go) { VRCPhysBoneCollider c = go.GetComponent<VRCPhysBoneCollider>(); if (c) colliders[c_count++] = c; }
        }
        
        foreach (VRCPhysBone pb in pbs)
        {
            pb.colliders.Clear();
            foreach (VRCPhysBoneCollider c in colliders)
            {
                pb.colliders.Add(c);
            }
        }

    }

#nullable restore

    private void OnGUI()
    {
        serializedObject.Update();

        // Header
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("m_PhysBoneCollidersBatchSetup", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.Space(10);

        // Collider Objects Section
        EditorGUILayout.HelpBox("Drag and drop GameObjects that will serve as colliders. Use the handles to reorder.", MessageType.Info);
        colliderList.DoLayoutList();

        EditorGUILayout.Space(15);

        // Target Objects Section
        EditorGUILayout.HelpBox("Drag and drop GameObjects that will receive the colliders. Use the handles to reorder.", MessageType.Info);

        // Add button to get selected GameObjects with VRCPhysBone components
        EditorGUILayout.BeginHorizontal();
        targetList.DoLayoutList();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);
        if (GUILayout.Button("Add Selected VRCPhysBone Objects", GUILayout.Height(25)))
        {
            AddSelectedPhysBoneObjects();
        }

        EditorGUILayout.Space(20);

        // Batch Setup Button

        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 14;
        buttonStyle.fontStyle = FontStyle.Bold;

        if (GUILayout.Button("Start Batch Setup", buttonStyle, GUILayout.Height(40), GUILayout.Width(200)))
        {
            AddCollider();
        }

        if (GUILayout.Button("GetComponentTest"))
        {
            DoYouHavePhysBoneComponent();
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void AddSelectedPhysBoneObjects()
    {
        // Get currently selected GameObjects in the hierarchy
        GameObject[] selectedObjects = Selection.gameObjects;

        if (selectedObjects.Length == 0)
        {
            EditorUtility.DisplayDialog("No Selection", "Please select GameObjects in the hierarchy first.", "OK");
            return;
        }

        List<GameObject> physBoneObjects = new List<GameObject>();

        // Filter objects that have VRCPhysBone components
        foreach (GameObject obj in selectedObjects)
        {
            VRCPhysBone physBone = obj.GetComponent<VRCPhysBone>();
            if (physBone != null)
            {
                physBoneObjects.Add(obj);
            }
        }

        if (physBoneObjects.Count == 0)
        {
            EditorUtility.DisplayDialog("No VRCPhysBone Found", "No selected GameObjects have VRCPhysBone components.", "OK");
            return;
        }

        // Add the filtered objects to the target list
        serializedObject.Update();

        foreach (GameObject physBoneObj in physBoneObjects)
        {
            // Add new element to the target list
            targetProperty.arraySize++;
            var newElement = targetProperty.GetArrayElementAtIndex(targetProperty.arraySize - 1);
            newElement.objectReferenceValue = physBoneObj;
        }

        serializedObject.ApplyModifiedProperties();

        EditorUtility.DisplayDialog("Success", $"Added {physBoneObjects.Count} GameObjects with VRCPhysBone components to the target list.", "OK");
    }

    private void StartBatchSetup()
    {
        serializedObject.Update();

        // Get the actual GameObject lists from the serialized property
        List<GameObject> validColliders = new List<GameObject>();
        List<GameObject> validTargets = new List<GameObject>();

        for (int i = 0; i < colliderProperty.arraySize; i++)
        {
            var element = colliderProperty.GetArrayElementAtIndex(i);
            if (element.objectReferenceValue != null)
            {
                validColliders.Add((GameObject)element.objectReferenceValue);
            }
        }

        for (int i = 0; i < targetProperty.arraySize; i++)
        {
            var element = targetProperty.GetArrayElementAtIndex(i);
            if (element.objectReferenceValue != null)
            {
                validTargets.Add((GameObject)element.objectReferenceValue);
            }
        }

        if (validColliders.Count == 0)
        {
            EditorUtility.DisplayDialog("Error", "Please add at least one Collider GameObject.", "OK");
            return;
        }

        if (validTargets.Count == 0)
        {
            EditorUtility.DisplayDialog("Error", "Please add at least one Target GameObject.", "OK");
            return;
        }

        // TODO: Implement the actual batch setup logic here
        Debug.Log($"Starting batch setup for {validTargets.Count} targets with {validColliders.Count} colliders");

        EditorUtility.DisplayDialog("Success", $"Batch setup completed for {validTargets.Count} targets with {validColliders.Count} colliders.", "OK");
    }

    // Temporary container class for serialization
    private class TempContainer : ScriptableObject
    {
        public List<GameObject> colliderObjects = new List<GameObject>();
        public List<GameObject> targetObjects = new List<GameObject>();
    }
}