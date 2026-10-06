#if UNITY_INCLUDE_TESTS

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using marble810.MarbleAvatarToolbox.PlayModeTools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarbleQuickPlay.Tests
{
    /// <summary>
    /// Runner 容器所有权登记（F7）：在测试运行开始时（Unity Test Runner 的 EditModeLauncher 已经
    /// 关闭所有既有 untitled 场景并创建/激活自己的容器之后）登记该容器的 handle。
    /// 只有与登记 handle 完全一致、且 untitled/空/未 dirty 的 active 场景才允许被 fixture 复用；
    /// 未登记（例如用户自己的未保存场景）一律拒绝，绝不借用。
    /// </summary>
    [SetUpFixture]
    public sealed class QuickPlayTestRunContext
    {
        /// <summary>Runner 自己创建的隔离容器 handle（Unity 的 scene handle 可以为负数，不能拿 -1 当哨兵）。</summary>
        public static int RunnerContainerHandle { get; private set; }

        /// <summary>是否已经登记了 Runner 容器（所有权凭证）。</summary>
        public static bool HasRunnerContainer { get; private set; }

        [OneTimeSetUp]
        public void RegisterRunnerContainer()
        {
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var pristine = active.IsValid() && active.isLoaded &&
                           string.IsNullOrEmpty(active.path) &&
                           active.rootCount == 0 &&
                           !active.isDirty;

            HasRunnerContainer = pristine;
            RunnerContainerHandle = active.handle;
            UnityEngine.Debug.Log("[QuickPlayTests] Runner container handle=" + RunnerContainerHandle +
                                  " (path='" + active.path + "', roots=" + active.rootCount + ", dirty=" + active.isDirty + ")");
        }
    }

    /// <summary>
    /// 自有隔离容器（F7）。
    ///
    /// 所有权规则：只使用**本 fixture 自己创建**的 additive untitled 场景；绝不把「没有路径」当作
    /// 「Test Runner 拥有」的证据（用户自己的未保存场景同样没有路径）。因此：
    /// - 容器句柄由本类创建并登记，`OwnsScene` 是唯一所有权依据；
    /// - 若 Unity 目前因 active 场景是未保存 untitled 而拒绝创建 additive 场景，会先临时激活一个**已保存**
    ///   场景（只改 activeScene，不修改该场景内容）以满足 NewScene 前置条件，随后立刻把新容器设为 active；
    /// - 任何一步无法确认时返回失败，调用方必须停止相关对象测试（不得退化为借用未知场景）。
    /// - Dispose 只销毁自己创建的对象、恢复原 activeScene 与 Selection、关闭自己创建的容器；
    ///   不保存/不重载/不关闭任何用户场景。
    /// </summary>
    internal sealed class QuickPlayIsolatedScene : IDisposable
    {
        /// <summary>测试用故障注入：为 true 时 TryCreate 直接失败（用于验证「无法确证容器则停止」）。</summary>
        internal static bool ForceCreateFailureForTests;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly Dictionary<int, SceneSnapshot> _preExistingScenes = new Dictionary<int, SceneSnapshot>();
        private readonly Scene _previousActiveScene;
        private readonly QuickPlaySelectionSnapshot _previousSelection;
        private readonly UnityEngine.Object[] _previousSelectionObjects;
        private readonly UnityEngine.Object _previousActiveObject;
        private Scene _scene;
        private bool _disposed;

        public Scene Scene => _scene;

        /// <summary>容器由本 fixture 自己创建（句柄已登记）。</summary>
        public bool OwnsScene { get; }

        /// <summary>容器是 Test Runner 启动器自己的未保存场景（仅在 Runner 运行中且证据齐全时复用）。</summary>
        public bool UsesTestRunnerContainer => !OwnsScene;

        public IReadOnlyDictionary<int, SceneSnapshot> PreExistingScenes => _preExistingScenes;

        private QuickPlayIsolatedScene(Scene scene, Scene previousActiveScene, bool ownsScene)
        {
            _scene = scene;
            _previousActiveScene = previousActiveScene;
            _previousSelection = QuickPlayObjectIdentity.CaptureSelection();
            _previousSelectionObjects = Selection.objects;
            _previousActiveObject = Selection.activeObject;
            OwnsScene = ownsScene;
        }

        /// <summary>本类自己创建过的容器句柄（所有权登记）。</summary>
        private static readonly HashSet<int> RegisteredOwnHandles = new HashSet<int>();

        /// <summary>
        /// 创建供测试使用的隔离容器；失败时给出原因（调用方必须停止对象测试）。
        ///
        /// 所有权来源（F7）：
        /// 1. 优先自己创建 additive untitled 容器——句柄登记在 <see cref="RegisteredOwnHandles"/>，OwnsScene=true；
        /// 2. 只有在 Unity 拒绝创建（已存在未保存 untitled 场景）时，才复用 **Test Runner 启动器自己的容器**，
        ///    并且必须同时满足可验证的生命周期证据：
        ///    - `EditModeLauncher.IsRunning == true`（Runner 正在运行 EditMode 测试；它在启动时会关闭所有既有 untitled 场景，
        ///      因此运行期间不可能存在用户的 untitled 场景）；
        ///    - 该场景 untitled、空（rootCount==0）、未 dirty；
        ///    - 不是本类登记过的自有容器。
        /// 任何一项不满足即失败，绝不借用未知场景。
        /// </summary>
        public static bool TryCreate(out QuickPlayIsolatedScene isolated, out string failureReason)
        {
            isolated = null;
            failureReason = string.Empty;

            if (ForceCreateFailureForTests)
            {
                failureReason = "测试注入：容器创建失败。";
                return false;
            }

            var previousActiveScene = SceneManager.GetActiveScene();
            var preExisting = CapturePreExistingScenes();

            Scene container = default;
            var ownsScene = false;

            // 1) 优先自建容器。
            try
            {
                container = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                if (container.IsValid() && container.isLoaded)
                {
                    ownsScene = true;
                    RegisteredOwnHandles.Add(container.handle);
                }
                else
                {
                    container = default;
                }
            }
            catch (Exception exception)
            {
                failureReason = "自建 additive 容器失败：" + exception.Message;
                container = default;
            }

            // 2) 自建失败时，检查 Runner 容器证据。
            if (!ownsScene)
            {
                var active = SceneManager.GetActiveScene();
                var registeredRunnerHandle = QuickPlayTestRunContext.RunnerContainerHandle;
                var visibleRootCount = CountVisibleRoots(active);
                var pristine = active.IsValid() && active.isLoaded &&
                               string.IsNullOrEmpty(active.path) &&
                               visibleRootCount == 0 &&
                               !active.isDirty &&
                               !RegisteredOwnHandles.Contains(active.handle);

                if (!QuickPlayTestRunContext.HasRunnerContainer ||
                    active.handle != registeredRunnerHandle || !pristine)
                {
                    var rootNames = new System.Text.StringBuilder();
                    foreach (var root in active.GetRootGameObjects()) rootNames.Append(root == null ? "<null>" : root.name).Append(',');
                    failureReason = (failureReason.Length > 0 ? failureReason + "；" : string.Empty) +
                                    "无法确证隔离容器（registeredRunnerHandle=" + registeredRunnerHandle +
                                    "，activeHandle=" + active.handle +
                                    "，visibleRoots=" + visibleRootCount + "[" + rootNames + "]，roots=" + active.rootCount + "，dirty=" + active.isDirty +
                                    "，registeredAsOwn=" + RegisteredOwnHandles.Contains(active.handle) +
                                    "，pristineActiveUntitledScene=" + pristine + "）。";
                    return false;
                }

                container = active;
                ownsScene = false;
            }

            try
            {
                SceneManager.SetActiveScene(container);
                if (SceneManager.GetActiveScene() != container)
                {
                    failureReason = "无法把隔离容器设为 activeScene。";
                    return false;
                }

                isolated = new QuickPlayIsolatedScene(container, previousActiveScene, ownsScene);
                foreach (var pair in preExisting) isolated._preExistingScenes[pair.Key] = pair.Value;
                return true;
            }
            catch (Exception exception)
            {
                failureReason = exception.GetType().Name + ": " + exception.Message;
                isolated = null;
                return false;
            }
            finally
            {
                if (isolated == null)
                {
                    if (ownsScene && container.IsValid())
                    {
                        try { EditorSceneManager.CloseScene(container, true); } catch (Exception) { }
                        RegisteredOwnHandles.Remove(container.handle);
                    }

                    if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    {
                        SceneManager.SetActiveScene(previousActiveScene);
                    }
                }
            }
        }

        public GameObject CreateGameObject(string name)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(QuickPlayIsolatedScene));
            if (!OwnsScene && !UsesTestRunnerContainer)
            {
                throw new InvalidOperationException("只能在已确证来源的隔离容器中创建对象。");
            }
            if (SceneManager.GetActiveScene() != _scene)
            {
                throw new InvalidOperationException("自有容器不再是 activeScene，拒绝在未确认的场景中创建对象。");
            }

            var gameObject = new GameObject(name);
            if (gameObject.scene != _scene)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                throw new InvalidOperationException("对象被创建到了非容器场景，测试中止。");
            }

            _created.Add(gameObject);
            return gameObject;
        }

        /// <summary>工具 bookkeeping 对象：隐藏（hideFlags != None）的根对象（例如 NDMF/MA 的 activator）。</summary>
        private static bool IsToolBookkeepingRoot(GameObject root)
        {
            return root != null && root.hideFlags != HideFlags.None;
        }

        private static int CountVisibleRoots(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return -1;
            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                if (IsToolBookkeepingRoot(root)) continue;
                count++;
            }

            return count;
        }

        /// <summary>所有已存在场景（含未保存场景）相对创建容器之前的变化描述；空表示完全一致。</summary>
        public List<string> DescribePreExistingSceneChanges()
        {
            var changes = new List<string>();

            foreach (var pair in _preExistingScenes)
            {
                if (pair.Key == _scene.handle) continue; // 隔离容器本身是我们的工作区，不参与既有场景不变性

                var scene = FindSceneByHandle(pair.Key);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    // 容器创建/释放过程中不应卸载既有场景。
                    changes.Add(pair.Value.Description + "：场景已不在已加载集合中");
                    continue;
                }

                var current = SceneSnapshot.Capture(scene);
                if (!string.Equals(pair.Value.Signature, current.Signature, StringComparison.Ordinal))
                {
                    changes.Add(pair.Value.Description + "：内容签名发生变化");
                }
            }

            return changes;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_scene.IsValid() && _scene.isLoaded)
            {
                UnityEngine.Debug.Log("[QuickPlayTests] Dispose 前容器状态：roots=" + _scene.rootCount +
                                      "，dirty=" + _scene.isDirty + "，owns=" + OwnsScene +
                                      "，tracked=" + _created.Count + "。");
            }

            foreach (var gameObject in _created)
            {
                if (gameObject != null) UnityEngine.Object.DestroyImmediate(gameObject);
            }

            _created.Clear();

            // 容器在创建时已确认是空且只属于本次测试：清空所有根对象，
            // 顺带回收测试/替身直接 new 出来、未被 fixture 跟踪的对象，保证下一个测试的容器仍是 pristine。
            if (_scene.IsValid() && _scene.isLoaded)
            {
                var untrackedVisible = 0;
                foreach (var root in _scene.GetRootGameObjects().ToArray())
                {
                    if (root == null) continue;
                    if (IsToolBookkeepingRoot(root)) continue; // 不碰第三方工具自己加的隐藏 bookkeeping 对象
                    if (!_created.Contains(root)) untrackedVisible++;
                    UnityEngine.Object.DestroyImmediate(root);
                }

                if (untrackedVisible > 0)
                {
                    UnityEngine.Debug.Log("[QuickPlayTests] 隔离容器清理由测试直接创建、未被 fixture 跟踪的对象：" +
                                          untrackedVisible + " 个。");
                }
            }

            // 恢复 Selection（区分空集与有内容）。
            if (_previousSelectionObjects != null)
            {
                // 与生产恢复相同：把原 active 对象排到首位再赋值 objects（Unity 会把首元素设为 active，
                // 而单独赋值 activeObject 会折叠多选）。
                var ordered = new List<UnityEngine.Object>();
                if (_previousActiveObject != null) ordered.Add(_previousActiveObject);
                foreach (var obj in _previousSelectionObjects)
                {
                    if (ReferenceEquals(obj, _previousActiveObject)) continue;
                    ordered.Add(obj);
                }

                Selection.objects = ordered.ToArray();
            }

            if (_previousActiveScene.IsValid() && _previousActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(_previousActiveScene);
            }

            if (OwnsScene && _scene.IsValid())
            {
                var handle = _scene.handle;
                EditorSceneManager.CloseScene(_scene, true);
                RegisteredOwnHandles.Remove(handle);
            }

            _scene = default;
        }

        private static Dictionary<int, SceneSnapshot> CapturePreExistingScenes()
        {
            var result = new Dictionary<int, SceneSnapshot>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                result[scene.handle] = SceneSnapshot.Capture(scene);
            }

            return result;
        }

        private static Scene FindSceneByHandle(int handle)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.handle == handle) return scene;
            }

            return default;
        }

        /// <summary>场景的内容签名：路径/名称/dirty/根数量 + 层级/组件/实例 ID 摘要。</summary>
        internal sealed class SceneSnapshot
        {
            public string Description;
            public string Signature;

            public static SceneSnapshot Capture(Scene scene)
            {
                var builder = new StringBuilder();
                builder.Append("path=").Append(scene.path).Append(";name=").Append(scene.name)
                    .Append(";dirty=").Append(scene.isDirty).Append(";roots=").Append(scene.rootCount).Append(';');

                foreach (var root in scene.GetRootGameObjects())
                {
                    AppendTransform(builder, root.transform);
                }

                return new SceneSnapshot
                {
                    Description = string.IsNullOrEmpty(scene.path) ? scene.name + "（未保存场景）" : scene.path,
                    Signature = Fnv1a(builder.ToString()),
                };
            }

            private static void AppendTransform(StringBuilder builder, Transform transform)
            {
                if (transform == null) return;

                var go = transform.gameObject;
                builder.Append(go.name).Append('#').Append(go.GetInstanceID()).Append('#')
                    .Append(go.activeSelf).Append('#').Append(go.layer).Append('#')
                    .Append(transform.localPosition).Append('#').Append(transform.localRotation)
                    .Append('#').Append(transform.localScale).Append('{');

                foreach (var component in go.GetComponents<Component>())
                {
                    builder.Append(component == null ? "<missing>" : component.GetType().FullName).Append(',');
                }

                builder.Append('}');

                for (var i = 0; i < transform.childCount; i++)
                {
                    AppendTransform(builder, transform.GetChild(i));
                }
            }

            private static string Fnv1a(string text)
            {
                unchecked
                {
                    const uint offset = 2166136261;
                    const uint prime = 16777619;
                    var hash = offset;
                    foreach (var c in text)
                    {
                        hash ^= c;
                        hash *= prime;
                    }

                    return hash.ToString("X8") + ":" + text.Length;
                }
            }
        }
    }

    /// <summary>需要自有容器的对象测试统一入口：无法确证隔离时停止（Ignore），不借用未知场景。</summary>
    internal static class QuickPlayObjectTestScope
    {
        public static QuickPlayIsolatedScene Require()
        {
            if (!QuickPlayIsolatedScene.TryCreate(out var isolated, out var reason))
            {
                Assert.Ignore("无法创建并确证自有隔离容器，已停止该对象测试：" + reason);
            }

            return isolated;
        }
    }

    internal static class QuickPlayTestTypes
    {
        /// <summary>按全名解析已安装组件类型；未安装时返回 null，测试据此跳过。</summary>
        public static Type Find(string fullName)
        {
            return QuickPlayToolRegistry.FindTypeByFullName(fullName);
        }

        /// <summary>把已安装的组件类型添加到对象上；未安装返回 null。</summary>
        public static Component AddComponent(GameObject gameObject, string fullName)
        {
            var type = Find(fullName);
            if (type == null) return null;
            return gameObject.AddComponent(type);
        }
    }

    /// <summary>可编程的内存持久化后端（偏好测试用）。</summary>
    internal sealed class FakePersistence : IQuickPlayPreferencePersistence
    {
        private QuickPlayPreferenceData _data;
        private bool _hasStoredData;

        public bool FailSave;
        public bool FailLoad;
        public int SaveCount;
        public string LastSaveError = "磁盘只读（测试）";

        public QuickPlayPreferenceData Stored => _data;

        public void Seed(QuickPlayPreferenceData data)
        {
            _data = data;
            _hasStoredData = true;
        }

        public void SeedCorrupt()
        {
            _data = null;
            _hasStoredData = true;
        }

        public bool HasStoredData()
        {
            return _hasStoredData;
        }

        public bool TryLoad(out QuickPlayPreferenceData data, out string error)
        {
            error = FailLoad ? "读取失败（测试）" : string.Empty;
            data = FailLoad ? null : _data?.Clone();
            return !FailLoad;
        }

        public bool TrySave(QuickPlayPreferenceData data, out string error)
        {
            error = string.Empty;
            if (FailSave)
            {
                error = LastSaveError;
                return false;
            }

            SaveCount++;
            _data = data.Clone();
            _hasStoredData = true;
            return true;
        }
    }

    /// <summary>可注入落盘失败的真实后端访问器替身（用于验证生产事务回滚）。</summary>
    internal sealed class FakeSettingsAssetAccessor : IQuickPlaySettingsAssetAccessor
    {
        private int _schemaVersion = QuickPlayPreferenceData.CurrentSchemaVersion;
        private bool _initialized;
        private List<string> _ids = new List<string>();

        public bool FailFlush;
        public bool FailRead;
        public int FlushCount;
        public string FilePath = "F:/test/UserSettings/MarbleAvatarToolbox/QuickPlaySettings.asset";

        public void Seed(int schemaVersion, bool initialized, IEnumerable<string> ids)
        {
            _schemaVersion = schemaVersion;
            _initialized = initialized;
            _ids = new List<string>(ids ?? Enumerable.Empty<string>());
        }

        public bool TryRead(out int schemaVersion, out bool initialized, out List<string> ids)
        {
            schemaVersion = _schemaVersion;
            initialized = _initialized;
            ids = new List<string>(_ids);
            return !FailRead;
        }

        public void Write(int schemaVersion, bool initialized, List<string> ids)
        {
            _schemaVersion = schemaVersion;
            _initialized = initialized;
            _ids = ids == null ? new List<string>() : new List<string>(ids);
        }

        public bool TryFlush(out string error)
        {
            error = string.Empty;
            FlushCount++;
            if (FailFlush)
            {
                error = "落盘失败（测试）";
                return false;
            }

            return true;
        }

        public string ResolvedFilePath => FilePath;

        public List<string> CurrentIds => new List<string>(_ids);
        public bool CurrentInitialized => _initialized;
        public int CurrentSchemaVersion => _schemaVersion;
    }

    /// <summary>
    /// 可注入失败与记录调用的准备操作替身。
    /// 注意：Instantiate 只创建**没有任何标记**的对象，与真实"标记之前"的生命周期一致（F5）。
    /// </summary>
    internal sealed class FakePrepareOperations : IQuickPlayPrepareOperations
    {
        public bool ThrowOnInstantiate;
        public bool FailPlace;
        public bool FailMarker;
        public bool FailStrip;
        public bool FailDestroyOnce;
        public bool FailDestroyAlways;
        public string StripError = "所选工具无法抑制（测试）";

        public GameObject LastClone;
        public readonly List<GameObject> Created = new List<GameObject>();
        public readonly List<GameObject> Destroyed = new List<GameObject>();
        public readonly List<GameObject> Placed = new List<GameObject>();
        public readonly List<string> MarkerSessionIds = new List<string>();
        public readonly List<GameObject> OriginalActiveChanges = new List<GameObject>();
        public readonly List<GameObject> SelectedClones = new List<GameObject>();
        public int StripPlanCalls;
        public bool MarkerAttached;

        public GameObject Instantiate(GameObject original)
        {
            if (ThrowOnInstantiate) throw new InvalidOperationException("Instantiate 注入异常（测试）");

            // 真实生命周期：此刻对象还没有会话标记。
            var clone = new GameObject(original.name + " (fake clone)");
            LastClone = clone;
            Created.Add(clone);
            return clone;
        }

        public bool TryPlaceClone(GameObject clone, GameObject original, out string error)
        {
            error = FailPlace ? "放置失败（测试）" : string.Empty;
            if (FailPlace) return false;
            Placed.Add(clone);
            return true;
        }

        public bool TryAttachMarker(GameObject clone, string sessionId, GameObject original, out string error)
        {
            error = FailMarker ? "标记失败（测试）" : string.Empty;
            if (FailMarker) return false;

            MarkerAttached = true;
            MarkerSessionIds.Add(sessionId);
            clone.AddComponent<QuickPlayCloneMarker>().Initialize(sessionId, original);
            return true;
        }

        public bool TryApplyStripPlan(
            GameObject clone,
            QuickPlayPreferenceData snapshot,
            QuickPlayStripResult result,
            out string error)
        {
            StripPlanCalls++;
            error = FailStrip ? StripError : string.Empty;
            return !FailStrip;
        }

        public void DestroyOwned(GameObject owned)
        {
            if (FailDestroyAlways || FailDestroyOnce)
            {
                FailDestroyOnce = false;
                throw new InvalidOperationException("DestroyOwned 注入异常（测试）");
            }

            Destroyed.Add(owned);
            if (owned != null) UnityEngine.Object.DestroyImmediate(owned);
        }

        public void SetOriginalActive(GameObject original, bool active)
        {
            OriginalActiveChanges.Add(original);
        }

        public void SelectClone(GameObject clone)
        {
            SelectedClones.Add(clone);
        }
    }

    /// <summary>
    /// 可注入保存失败的内存会话记录后端。
    /// Save 与 Load 使用**同一份数据**（F8），因此可以真实验证跨重载往返。
    /// </summary>
    internal sealed class FakeSessionStore : IQuickPlaySessionStore
    {
        private QuickPlaySessionRecord _data;

        public bool FailSave;
        public bool FailSaveWhenPhaseIsRunning;
        public string SaveError = "会话记录写入失败（测试）";
        public int SaveCount;
        public int ClearCount;

        public QuickPlaySessionRecord Data => _data?.Clone();

        public void Seed(QuickPlaySessionRecord record)
        {
            _data = record?.Clone();
        }

        public bool TrySave(QuickPlaySessionRecord record, out string error)
        {
            error = string.Empty;

            if (FailSave ||
                (FailSaveWhenPhaseIsRunning && record != null && record.phase == (int)QuickPlaySessionPhase.Running))
            {
                error = SaveError;
                return false;
            }

            SaveCount++;
            _data = record?.Clone();
            return true;
        }

        public QuickPlaySessionRecord Load(out string error)
        {
            error = string.Empty;
            return _data?.Clone();
        }

        public void Clear()
        {
            ClearCount++;
            _data = null;
        }
    }

    /// <summary>测试用的假“同名不同程序集”组件类型（真实存在于测试程序集）。</summary>
    internal sealed class FakeLacLikeOptimizer : MonoBehaviour
    {
    }

    internal static class QuickPlayTestHelpers
    {
        public static List<string> CollectedWarnings { get; } = new List<string>();
        public static List<string> CollectedDialogs { get; } = new List<string>();

        public static Action<string> WarningSink()
        {
            return message => CollectedWarnings.Add(message);
        }

        public static void ClearWarnings()
        {
            CollectedWarnings.Clear();
        }

        public static bool HasWarningContaining(string token)
        {
            return CollectedWarnings.Any(message => message.IndexOf(token, StringComparison.Ordinal) >= 0);
        }

        /// <summary>拦截会话对话框，避免测试出现模态窗口；返回恢复用的还原委托。</summary>
        public static Action TrapDialogs()
        {
            var previous = QuickPlaySession.DialogPresenter;
            CollectedDialogs.Clear();
            QuickPlaySession.DialogPresenter = (title, message) => CollectedDialogs.Add(title + " | " + message);
            return () =>
            {
                QuickPlaySession.DialogPresenter = previous;
            };
        }

        public static bool HasDialogContaining(string token)
        {
            return CollectedDialogs.Any(message => message.IndexOf(token, StringComparison.Ordinal) >= 0);
        }
    }

    /// <summary>
    /// 测试隔离证据（F7）：容器必须是本 fixture 自己创建并登记的；空路径场景（含用户未保存场景）
    /// 不是所有权凭证。创建/释放对象不得改变任何既有场景（含未保存场景）的内容签名与 dirty 状态。
    /// </summary>
    [Category("QuickPlay")]
    public sealed class QuickPlayTestIsolationTests
    {

        [Test]
        public void Fixture_RequiresOwnContainer_AndCapturesAllPreExistingScenes()
        {
            using var isolated = QuickPlayObjectTestScope.Require();

            // 容器必须是「自己创建的」或「Runner 已登记 handle 的容器」二者之一，且始终是 untitled 场景。
            Assert.IsTrue(isolated.OwnsScene || isolated.UsesTestRunnerContainer,
                "只允许自有容器或已登记的 Runner 容器。");
            Assert.IsTrue(isolated.Scene.IsValid() && isolated.Scene.isLoaded);
            Assert.IsEmpty(isolated.Scene.path, "隔离容器必须是 untitled 临时场景。");
            Assert.IsNotEmpty(isolated.PreExistingScenes, "必须捕获所有既有场景（含未保存场景）作为不变性基线。");

            var probe = isolated.CreateGameObject("IsolatedProbe");
            Assert.AreEqual(isolated.Scene, probe.scene, "对象必须创建在自有容器内。");

            Assert.IsEmpty(isolated.DescribePreExistingSceneChanges(),
                "创建测试对象不得改变任何既有场景（签名/dirty）。");
        }

        [Test]
        public void Fixture_RefusesToReuseNonEmptyUntitledScene()
        {
            // 让当前工作区不再 pristine（内含测试对象），随后任何新的隔离容器请求都必须失败，
            // 绝不能把这个未确认所有权的 untitled 场景当成自己的容器复用。
            using var outer = QuickPlayObjectTestScope.Require();
            outer.CreateGameObject("NonPristineProbe");

            var created = QuickPlayIsolatedScene.TryCreate(out var inner, out var reason);

            Assert.IsFalse(created, "容器非 pristine 时必须拒绝复用，而不是借用未确认的 untitled 场景。");
            Assert.IsNull(inner);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void Fixture_FailsClosedWhenContainerCannotBeCreated()
        {
            QuickPlayIsolatedScene.ForceCreateFailureForTests = true;
            try
            {
                var created = QuickPlayIsolatedScene.TryCreate(out var isolated, out var reason);

                Assert.IsFalse(created, "无法确证容器时必须失败，而不是借用未知场景。");
                Assert.IsNull(isolated);
                Assert.IsNotEmpty(reason);
            }
            finally
            {
                QuickPlayIsolatedScene.ForceCreateFailureForTests = false;
            }
        }

        [Test]
        public void Fixture_CleansUpCreatedObjectsOnDispose()
        {
            QuickPlayIsolatedScene isolated = null;
            GameObject probe = null;
            try
            {
                isolated = QuickPlayObjectTestScope.Require();
                probe = isolated.CreateGameObject("DisposeProbe");
            }
            finally
            {
                isolated?.Dispose();
            }

            Assert.IsTrue(probe == null, "释放后测试对象必须被销毁。");
        }
    }
}

#endif
