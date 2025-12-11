using System;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;

namespace marble810.AvatarTools.CustomGUI
{
    public class NodeInfoRefreshScheduler : IDisposable
    {
        private IVisualElementScheduledItem _scheduleItem;
        private Action _onBeforeRefresh;
        private VisualElement _root;

        private long _intervalMs;
        private bool _isRunning;
        public bool IsRunning => _isRunning;


        public NodeInfoRefreshScheduler(Action onBeforeRefresh = null, long intervalMs = 200)
        {
            _onBeforeRefresh = onBeforeRefresh;
            _intervalMs = intervalMs;
            Debug.Log($"[Scheduler] 构造完成，intervalMs: {intervalMs}");
        }

        public void Start(VisualElement root)
        {
            if (root == null)
            {
                Debug.LogError("[Scheduler] Start 失败：root 为 null！");
            }

            Stop();

            _root = root;
            
            _scheduleItem = root.schedule.Execute(() =>
            {
                try
                {
                    _onBeforeRefresh?.Invoke();
                    ParentingNodeGUI.TriggerRefresh();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error in NodeInfoRefreshScheduler: {e}");
                }
            }).Every(_intervalMs);

            _isRunning = true;
        }

        public void Stop()
        {
            _scheduleItem?.Pause();
            _scheduleItem = null;
            _isRunning = false;
        }

        public void TriggerNow()
        {
            Debug.Log("[Scheduler]TriggerNow被调用");
            _onBeforeRefresh?.Invoke();
            ParentingNodeGUI.TriggerRefresh();
        }

        public void Dispose()
        {
            Stop();
            _onBeforeRefresh = null;
            _root = null;
        }
    }
}
