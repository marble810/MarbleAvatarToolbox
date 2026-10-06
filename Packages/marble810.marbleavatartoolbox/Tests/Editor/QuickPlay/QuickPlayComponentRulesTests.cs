#if UNITY_INCLUDE_TESTS

using System.Linq;
using marble810.MarbleAvatarToolbox.PlayModeTools;
using NUnit.Framework;
using UnityEngine;

namespace MarbleQuickPlay.Tests
{
    /// <summary>
    /// 副本剔除规则测试：在隔离预览场景中构造真实组件，验证保护集合、inactive、系列覆盖与部分范围。
    /// </summary>
    [Category("QuickPlay")]
    public sealed class QuickPlayComponentRulesTests
    {
        private QuickPlayIsolatedScene _scene;

        [SetUp]
        public void SetUp()
        {
            _scene = QuickPlayObjectTestScope.Require();
        }

        [TearDown]
        public void TearDown()
        {
            _scene?.Dispose();
            _scene = null;
        }

        [Test]
        public void CollectTargets_VrcFuryOnly_KeepsOtherTools()
        {
            var root = _scene.CreateGameObject("Avatar");
            var vrcFuryComponent = QuickPlayTestTypes.AddComponent(root, "VF.Model.VRCFury");
            var aaoComponent = QuickPlayTestTypes.AddComponent(root, "Anatawa12.AvatarOptimizer.TraceAndOptimize");

            Assert.IsNotNull(vrcFuryComponent, "本机应有 VRCFury。");
            Assert.IsNotNull(aaoComponent, "本机应有 AAO。");

            var targets = QuickPlayComponentRules.CollectTargets(root, QuickPlayToolRegistry.Find("vrcfury"));
            Assert.AreEqual(1, targets.Count);
            Assert.AreSame(vrcFuryComponent, targets[0]);
        }

        [Test]
        public void Apply_VrcFury_RemovesInactiveChildComponentsAndKeepsAao()
        {
            var root = _scene.CreateGameObject("Avatar");
            var inactiveChild = _scene.CreateGameObject("InactiveChild");
            inactiveChild.transform.SetParent(root.transform, false);
            inactiveChild.SetActive(false);

            var removed = QuickPlayTestTypes.AddComponent(inactiveChild, "VF.Component.VRCFuryHapticPlug");
            var kept = QuickPlayTestTypes.AddComponent(root, "Anatawa12.AvatarOptimizer.TraceAndOptimize");
            Assert.IsNotNull(removed);
            Assert.IsNotNull(kept);

            var result = new QuickPlayStripResult();
            Assert.IsTrue(QuickPlayComponentRules.TryApply(root, QuickPlayToolRegistry.Find("vrcfury"), result, out var applyErrorVrcFury), applyErrorVrcFury);

            Assert.IsTrue(removed == null, "inactive 子对象上的 VRCFury 组件应被剔除。");
            Assert.IsNotNull(kept, "未勾选工具的组件必须保留。");
            Assert.AreEqual(1, result.RemovedByToolId["vrcfury"]);
            Assert.AreEqual(1, result.TotalRemovedComponents);
        }

        [Test]
        public void Apply_Aao_RemovesSeriesAndKeepsOtherTools()
        {
            var root = _scene.CreateGameObject("Avatar");
            var trace = QuickPlayTestTypes.AddComponent(root, "Anatawa12.AvatarOptimizer.TraceAndOptimize");
            var makeChildren = QuickPlayTestTypes.AddComponent(root, "Anatawa12.AvatarOptimizer.MakeChildren");
            var selectedMesh = QuickPlayTestTypes.AddComponent(root, "Anatawa12.AvatarOptimizer.FreezeBlendShape");
            var vrcFury = QuickPlayTestTypes.AddComponent(root, "VF.Model.VRCFury");

            Assert.IsNotNull(trace);
            Assert.IsNotNull(makeChildren, "本机 AAO 应包含单体组件 MakeChildren。");
            Assert.IsNotNull(vrcFury);

            var result = new QuickPlayStripResult();
            Assert.IsTrue(QuickPlayComponentRules.TryApply(root, QuickPlayToolRegistry.Find("aao"), result, out var applyErrorAao), applyErrorAao);

            Assert.IsTrue(trace == null);
            Assert.IsTrue(makeChildren == null);
            if (selectedMesh != null) Assert.IsTrue(selectedMesh == null, "AAO 单体/编辑组件应同属全系列。");
            Assert.IsNotNull(vrcFury, "未勾选 VRCFury 时不得剔除。");
            Assert.GreaterOrEqual(result.RemovedByToolId["aao"], 2);
        }

        [Test]
        public void Apply_Aao_DoesNotTouchProtectedTypes()
        {
            var root = _scene.CreateGameObject("Avatar");
            var animator = root.AddComponent<Animator>();
            var renderer = root.AddComponent<MeshRenderer>();
            var maComponent = QuickPlayTestTypes.AddComponent(root, "nadena.dev.modular_avatar.core.ModularAvatarMergeArmature");

            var result = new QuickPlayStripResult();
            Assert.IsTrue(QuickPlayComponentRules.TryApply(root, QuickPlayToolRegistry.Find("aao"), result, out var applyErrorAao), applyErrorAao);

            Assert.IsNotNull(animator);
            Assert.IsNotNull(renderer);
            Assert.IsNotNull(maComponent, "MA 组件属于保护集合。");
            Assert.AreEqual(0, result.TotalRemovedComponents);
        }

        [Test]
        public void Apply_UnselectedTool_NeverRunsOnItsOwn()
        {
            var root = _scene.CreateGameObject("Avatar");
            var vrcFury = QuickPlayTestTypes.AddComponent(root, "VF.Model.VRCFury");
            Assert.IsNotNull(vrcFury);

            // 只对 AAO 执行计划：VRCFury 必须保持不动。
            var result = new QuickPlayStripResult();
            Assert.IsTrue(QuickPlayComponentRules.TryApply(root, QuickPlayToolRegistry.Find("aao"), result, out var applyErrorAao), applyErrorAao);

            Assert.IsNotNull(vrcFury);
        }

        [Test]
        public void Apply_Vqt_RemovesMenuIconResizerAndDisablesCompressionSetting()
        {
            var root = _scene.CreateGameObject("Avatar");
            var resizer = QuickPlayTestTypes.AddComponent(root, "KRT.VRCQuestTools.Components.MenuIconResizer");
            var settings = QuickPlayTestTypes.AddComponent(root, "KRT.VRCQuestTools.Components.AvatarConverterSettings");

            Assert.IsNotNull(resizer, "本机应有 VQT。");
            Assert.IsNotNull(settings, "本机应有 VQT AvatarConverterSettings。");

            SetBooleanField(settings, "compressExpressionsMenuIcons", true);

            var result = new QuickPlayStripResult();
            Assert.IsTrue(QuickPlayComponentRules.TryApply(root, QuickPlayToolRegistry.Find("vqt-menu-icons"), result, out var applyErrorVqt), applyErrorVqt);

            Assert.IsTrue(resizer == null, "MenuIconResizer 应被剔除。");
            Assert.IsNotNull(settings, "AvatarConverterSettings 必须保留（材质/平台转换继续生效）。");
            Assert.IsFalse(GetBooleanField(settings, "compressExpressionsMenuIcons"), "菜单图标压缩开关应被关闭。");
            Assert.IsTrue(result.VqtMenuIconStateSuppressionRequested);
        }

        [Test]
        public void Vqt_NoMenuIconConfiguration_IsNoOp()
        {
            var root = _scene.CreateGameObject("Avatar");
            var result = new QuickPlayStripResult();

            Assert.IsTrue(QuickPlayComponentRules.TryApply(root, QuickPlayToolRegistry.Find("vqt-menu-icons"), result, out var applyErrorVqt), applyErrorVqt);

            Assert.IsFalse(result.VqtMenuIconStateSuppressionRequested);
            Assert.AreEqual(0, result.TotalRemovedComponents);
        }

        [Test]
        public void D4Rk_NotInstalled_FailsClosedWithReason()
        {
            var root = _scene.CreateGameObject("Avatar");
            var result = new QuickPlayStripResult();
            var definition = QuickPlayToolRegistry.Find("d4rk-avatar-optimizer");

            var installed = QuickPlayToolRegistry.Evaluate(definition).Availability
                            != QuickPlayToolAvailability.NotInstalled;

            var applied = QuickPlayComponentRules.TryInstallD4RkBlocker(root, definition, result, out var error);

            if (!installed)
            {
                Assert.IsFalse(applied, "未安装 d4rk 时不得宣称已建立阻断。");
                Assert.IsNotEmpty(error, "失败闭合必须给出明确原因。");
                Assert.IsFalse(result.D4RkBlockerInstalled);
            }
            else
            {
                Assert.IsTrue(applied, error);
                Assert.IsTrue(result.D4RkBlockerInstalled);
            }
        }

        [Test]
        public void CollectAllRegisteredTargets_ExcludesProtectedAndUnknown()
        {
            var root = _scene.CreateGameObject("Avatar");
            QuickPlayTestTypes.AddComponent(root, "VF.Model.VRCFury");
            QuickPlayTestTypes.AddComponent(root, "Anatawa12.AvatarOptimizer.TraceAndOptimize");
            root.AddComponent<BoxCollider>();
            QuickPlayTestTypes.AddComponent(root, "nadena.dev.modular_avatar.core.ModularAvatarMergeArmature");

            var targets = QuickPlayComponentRules.CollectAllRegisteredTargets(root);
            var names = targets.Select(component => component.GetType().FullName).ToArray();

            Assert.IsTrue(names.Any(name => name == "VF.Model.VRCFury"));
            Assert.IsTrue(names.Any(name => name == "Anatawa12.AvatarOptimizer.TraceAndOptimize"));
            Assert.IsFalse(names.Any(name => name != null && name.Contains("BoxCollider")));
            Assert.IsFalse(names.Any(name => name != null && name.Contains("ModularAvatar")));
        }

        private static void SetBooleanField(Component component, string fieldName, bool value)
        {
            var serialized = new UnityEditor.SerializedObject(component);
            var property = serialized.FindProperty(fieldName);
            Assert.IsNotNull(property, fieldName + " 应可通过序列化路径访问。");
            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool GetBooleanField(Component component, string fieldName)
        {
            var serialized = new UnityEditor.SerializedObject(component);
            var property = serialized.FindProperty(fieldName);
            Assert.IsNotNull(property, fieldName + " 应可通过序列化路径访问。");
            return property.boolValue;
        }
    }
}

#endif
