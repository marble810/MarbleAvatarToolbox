using UnityEngine;
using UnityEditor;
using UnityEngine.Animations;
using System.Collections.Generic;
using System.IO;
using UnityEditor.Animations;
using System;

namespace marble810.AvatarTools.PoseMenuMaker.Utils
{
    public static class AnimatorFactory
    {

        private static string paramName = "PoseIndex";

        private static AnimationClip emptyClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Domain.Path.EmptyClipPath);

        private static void SetTransSettings(AnimatorStateTransition targetTransition, int paramValue)
        {

            targetTransition.exitTime = 0f;
            targetTransition.duration = 0f;
            targetTransition.hasExitTime = false;

            targetTransition.AddCondition(AnimatorConditionMode.Equals, paramValue, paramName);
        }

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

                rootStateMachine.anyStatePosition = new Vector2(100, 600);

                //AddParam
                controller.AddParameter(paramName, AnimatorControllerParameterType.Int);

                //AddEmpty
                AnimatorState emptyState = rootStateMachine.AddState(emptyClip.name);
                emptyState.motion = emptyClip;
                AnimatorStateTransition emptyTrans = rootStateMachine.AddAnyStateTransition(emptyState);
                SetTransSettings(emptyTrans,0);
                //Add的第一个State默认连线了，不需要再连线


            for (int i = 0; i < clips.Count; i++)
            {
                var clip = clips[i];
                if (clip == null) continue;
                AnimatorState newState = rootStateMachine.AddState(clip.name);
                newState.motion = clip;
                AnimatorStateTransition newStateTransition = rootStateMachine.AddAnyStateTransition(newState);
                SetTransSettings(newStateTransition, i+1);
            }


            }

            EditorUtility.SetDirty(controller);
            Debug.Log("Create Animator Success at " + assetPath);

            return controller;
        }
    }
}