using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;

namespace marble810.MarbleAvatarToolbox.PlayModeTools
{
    /// <summary>目标 Avatar 解析结果。</summary>
    internal sealed class QuickPlayTargetResolution
    {
        public GameObject AvatarRoot;
        public string FailureReason;

        public bool Succeeded => AvatarRoot != null;
    }

    /// <summary>
    /// 选择判定所需的客观事实（与 Unity 查询解耦，便于纯逻辑测试）。
    /// </summary>
    internal sealed class QuickPlaySelectionFacts
    {
        public bool HasSelection;
        public bool SelectionIsOwnedClone;
        public bool FoundAvatarInParents;
        public bool AvatarEligible;

        /// <summary>当父链中存在 Avatar 但不可用时，给出具体原因。</summary>
        public string AvatarIneligibleReason = string.Empty;
    }

    /// <summary>
    /// 选择判定策略（纯逻辑）：
    /// - 用户明确选中了 QuickPlay 自己的 clone → 拒绝；
    /// - 用户明确选中了父链中不可用的 Avatar（资产/Prefab Stage/预览/EditorOnly）→ 拒绝，不回退；
    /// - 完全没有有效选择（无选择或不含 Avatar）→ 允许回退到当前场景唯一候选。
    /// </summary>
    internal static class QuickPlayTargetSelectionPolicy
    {
        public const string SelectedOwnedCloneMessage =
            "所选对象是 QuickPlay 自己创建的临时副本，不能作为 QuickPlay 的目标。\n\n" +
            "请先退出当前会话（或选择原始 Avatar）后再启动。";

        public static string Evaluate(QuickPlaySelectionFacts facts, out bool useFallback)
        {
            useFallback = false;
            if (facts == null)
            {
                return "内部错误：选择判定参数为空。";
            }

            if (!facts.HasSelection)
            {
                useFallback = true;
                return null;
            }

            if (facts.SelectionIsOwnedClone)
            {
                return SelectedOwnedCloneMessage;
            }

            if (facts.FoundAvatarInParents)
            {
                if (facts.AvatarEligible) return null; // 直接使用父链中的 Avatar
                return "所选对象位于不可用于 QuickPlay 的目标上：" +
                       (string.IsNullOrEmpty(facts.AvatarIneligibleReason) ? "该 Avatar 不可用。" : facts.AvatarIneligibleReason) +
                       "\n\nQuickPlay 不会回退到场景中的其它 Avatar。请选择普通已加载场景中的 Avatar。";
            }

            useFallback = true;
            return null;
        }
    }

    /// <summary>
    /// QuickPlay 目标解析：优先 Selection 父链，其次当前已加载场景中的唯一合法候选。
    /// 排除持久资产、Prefab Stage、预览场景、EditorOnly 目标与 QuickPlay 自身 clone。
    /// </summary>
    internal static class QuickPlayTargetResolver
    {
        public static QuickPlayTargetResolution Resolve(GameObject selectedObject)
        {
            var result = new QuickPlayTargetResolution();

            var avatarInParents = selectedObject == null ? null : FindAvatarInParents(selectedObject);
            var facts = new QuickPlaySelectionFacts
            {
                HasSelection = selectedObject != null,
                SelectionIsOwnedClone = QuickPlayCloneLocator.IsOwnedClone(selectedObject),
                FoundAvatarInParents = avatarInParents != null,
                AvatarEligible = avatarInParents != null && IsEligibleCandidate(avatarInParents, out var reason),
            };
            if (avatarInParents != null && !facts.AvatarEligible)
            {
                IsEligibleCandidate(avatarInParents, out reason);
                facts.AvatarIneligibleReason = reason;
            }

            var policyError = QuickPlayTargetSelectionPolicy.Evaluate(facts, out var useFallback);
            if (policyError != null)
            {
                result.FailureReason = policyError;
                return result;
            }

            if (!useFallback)
            {
                result.AvatarRoot = avatarInParents;
                return result;
            }

            var candidates = CollectSceneCandidates();
            if (candidates.Count == 1)
            {
                result.AvatarRoot = candidates[0];
                return result;
            }

            var activeCandidates = candidates.Where(candidate => candidate.activeInHierarchy).ToList();
            if (activeCandidates.Count == 1)
            {
                result.AvatarRoot = activeCandidates[0];
                return result;
            }

            result.FailureReason =
                "无法确定要使用的 Avatar。\n\n" +
                "请先选择一个带有 VRCAvatarDescriptor 的 Avatar 根对象或其子对象；" +
                "或者确保当前打开的场景中只有一个 active 且非 EditorOnly 的 Avatar。\n" +
                "持久资产、Prefab Stage 与预览场景中的对象不会被用作 QuickPlay 目标。";
            return result;
        }

        internal static GameObject FindAvatarInParents(GameObject selectedObject)
        {
            if (selectedObject == null) return null;

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

        internal static bool IsEligibleCandidate(GameObject candidate)
        {
            return IsEligibleCandidate(candidate, out _);
        }

        /// <summary>判断对象是否可作为 QuickPlay 目标，并给出不可用原因。</summary>
        internal static bool IsEligibleCandidate(GameObject candidate, out string reason)
        {
            reason = string.Empty;
            if (candidate == null)
            {
                reason = "目标为空。";
                return false;
            }

            var scene = candidate.scene;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                reason = "目标不在已加载的场景中（可能是持久资产或未加载对象）。";
                return false;
            }

            if (EditorSceneManager.IsPreviewScene(scene) || EditorSceneManager.IsPreviewSceneObject(candidate))
            {
                reason = "目标是预览场景对象。";
                return false;
            }

            if (AssetDatabase.Contains(candidate))
            {
                reason = "目标是持久资产（Prefab/Project 资产）上的对象。";
                return false;
            }

            if (PrefabStageUtility.GetPrefabStage(candidate) != null)
            {
                reason = "目标位于 Prefab Stage 中。";
                return false;
            }

            if ((candidate.hideFlags & HideFlags.HideAndDontSave) != 0)
            {
                reason = "目标带有 HideAndDontSave 标记。";
                return false;
            }

            if (string.Equals(candidate.tag, "EditorOnly", StringComparison.Ordinal))
            {
                reason = "目标被标记为 EditorOnly。";
                return false;
            }

            if (QuickPlayCloneLocator.IsOwnedClone(candidate))
            {
                reason = "目标是 QuickPlay 自己创建的临时副本。";
                return false;
            }

            return true;
        }

        private static List<GameObject> CollectSceneCandidates()
        {
            var candidates = new List<GameObject>();

            foreach (var scene in QuickPlaySceneEnumerator.LoadedEditableScenes())
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var descriptor in root.GetComponentsInChildren<VRCAvatarDescriptor>(true))
                    {
                        if (descriptor == null) continue;
                        var candidate = descriptor.gameObject;
                        if (!IsEligibleCandidate(candidate)) continue;
                        if (candidates.Contains(candidate)) continue;
                        candidates.Add(candidate);
                    }
                }
            }

            return candidates;
        }
    }

    /// <summary>
    /// clone 所有权定位。只有带匹配 sessionId 的 <see cref="QuickPlayCloneMarker"/> 才被视为本会话对象。
    /// 绝不仅凭名字或后缀判定。
    /// </summary>
    internal static class QuickPlayCloneLocator
    {
        /// <summary>该对象是否是任意 QuickPlay clone（不论会话）。</summary>
        public static bool IsOwnedClone(GameObject gameObject)
        {
            return gameObject != null && gameObject.GetComponent<QuickPlayCloneMarker>() != null;
        }

        /// <summary>
        /// 在指定场景中查找属于该会话的 clone。
        /// 调用方负责决定场景范围（生产路径只传入已加载的普通场景）。
        /// </summary>
        public static List<GameObject> FindInScene(Scene scene, string sessionId)
        {
            var result = new List<GameObject>();
            if (string.IsNullOrEmpty(sessionId)) return result;
            if (!scene.IsValid() || !scene.isLoaded) return result;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var marker in root.GetComponentsInChildren<QuickPlayCloneMarker>(true))
                {
                    if (marker == null) continue;
                    if (!string.Equals(marker.SessionId, sessionId, StringComparison.Ordinal)) continue;
                    if (result.Contains(marker.gameObject)) continue;
                    result.Add(marker.gameObject);
                }
            }

            return result;
        }

        /// <summary>在所有已加载的普通场景中查找属于该会话的 clone（不搜索预览场景）。</summary>
        public static List<GameObject> FindAll(string sessionId)
        {
            var result = new List<GameObject>();
            if (string.IsNullOrEmpty(sessionId)) return result;

            foreach (var scene in QuickPlaySceneEnumerator.LoadedEditableScenes())
            {
                result.AddRange(FindInScene(scene, sessionId));
            }

            return result;
        }
    }

    /// <summary>
    /// 原对象引用解析：仅使用 clone 标记中的直接引用与 durable GlobalObjectId。
    /// 身份失效时返回 null（调用方告警并跳过），绝不按名字/兄弟序号猜测。
    /// </summary>
    internal static class QuickPlayOriginalReference
    {
        public static GameObject Resolve(
            QuickPlaySessionRecord record,
            IReadOnlyList<GameObject> ownedClones,
            out string failureReason)
        {
            failureReason = string.Empty;

            // 1) clone 标记中的直接引用（同一会话最可靠）。
            if (ownedClones != null)
            {
                foreach (var clone in ownedClones)
                {
                    if (clone == null) continue;
                    var marker = clone.GetComponent<QuickPlayCloneMarker>();
                    if (marker == null || marker.OriginalAvatar == null) continue;
                    return marker.OriginalAvatar;
                }
            }

            // 2) durable GlobalObjectId。
            if (record != null && !string.IsNullOrEmpty(record.originalGlobalId))
            {
                var resolved = QuickPlayObjectIdentity.TryResolve(record.originalGlobalId, out var identityError) as GameObject;
                if (resolved != null) return resolved;
                failureReason = "原对象身份已失效（" + identityError + "）。";
                return null;
            }

            failureReason = "会话记录没有可用的原对象身份（该对象所在场景无法生成 durable GlobalObjectId），" +
                            "QuickPlay 不会按名字猜测对象。";
            return null;
        }
    }
}
