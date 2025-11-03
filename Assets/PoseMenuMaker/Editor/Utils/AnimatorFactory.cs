using UnityEngine;
using UnityEditor;
using UnityEngine.Animations;
using System.Collections.Generic;
using System.IO;
using UnityEditor.Animations;

namespace marble810.AvatarTools.PoseMenuMaker.Utils
{
    public static class AnimatorFactory
    {
        public static AnimatorController CreateAnimator(string assetPath, List<AnimationClip> clips)
        {

            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError("AnimatorControllerFactory: 创建失败，提供的路径为空。");
                return null;
            }
            if (!assetPath.EndsWith(".controller"))
            {
                Debug.LogError($"AnimatorControllerFactory: 创建失败，路径'{assetPath}'必须以.controller结尾。");
                return null;
            }
            if (clips == null || clips.Count == 0)
            {
                Debug.LogWarning("AnimatorControllerFactory: 提供的动画列表为空，将创建一个空的AnimatorController。");
            }

            string directory = Path.GetDirectoryName(assetPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(assetPath);
            if (controller == null)
            {
                Debug.LogError($"AnimatorControllerFactory: 未知错误，无法在路径'{assetPath}'创建AnimatorController。");
                return null;
            }

            var rootStateMachine = controller.layers[0].stateMachine;
            if (clips != null)
            {
                foreach (var clip in clips)
                {
                    if (clip == null) continue;
                    AnimatorState newState = rootStateMachine.AddState(clip.name);
                    newState.motion = clip;
                }
            }

            EditorUtility.SetDirty(controller);
            Debug.Log("Create Animator Success at " + assetPath);

            return controller;
        }
    }
}