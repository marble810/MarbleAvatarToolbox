using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using VRC.SDK3.Dynamics.PhysBone.Components;
using System.Drawing.Text;
using HarmonyLib;

namespace marble810.AvatarTools.PhysBoneSync
{
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
            var tempObject = ScriptableObject.CreateInstance<BatchSetupTempContainer>();
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
                    EditorGUI.LabelField(rect, "碰撞体 GameObjects");
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
                    EditorGUI.LabelField(rect, "目标动骨 GameObjects");
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

        private void AddCollider()
        {

            //Validation
            if (targetObjects.Count == 0 || colliderObjects.Count == 0)
            {
                EditorUtility.DisplayDialog("Warning", "碰撞体列表与目标列表不应为空！", "OK");
                return;
            }

            List<VRCPhysBoneCollider> validColliders = new List<VRCPhysBoneCollider>();
            foreach (var go in colliderObjects)
            {
                if (go != null)
                {
                    var collider = go.GetComponent<VRCPhysBoneCollider>();
                    if (collider != null)
                    {
                        validColliders.Add(collider);
                    }
                }
            }
            if (validColliders.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "在碰撞体列表内找不到VRCPhysBoneCollider!", "OK");
                return;
            }

            int successCount = 0;
            foreach (var targetGO in targetObjects)
            {
                if (targetGO == null) continue;
                VRCPhysBone physBone = targetGO.GetComponent<VRCPhysBone>();
                if (physBone == null) continue;

                Undo.RecordObject(physBone, "Batch Add PhysBone Colliders");

                SerializedObject physBoneSO = new SerializedObject(physBone);
                SerializedProperty collidersProp = physBoneSO.FindProperty("colliders");
                collidersProp.ClearArray();
                collidersProp.arraySize = validColliders.Count;

                for (int i = 0; i < validColliders.Count; i++)
                {
                    SerializedProperty element = collidersProp.GetArrayElementAtIndex(i);
                    element.objectReferenceValue = validColliders[i];
                }

                physBoneSO.ApplyModifiedProperties();
                successCount++;

            }

            ShowNotification(new GUIContent($"Successfully applied {validColliders.Count} colliders to {successCount} PhysBones"));


            // VRCPhysBone[] pbs = new VRCPhysBone[targetObjects.Count];
            // int pb_count = 0;
            // foreach (GameObject go in targetObjects)
            // {
            //     if (go) { VRCPhysBone pb = go.GetComponent<VRCPhysBone>(); if (pb) pbs[pb_count++] = pb; }
            // }

            // VRCPhysBoneCollider[] colliders = new VRCPhysBoneCollider[colliderObjects.Count];
            // int c_count = 0;
            // foreach (GameObject go in colliderObjects)
            // {
            //     if (go) { VRCPhysBoneCollider c = go.GetComponent<VRCPhysBoneCollider>(); if (c) colliders[c_count++] = c; }
            // }

            // foreach (VRCPhysBone pb in pbs)
            // {
            //     pb.colliders.Clear();
            //     foreach (VRCPhysBoneCollider c in colliders)
            //     {
            //         pb.colliders.Add(c);
            //     }
            // }

        }

        private void FetchPBFromSelection()
        {
            // Get currently selected GameObjects in the hierarchy
            GameObject[] selectedObjects = Selection.gameObjects;

            if (selectedObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("No Selection", "Please select GameObjects in the hierarchy first.", "OK");
                return;
            }

            List<GameObject> filteredGameObjects = new List<GameObject>();

            foreach (GameObject go in selectedObjects)
            {
                VRCPhysBone[] components = go.GetComponentsInChildren<VRCPhysBone>();
                if (components.Length != 0)
                {
                    Debug.Log($"Selected GameObject: {go.name} found {components.Length} PhysBones");
                    foreach (VRCPhysBone component in components)
                    {
                        GameObject filtered_go = component.gameObject;
                        filteredGameObjects.Add(filtered_go);
                    }
                }
            }

            Undo.RecordObject(serializedObject.targetObject, "Fetch PhysBone Objects");

            targetProperty.ClearArray();

            var uniqueFilteredObjects = new List<GameObject>();
            foreach (var obj in filteredGameObjects)
            {
                if (!uniqueFilteredObjects.Contains(obj))
                {
                    uniqueFilteredObjects.Add(obj);
                }
            }

            targetProperty.arraySize = uniqueFilteredObjects.Count;

            for (int i = 0; i < uniqueFilteredObjects.Count; i++)
            {
                SerializedProperty element = targetProperty.GetArrayElementAtIndex(i);
                element.objectReferenceValue = uniqueFilteredObjects[i];
            }

            ShowNotification(new GUIContent($"Fetched {uniqueFilteredObjects.Count} PhysBone Objs"));

        }

#nullable restore

        private void OnGUI()
        {
            serializedObject.Update();

            // Header
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("MarbleAvatarTools", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("PhysBone Collider Batch Applier", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.Space(10);

            // Collider Objects Section
            colliderList.DoLayoutList();

            EditorGUILayout.Space(15);

            // Add button to get selected GameObjects with VRCPhysBone components
            EditorGUILayout.BeginHorizontal();
            targetList.DoLayoutList();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
            if (GUILayout.Button("获取所选骨骼子级下所有动骨", GUILayout.Height(25)))
            {
                FetchPBFromSelection();
            }

            EditorGUILayout.Space(20);

            // Batch Setup Button

            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = 14;
            buttonStyle.fontStyle = FontStyle.Bold;

            if (GUILayout.Button("应用设置", buttonStyle, GUILayout.Height(40)))
            {
                AddCollider();
            }

            serializedObject.ApplyModifiedProperties();
        }



        // Temporary container class for serialization
        private class BatchSetupTempContainer : ScriptableObject
        {
            public List<GameObject> colliderObjects = new List<GameObject>();
            public List<GameObject> targetObjects = new List<GameObject>();
        }
    }
}

