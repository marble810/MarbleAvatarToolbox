#if UNITY_INCLUDE_TESTS

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using marble810.MarbleAvatarToolbox.PlayModeTools;
using NUnit.Framework;
using nadena.dev.ndmf;

namespace MarbleQuickPlay.Tests
{
    /// <summary>
    /// VQT 专用抑制策略测试：能力检测、目标限定判定与状态关闭逻辑。
    /// 不做完整 NDMF 构建（那会修改场景），只验证目标限定与反射能力。
    /// </summary>
    [Category("QuickPlay")]
    public sealed class QuickPlayVqtSuppressionTests
    {
        [Test]
        public void VqtCapability_IsAvailableInThisProject()
        {
            var capability = QuickPlayToolRegistry.Evaluate(QuickPlayToolRegistry.Find("vqt-menu-icons"));

            Assert.AreEqual(QuickPlayToolAvailability.Available, capability.Availability, capability.Reason);
            Assert.IsNotEmpty(capability.ResolvedTypes);
        }

        [Test]
        public void VqtSuppression_CanResolveStateTypeFieldAndPass()
        {
            var stateType = QuickPlayToolRegistry.FindTypeByFullName(QuickPlayToolRegistry.VqtNdmfStateFullName);
            Assert.IsNotNull(stateType, "应能解析 VQT NdmfState。");

            var field = stateType.GetField(QuickPlayToolRegistry.VqtCompressMenuIconsFieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "应能解析 compressExpressionsMenuIcons 字段。");
            Assert.AreEqual(typeof(bool), field.FieldType);

            Assert.IsNotNull(QuickPlayToolRegistry.FindTypeByFullName(QuickPlayToolRegistry.VqtMenuIconPassFullName),
                "应能解析 MenuIconResizerPass（用于 pass 排序约束）。");
            Assert.AreEqual("KRT.VRCQuestTools.Ndmf.MenuIconResizerPass", QuickPlayVqtSuppression.MenuIconResizerPassFullName);
        }

        [Test]
        public void VqtSuppression_StateInstanceIsCreatable()
        {
            var stateType = QuickPlayToolRegistry.FindTypeByFullName(QuickPlayToolRegistry.VqtNdmfStateFullName);
            Assert.IsNotNull(stateType);

            var instance = Activator.CreateInstance(stateType, true);
            Assert.IsNotNull(instance, "NdmfState 必须可实例化（GetState<T> 的 new() 约束）。");
        }

        [Test]
        public void VqtSuppression_NullContextFailsClosed()
        {
            Assert.IsFalse(QuickPlayVqtSuppression.TryDisableCompressionState(null, out var error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void VqtSuppression_IsTargetLimited()
        {
            // 没有活动 QuickPlay 会话时，任何对象都不是 VQT 抑制目标。
            using (var scene = QuickPlayObjectTestScope.Require())
            {
                var gameObject = scene.CreateGameObject("QuickPlayVqtProbe");
                Assert.IsFalse(QuickPlaySession.IsVqtSuppressionTarget(gameObject));
            }

            Assert.IsFalse(QuickPlaySession.IsVqtSuppressionTarget(null));
        }

        [Test]
        public void NdmfPlugin_IsRegisteredWithQuickPlayQualifiedName()
        {
            Assert.AreEqual("marble810.marbleavatartoolbox.quickplay", QuickPlayNdmfPlugin.Instance.QualifiedName);
            Assert.AreEqual("Marble Avatar Toolbox QuickPlay", QuickPlayNdmfPlugin.Instance.DisplayName);
        }

        [Test]
        public void NdmfApplyOnPlayRead_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => { var unused = QuickPlayNdmfConfig.ApplyOnPlay; });
        }

        [Test]
        public void MenuIconResizerTypeIsRecognizedOnlyForVqt()
        {
            var resizerType = QuickPlayToolRegistry.FindTypeByFullName(QuickPlayToolRegistry.VqtMenuIconResizerFullName);
            Assert.IsNotNull(resizerType);

            Assert.IsTrue(QuickPlayToolRegistry.IsTargetType(QuickPlayToolRegistry.Find("vqt-menu-icons"), resizerType));
            Assert.IsFalse(QuickPlayToolRegistry.IsTargetType(QuickPlayToolRegistry.Find("vrcfury"), resizerType));
            Assert.IsFalse(QuickPlayToolRegistry.IsTargetType(QuickPlayToolRegistry.Find("aao"), resizerType));
        }

        [Test]
        public void VqtSettingsTypeIsNotRegisteredAsRemovalTarget()
        {
            // AvatarConverterSettings 必须保留：只能关闭图标压缩字段，不能登记为删除目标。
            var settingsType = QuickPlayToolRegistry.FindTypeByFullName(QuickPlayToolRegistry.VqtAvatarConverterSettingsFullName);
            Assert.IsNotNull(settingsType);

            foreach (var tool in QuickPlayToolRegistry.Tools)
            {
                Assert.IsFalse(tool.ExactTypeFullNames.Contains(QuickPlayToolRegistry.VqtAvatarConverterSettingsFullName),
                    tool.Id + " 不得把 AvatarConverterSettings 登记为删除目标。");
            }

            // 保护集合不覆盖第三方目标工具类型，因此 VQT 规则必须显式保留该组件。
            Assert.IsFalse(QuickPlayToolRegistry.IsProtected(settingsType));
        }
    }
}

#endif
