using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.config;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;

namespace marble810.MarbleAvatarTools.PlayModeTools
{
    [InitializeOnLoad]
    internal static class PlayWithoutVRCFury
    {
        private const string PlayMenuPath = "MarbleAvatarTools/Play Without VRCFury";
        private const string ContextMenuRoot = "GameObject/marbleTools/VRCFury-Free Avatar/";
        private const string PlayContextMenuPath = ContextMenuRoot + "Play Without VRCFury";
        private const string PlayCloneSuffix = " (No VRCFury Play Clone)";

        private sealed class SessionState
        {
            public GameObject OriginalAvatar;
            public GameObject CloneAvatar;
            public bool OriginalActiveSelf;
            public bool IsPrepared;
            public bool PreprocessCompleted;
        }

        private static readonly SessionState Session = new SessionState();

        static PlayWithoutVRCFury()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem(PlayMenuPath)]
        private static void StartFromMenu()
        {
            StartForSelection(Selection.activeGameObject);
        }

        [MenuItem(PlayMenuPath, true)]
        private static bool ValidateStartFromMenu()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem(PlayContextMenuPath, false, 0)]
        private static void StartFromContext(MenuCommand command)
        {
            StartForSelection(command.context as GameObject);
        }

        [MenuItem(PlayContextMenuPath, true)]
        private static bool ValidateStartFromContext(MenuCommand command)
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && command.context is GameObject;
        }

        private static void StartForSelection(GameObject selectedObject)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ShowDialog("无法开始", "当前已经处于进入 Play 的流程中。", "确定");
                return;
            }

            CleanupStalePlayClones();

            if (Session.IsPrepared || Session.CloneAvatar != null)
            {
                ShowDialog("会话已存在", "当前已经准备或运行了一个 Play Without VRCFury 会话。请先退出当前会话。", "确定");
                return;
            }

            if (!TryPrepareClone(selectedObject, PlayCloneSuffix, out var avatarRoot, out var clone, out var removedCount))
            {
                return;
            }

            Session.OriginalAvatar = avatarRoot;
            Session.CloneAvatar = clone;
            Session.OriginalActiveSelf = avatarRoot.activeSelf;
            Session.IsPrepared = true;
            Session.PreprocessCompleted = false;

            avatarRoot.SetActive(false);
            clone.SetActive(true);
            Selection.activeGameObject = clone;

            Debug.Log($"Play Without VRCFury: prepared clone '{clone.name}', removed {removedCount} VRCFury component(s).");

            EditorApplication.EnterPlaymode();

            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                CleanupSession();
                ShowDialog("进入 Play 失败", "编辑器未能进入 Play Mode，临时会话已回滚。", "确定");
            }
        }

        private static bool TryPrepareClone(
            GameObject selectedObject,
            string cloneSuffix,
            out GameObject avatarRoot,
            out GameObject clone,
            out int removedCount)
        {
            avatarRoot = ResolveAvatarRoot(selectedObject);
            clone = null;
            removedCount = 0;

            if (avatarRoot == null)
            {
                ShowDialog(
                    "未找到 Avatar",
                    "无法确定要使用的 Avatar。\n\n" +
                    "请先选择一个带有 VRCAvatarDescriptor 的 Avatar 根对象或其子对象；" +
                    "或者确保当前打开的场景中只有一个 active 且非 EditorOnly 的 Avatar。",
                    "确定");
                return false;
            }

            clone = UnityEngine.Object.Instantiate(avatarRoot);
            clone.name = avatarRoot.name + cloneSuffix;
            clone.transform.SetParent(avatarRoot.transform.parent, false);
            clone.transform.SetSiblingIndex(avatarRoot.transform.GetSiblingIndex() + 1);
            clone.transform.localPosition = avatarRoot.transform.localPosition;
            clone.transform.localRotation = avatarRoot.transform.localRotation;
            clone.transform.localScale = avatarRoot.transform.localScale;
            removedCount = StripVRCFuryComponents(clone);
            return true;
        }

        private static GameObject ResolveAvatarRoot(GameObject selectedObject)
        {
            if (selectedObject != null)
            {
                var resolvedFromSelection = FindAvatarInParents(selectedObject);
                if (resolvedFromSelection != null)
                {
                    return resolvedFromSelection;
                }
            }

            var sceneCandidates = UnityEngine.Object.FindObjectsOfType<VRCAvatarDescriptor>(true)
                .Where(descriptor => descriptor != null)
                .Select(descriptor => descriptor.gameObject)
                .Where(IsSceneAvatarCandidate)
                .Distinct()
                .ToArray();

            if (sceneCandidates.Length == 1)
            {
                return sceneCandidates[0];
            }

            var activeCandidates = sceneCandidates
                .Where(candidate => candidate.activeInHierarchy)
                .ToArray();

            if (activeCandidates.Length == 1)
            {
                return activeCandidates[0];
            }

            return null;
        }

        private static void CleanupStalePlayClones()
        {
            var staleClones = UnityEngine.Object.FindObjectsOfType<VRCAvatarDescriptor>(true)
                .Select(descriptor => descriptor != null ? descriptor.gameObject : null)
                .Where(gameObject => gameObject != null)
                .Where(gameObject => gameObject.name.EndsWith(PlayCloneSuffix, StringComparison.Ordinal))
                .ToArray();

            foreach (var staleClone in staleClones)
            {
                UnityEngine.Object.DestroyImmediate(staleClone);
            }
        }

        private static GameObject FindAvatarInParents(GameObject selectedObject)
        {
            var current = selectedObject.transform;
            while (current != null)
            {
                if (current.GetComponent<VRCAvatarDescriptor>() != null)
                {
                    return current.gameObject;
                }

                current = current.parent;
            }

            return null;
        }

        private static bool IsSceneAvatarCandidate(GameObject gameObject)
        {
            if (gameObject == null) return false;
            if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded) return false;
            if ((gameObject.hideFlags & HideFlags.HideAndDontSave) != 0) return false;
            if (string.Equals(gameObject.tag, "EditorOnly", StringComparison.Ordinal)) return false;
            if (gameObject.name.EndsWith(PlayCloneSuffix, StringComparison.Ordinal)) return false;
            return true;
        }

        private static int StripVRCFuryComponents(GameObject cloneRoot)
        {
            var removedCount = 0;
            var components = cloneRoot.GetComponentsInChildren<Component>(true)
                .Where(component => component != null)
                .ToArray();

            foreach (var component in components)
            {
                if (component is Transform) continue;
                if (!IsVRCFuryComponent(component)) continue;

                UnityEngine.Object.DestroyImmediate(component);
                removedCount++;
            }

            return removedCount;
        }

        private static bool IsVRCFuryComponent(Component component)
        {
            var type = component.GetType();
            var assemblyName = type.Assembly.GetName().Name;
            var fullName = type.FullName ?? string.Empty;
            var typeName = type.Name;

            if (string.Equals(assemblyName, "VRCFury", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (fullName.StartsWith("VF.", StringComparison.Ordinal))
            {
                return true;
            }

            return fullName.IndexOf("VRCFury", StringComparison.OrdinalIgnoreCase) >= 0
                   || typeName.StartsWith("VRCFury", StringComparison.OrdinalIgnoreCase);
        }

        internal static void RunAvatarPreprocessOnPlay(GameObject cloneAvatar, string displayCloneName)
        {
            if (cloneAvatar == null) return;

            var originalName = cloneAvatar.name;
            var preprocessName = originalName + "(Clone)";
            var backupOriginal = UnityEngine.Object.Instantiate(cloneAvatar);
            backupOriginal.name = originalName;
            backupOriginal.SetActive(false);

            cloneAvatar.name = preprocessName;

            Debug.Log($"Play Without VRCFury: running avatar preprocess fallback for '{displayCloneName}'.");

            var success = VRCBuildPipelineCallbacks.OnPreprocessAvatar(cloneAvatar);

            if (cloneAvatar != null)
            {
                cloneAvatar.name = displayCloneName;
            }

            if (backupOriginal != null)
            {
                UnityEngine.Object.DestroyImmediate(backupOriginal);
            }

            if (!success)
            {
                Debug.LogError("Play Without VRCFury: avatar preprocess fallback failed. Exiting play mode.");
                EditorApplication.delayCall += () =>
                {
                    if (EditorApplication.isPlaying)
                    {
                        EditorApplication.ExitPlaymode();
                    }
                };
                return;
            }

            RecreateAnimators(cloneAvatar.transform);

            Debug.Log($"Play Without VRCFury: avatar preprocess fallback completed for '{displayCloneName}'.");
        }

        private static void RecreateAnimators(Transform avatarRoot)
        {
            var tempObject = new GameObject("__PlayWithoutVRCFuryAnimatorTemp");
            tempObject.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                foreach (var animator in avatarRoot.GetComponentsInChildren<Animator>(true).Reverse())
                {
                    var owner = animator.gameObject;
                    var temporaryAnimator = tempObject.AddComponent<Animator>();
                    var wasEnabled = animator.enabled;

                    var temporaryRequiredComponents = new List<Component>();
                    foreach (var requiredComponent in FindSiblingComponentsRequiringAnimator(animator))
                    {
                        var temporaryComponent = tempObject.AddComponent(requiredComponent.GetType());
                        temporaryRequiredComponents.Add(temporaryComponent);
                        EditorUtility.CopySerialized(requiredComponent, temporaryComponent);
                        UnityEngine.Object.DestroyImmediate(requiredComponent);
                    }

                    EditorUtility.CopySerialized(animator, temporaryAnimator);
                    UnityEngine.Object.DestroyImmediate(animator);

                    var recreatedAnimator = owner.AddComponent<Animator>();
                    recreatedAnimator.enabled = false;
                    EditorUtility.CopySerialized(temporaryAnimator, recreatedAnimator);
                    recreatedAnimator.enabled = wasEnabled;

                    foreach (var temporaryRequiredComponent in temporaryRequiredComponents)
                    {
                        var restoredComponent = owner.AddComponent(temporaryRequiredComponent.GetType());
                        EditorUtility.CopySerialized(temporaryRequiredComponent, restoredComponent);
                        UnityEngine.Object.DestroyImmediate(temporaryRequiredComponent);
                    }

                    UnityEngine.Object.DestroyImmediate(temporaryAnimator);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tempObject);
            }
        }

        private static IEnumerable<Component> FindSiblingComponentsRequiringAnimator(Animator animator)
        {
            return animator.GetComponents<Component>()
                .Where(component => component != null)
                .Where(component =>
                {
                    return component.GetType()
                        .GetCustomAttributes(typeof(RequireComponent), true)
                        .Cast<RequireComponent>()
                        .Any(requirement => requirement.m_Type0 == typeof(Animator)
                                            || requirement.m_Type1 == typeof(Animator)
                                            || requirement.m_Type2 == typeof(Animator));
                });
        }

        private static void EnsurePlayableRootAnimator(GameObject cloneAvatar)
        {
            if (cloneAvatar == null) return;

            var animator = cloneAvatar.GetComponent<Animator>();
            if (animator == null || animator.enabled) return;
            if (animator.avatar == null) return;
            if (animator.runtimeAnimatorController == null) return;

            var avatarIsValid = animator.avatar.isValid;
            animator.enabled = true;
            Debug.Log($"Play Without VRCFury: enabled root Animator on '{cloneAvatar.name}' for play testing. avatarValid={avatarIsValid}");
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                if (Session.IsPrepared && !Session.PreprocessCompleted && Session.CloneAvatar != null)
                {
                    Session.PreprocessCompleted = true;

                    if (!Config.ApplyOnPlay)
                    {
                        RunAvatarPreprocessOnPlay(Session.CloneAvatar, Session.CloneAvatar.name);
                    }

                    EnsurePlayableRootAnimator(Session.CloneAvatar);
                }
                return;
            }

            if (state == PlayModeStateChange.EnteredEditMode)
            {
                CleanupSession();
            }
        }

        private static void CleanupSession()
        {
            try
            {
                if (Session.CloneAvatar != null)
                {
                    UnityEngine.Object.DestroyImmediate(Session.CloneAvatar);
                }

                if (Session.OriginalAvatar != null)
                {
                    Session.OriginalAvatar.SetActive(Session.OriginalActiveSelf);
                    Selection.activeGameObject = Session.OriginalAvatar;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Play Without VRCFury cleanup failed: {exception.Message}");
            }
            finally
            {
                Session.OriginalAvatar = null;
                Session.CloneAvatar = null;
                Session.OriginalActiveSelf = false;
                Session.IsPrepared = false;
                Session.PreprocessCompleted = false;
            }
        }

        private static void ShowDialog(string title, string message, string ok)
        {
            EditorUtility.DisplayDialog(title, message, ok);
        }
    }
}