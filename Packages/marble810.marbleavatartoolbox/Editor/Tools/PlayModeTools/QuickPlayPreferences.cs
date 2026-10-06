using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace marble810.MarbleAvatarToolbox.PlayModeTools
{
    /// <summary>持久化的 QuickPlay 偏好（仅用户级、项目内）。</summary>
    [Serializable]
    internal sealed class QuickPlayPreferenceData
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public bool initialized;
        public List<string> stripToolIds = new List<string>();

        public QuickPlayPreferenceData Clone()
        {
            return new QuickPlayPreferenceData
            {
                schemaVersion = schemaVersion,
                initialized = initialized,
                stripToolIds = stripToolIds == null ? new List<string>() : new List<string>(stripToolIds),
            };
        }
    }

    /// <summary>偏好读写后端，便于在测试中替换为内存或可失败实现。</summary>
    internal interface IQuickPlayPreferencePersistence
    {
        /// <summary>是否存在已写入的配置文件（用于区分“首次使用”与“配置损坏”）。</summary>
        bool HasStoredData();

        /// <summary>读取配置；data 为 null 表示没有可用配置（未写入或损坏）。</summary>
        bool TryLoad(out QuickPlayPreferenceData data, out string error);

        /// <summary>写入配置；失败时返回 false 并给出原因。</summary>
        bool TrySave(QuickPlayPreferenceData data, out string error);
    }

    /// <summary>
    /// QuickPlay 偏好的内存状态与安全保存逻辑（可测试核心）。
    /// 未知历史 ID 会被保留但不参与执行；保存失败回滚内存状态。
    /// </summary>
    internal sealed class QuickPlayPreferences
    {
        private readonly IQuickPlayPreferencePersistence _persistence;
        private readonly Action<string> _warning;
        private readonly List<string> _stripToolIds = new List<string>();
        private int _schemaVersion = QuickPlayPreferenceData.CurrentSchemaVersion;

        public QuickPlayPreferences(IQuickPlayPreferencePersistence persistence, Action<string> warning = null)
        {
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _warning = warning ?? (_ => { });
        }

        /// <summary>当前选择（含未知历史 ID，保持原样）。</summary>
        public IReadOnlyList<string> SelectedToolIds => _stripToolIds;

        public bool IsSelected(string toolId)
        {
            return !string.IsNullOrEmpty(toolId) &&
                   _stripToolIds.Any(id => string.Equals(id, toolId, StringComparison.Ordinal));
        }

        /// <summary>加载配置；首次使用或损坏时使用默认值（仅 VRCFury）。</summary>
        public void Load()
        {
            _stripToolIds.Clear();
            _schemaVersion = QuickPlayPreferenceData.CurrentSchemaVersion;

            if (!_persistence.TryLoad(out var data, out var error))
            {
                if (_persistence.HasStoredData())
                {
                    _warning("QuickPlay 配置文件无法读取，已采用默认值（仅 VRCFury）。" +
                             (string.IsNullOrEmpty(error) ? string.Empty : "原因：" + error));
                }

                ApplyDefaults();
                return;
            }

            if (data == null || !data.initialized)
            {
                if (_persistence.HasStoredData())
                {
                    _warning("QuickPlay 配置缺少有效数据（可能已损坏），已采用默认值（仅 VRCFury）。");
                }

                ApplyDefaults();
                return;
            }

            _schemaVersion = MigrateSchemaVersion(data.schemaVersion);
            foreach (var id in data.stripToolIds ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (_stripToolIds.Contains(id)) continue; // 去重，保留首个
                _stripToolIds.Add(id); // 未知历史 ID 安全保留，不执行匹配
            }
        }

        /// <summary>
        /// 修改一个已登记工具的勾选状态并立即保存；保存失败时回滚并返回 false。
        /// </summary>
        public bool TrySetSelected(string toolId, bool selected, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrEmpty(toolId))
            {
                error = "工具 ID 为空。";
                return false;
            }

            var previous = new List<string>(_stripToolIds);
            var wasSelected = IsSelected(toolId);

            if (selected && !wasSelected)
            {
                _stripToolIds.Add(toolId);
            }
            else if (!selected && wasSelected)
            {
                _stripToolIds.RemoveAll(id => string.Equals(id, toolId, StringComparison.Ordinal));
            }
            else
            {
                return true; // 无变化，无需写盘
            }

            if (TrySave(out error)) return true;

            _stripToolIds.Clear();
            _stripToolIds.AddRange(previous);
            return false;
        }

        /// <summary>生成启动会话使用的不可变快照。</summary>
        public QuickPlayPreferenceData Snapshot()
        {
            return new QuickPlayPreferenceData
            {
                schemaVersion = _schemaVersion,
                initialized = true,
                stripToolIds = new List<string>(_stripToolIds),
            };
        }

        private bool TrySave(out string error)
        {
            var data = Snapshot();
            if (_persistence.TrySave(data, out error)) return true;

            _warning("QuickPlay 设置保存失败：" + error);
            return false;
        }

        private int MigrateSchemaVersion(int storedVersion)
        {
            if (storedVersion <= 0)
            {
                _warning("QuickPlay 配置缺少 schema 版本，按 v1 处理。");
                return QuickPlayPreferenceData.CurrentSchemaVersion;
            }

            if (storedVersion > QuickPlayPreferenceData.CurrentSchemaVersion)
            {
                // 与写入行为保持一致：读入后按当前支持的 schema 写回（保留未知历史 ID），不声明只读。
                _warning($"QuickPlay 配置来自更新的 schema（v{storedVersion}），已按当前版本读取；" +
                         "下次保存会以 v" + QuickPlayPreferenceData.CurrentSchemaVersion + " 写回（未知历史 ID 会原样保留）。");
                return QuickPlayPreferenceData.CurrentSchemaVersion;
            }

            return storedVersion;
        }

        private void ApplyDefaults()
        {
            foreach (var tool in QuickPlayToolRegistry.Tools)
            {
                if (tool.DefaultEnabled) _stripToolIds.Add(tool.Id);
            }
        }
    }

    /// <summary>
    /// ScriptableSingleton 后端的偏好持久化实现。
    ///
    /// 事务语义：写入前记录当前内存状态，落盘失败时把内存状态回滚到写入前，
    /// 保证「界面已回滚」与「磁盘/内存实际值」一致。
    /// </summary>
    internal sealed class QuickPlayScriptableSingletonPersistence : IQuickPlayPreferencePersistence
    {
        private readonly IQuickPlaySettingsAssetAccessor _accessor;

        public QuickPlayScriptableSingletonPersistence()
            : this(new QuickPlayScriptableSingletonAssetAccessor())
        {
        }

        internal QuickPlayScriptableSingletonPersistence(IQuickPlaySettingsAssetAccessor accessor)
        {
            _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        }

        public bool HasStoredData()
        {
            try
            {
                var path = _accessor.ResolvedFilePath;
                return !string.IsNullOrEmpty(path) && File.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool TryLoad(out QuickPlayPreferenceData data, out string error)
        {
            error = string.Empty;
            data = null;

            if (!_accessor.TryRead(out var schemaVersion, out var initialized, out var ids))
            {
                error = "无法读取用户级配置资产。";
                return false;
            }

            if (!initialized) return true;

            data = new QuickPlayPreferenceData
            {
                schemaVersion = schemaVersion,
                initialized = true,
                stripToolIds = ids ?? new List<string>(),
            };
            return true;
        }

        public bool TrySave(QuickPlayPreferenceData data, out string error)
        {
            error = string.Empty;
            if (data == null)
            {
                error = "配置数据为空。";
                return false;
            }

            // 记录写入前的内存状态，用于失败回滚。
            var hadPrevious = _accessor.TryRead(out var previousSchema, out var previousInitialized, out var previousIds);

            try
            {
                _accessor.Write(data.schemaVersion, true, data.stripToolIds);
                if (_accessor.TryFlush(out error)) return true;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
            }

            // 落盘/写入失败：把内存状态回滚到写入前，避免 UI 与磁盘策略不一致。
            if (hadPrevious)
            {
                try
                {
                    _accessor.Write(previousSchema, previousInitialized, previousIds);
                    _accessor.TryFlush(out var rollbackError);
                    if (!string.IsNullOrEmpty(rollbackError))
                    {
                        Debug.LogWarning("[QuickPlay] 回滚用户级配置内存状态时落盘失败：" + rollbackError);
                    }
                }
                catch (Exception rollbackException)
                {
                    Debug.LogWarning("[QuickPlay] 回滚用户级配置内存状态失败：" + rollbackException.Message);
                }
            }

            return false;
        }
    }
}
