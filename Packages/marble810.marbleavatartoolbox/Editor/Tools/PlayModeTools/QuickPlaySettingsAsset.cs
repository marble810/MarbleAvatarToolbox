using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace marble810.MarbleAvatarToolbox.PlayModeTools
{
    /// <summary>
    /// 用户级配置资产的读写访问。分离「内存状态」与「落盘」，便于在生产事务中回滚并做失败注入测试。
    /// </summary>
    internal interface IQuickPlaySettingsAssetAccessor
    {
        bool TryRead(out int schemaVersion, out bool initialized, out List<string> ids);

        /// <summary>只写内存状态，不落盘。</summary>
        void Write(int schemaVersion, bool initialized, List<string> ids);

        /// <summary>把当前内存状态落盘。</summary>
        bool TryFlush(out string error);

        /// <summary>配置文件的绝对路径（用于存在性检查）。</summary>
        string ResolvedFilePath { get; }
    }

    /// <summary>
    /// 项目内用户级配置资产。落盘位置为 &lt;项目&gt;/UserSettings/MarbleAvatarToolbox/QuickPlaySettings.asset。
    /// 不保存 Avatar 引用、临时对象或第三方类型；不写入场景与 Package 资产。
    /// </summary>
    [FilePath(QuickPlaySettingsAsset.RelativeAssetPath, FilePathAttribute.Location.ProjectFolder)]
    internal sealed class QuickPlaySettingsAsset : ScriptableSingleton<QuickPlaySettingsAsset>
    {
        /// <summary>相对于项目根目录的配置路径（同时用于落盘与存在性检查）。</summary>
        internal const string RelativeAssetPath = "UserSettings/MarbleAvatarToolbox/QuickPlaySettings.asset";

        public int schemaVersion = QuickPlayPreferenceData.CurrentSchemaVersion;
        public bool initialized;
        public List<string> stripToolIds = new List<string>();

        /// <summary>实际落盘路径（ScriptableSingleton 保护成员封装）。</summary>
        internal static string StoredFilePath => GetFilePath();

        /// <summary>
        /// 绝对化的落盘路径。ScriptableSingleton 返回的是相对项目根目录的路径，
        /// 直接用于 File.Exists 会受编辑器当前工作目录影响，所以这里显式拼上项目根目录。
        /// </summary>
        internal static string ResolvedStoredFilePath
        {
            get
            {
                var raw = GetFilePath();
                if (string.IsNullOrEmpty(raw)) return raw;
                if (Path.IsPathRooted(raw)) return raw;

                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                return string.IsNullOrEmpty(projectRoot) ? raw : Path.GetFullPath(Path.Combine(projectRoot, raw));
            }
        }

        /// <summary>写入配置（ScriptableSingleton 保护成员封装）。</summary>
        internal void SaveNow()
        {
            Save(true);
        }
    }

    /// <summary>基于 <see cref="QuickPlaySettingsAsset"/> 的生产访问器。</summary>
    internal sealed class QuickPlayScriptableSingletonAssetAccessor : IQuickPlaySettingsAssetAccessor
    {
        public bool TryRead(out int schemaVersion, out bool initialized, out List<string> ids)
        {
            schemaVersion = QuickPlayPreferenceData.CurrentSchemaVersion;
            initialized = false;
            ids = new List<string>();

            try
            {
                var asset = QuickPlaySettingsAsset.instance;
                if (asset == null) return false;

                schemaVersion = asset.schemaVersion;
                initialized = asset.initialized;
                ids = asset.stripToolIds == null ? new List<string>() : new List<string>(asset.stripToolIds);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[QuickPlay] 读取用户级配置失败：" + exception.Message);
                return false;
            }
        }

        public void Write(int schemaVersion, bool initialized, List<string> ids)
        {
            var asset = QuickPlaySettingsAsset.instance;
            asset.schemaVersion = schemaVersion;
            asset.initialized = initialized;
            asset.stripToolIds = ids == null ? new List<string>() : new List<string>(ids);
        }

        public bool TryFlush(out string error)
        {
            error = string.Empty;
            try
            {
                var asset = QuickPlaySettingsAsset.instance;
                asset.SaveNow();

                // 不只把"不抛异常"当成功：落盘后按绝对路径读回并校验内容。
                var path = ResolvedFilePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    error = "落盘后找不到配置文件：" + path;
                    return false;
                }

                var text = File.ReadAllText(path);
                var expectedIds = asset.stripToolIds ?? new List<string>();
                if (expectedIds.Count == 0)
                {
                    if (text.IndexOf("stripToolIds: []", StringComparison.Ordinal) < 0)
                    {
                        error = "落盘内容未包含空列表标记（stripToolIds: []），无法确认写入生效。";
                        return false;
                    }
                }
                else
                {
                    foreach (var id in expectedIds)
                    {
                        if (text.IndexOf("- " + id, StringComparison.Ordinal) < 0)
                        {
                            error = "落盘内容缺少工具 ID：" + id;
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        public string ResolvedFilePath
        {
            get
            {
                try
                {
                    return QuickPlaySettingsAsset.ResolvedStoredFilePath;
                }
                catch (Exception)
                {
                    return string.Empty;
                }
            }
        }
    }
}
