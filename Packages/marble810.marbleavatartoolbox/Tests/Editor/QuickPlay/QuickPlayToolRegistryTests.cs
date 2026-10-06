#if UNITY_INCLUDE_TESTS

using System.Linq;
using marble810.MarbleAvatarToolbox.PlayModeTools;
using NUnit.Framework;
using UnityEngine;

namespace MarbleQuickPlay.Tests
{
    /// <summary>
    /// 工具登记表与匹配/保护规则的测试。全部为纯元数据判断，不修改当前场景。
    /// </summary>
    [Category("QuickPlay")]
    public sealed class QuickPlayToolRegistryTests
    {
        private static readonly string[] ExpectedIds =
        {
            "vrcfury",
            "aao",
            "d4rk-avatar-optimizer",
            "meshia",
            "lac",
            "overall-mesh-simplifier",
            "mantis-ndmf",
            "lil-ndmf-mesh-simplifier",
            "ttt-optimization",
            "vqt-menu-icons",
        };

        private static readonly string[] ExpectedLabels =
        {
            "VRCFury",
            "AAO (Avatar Optimizer)",
            "d4rk Avatar Optimizer",
            "Meshia Mesh Simplification",
            "Avatar Compressor (LAC)",
            "Overall NDMF Mesh Simplifier",
            "NDMF Mantis LOD Editor",
            "lilNDMFMeshSimplifier（旧版）",
            "TexTransTool（仅贴图优化）",
            "VRCQuestTools（仅菜单图标优化）",
        };

        [Test]
        public void Tools_FixedTenEntriesInStableOrder()
        {
            var ids = QuickPlayToolRegistry.Tools.Select(tool => tool.Id).ToArray();
            Assert.AreEqual(ExpectedIds, ids);
            Assert.AreEqual(10, QuickPlayToolRegistry.Tools.Count);
        }

        [Test]
        public void Tools_LabelsMatchDesignList()
        {
            var labels = QuickPlayToolRegistry.Tools.Select(tool => tool.Label).ToArray();
            Assert.AreEqual(ExpectedLabels, labels);
        }

        [Test]
        public void Tools_PartialRangeLabelsAreExplicit()
        {
            var ttt = QuickPlayToolRegistry.Find("ttt-optimization");
            var vqt = QuickPlayToolRegistry.Find("vqt-menu-icons");
            StringAssert.Contains("仅贴图优化", ttt.Label);
            StringAssert.Contains("仅菜单图标优化", vqt.Label);
            StringAssert.Contains("贴花", ttt.Tooltip);
            StringAssert.Contains("平台", vqt.Tooltip);
        }

        [Test]
        public void Tools_RiskTooltipsCoverVrcFuryAndAao()
        {
            var vrcFury = QuickPlayToolRegistry.Find("vrcfury");
            var aao = QuickPlayToolRegistry.Find("aao");
            StringAssert.Contains("构建功能", vrcFury.Tooltip);
            StringAssert.Contains("删面", aao.Tooltip);
            StringAssert.Contains("重命名", aao.Tooltip);
            StringAssert.Contains("MakeChildren", aao.Tooltip);
        }

        [Test]
        public void Defaults_OnlyVrcFuryEnabled()
        {
            var defaults = QuickPlayToolRegistry.Tools.Where(tool => tool.DefaultEnabled).Select(tool => tool.Id).ToArray();
            Assert.AreEqual(new[] { "vrcfury" }, defaults);
        }

        [Test]
        public void Availability_MatchesLoadedTypeAndInstallationEvidence()
        {
            foreach (var tool in QuickPlayToolRegistry.Tools)
            {
                var capability = QuickPlayToolRegistry.Evaluate(tool);
                var hasAnyExactType = tool.ExactTypeFullNames.Any(name => QuickPlayToolRegistry.FindTypeByFullName(name) != null);
                var hasAnyBaseType = tool.BaseTypeFullNames.Any(name => QuickPlayToolRegistry.FindTypeByFullName(name) != null);
                var evidence = QuickPlayToolRegistry.DescribeInstallationEvidence(tool, null);

                if (capability.Availability == QuickPlayToolAvailability.NotInstalled)
                {
                    Assert.IsFalse(hasAnyExactType || hasAnyBaseType,
                        tool.Id + " 被判定为未安装，但登记类型已加载。");
                    Assert.IsEmpty(evidence, tool.Id + " 被判定为未安装，但存在安装证据。");
                }
                else
                {
                    Assert.IsNotEmpty(evidence, tool.Id + " 被判定为已加载，但没有证据。");
                }
            }
        }

        [Test]
        public void Evidence_InstalledButRegisteredTypeMissing_IsIncompatibleNotNotInstalled()
        {
            // R3：有独立安装证据（这里用测试程序集里真实存在的类型冒充证据）但目标类型缺失时，
            // 必须报 Incompatible（保守拒绝），不能误报 NotInstalled。
            var definition = new QuickPlayToolDefinition(
                "evidence-test",
                "Evidence Test",
                "test",
                QuickPlayToolAction.RemoveComponents,
                false,
                exactTypeFullNames: new[] { "MarbleQuickPlay.Tests.DoesNotExistAnywhere" },
                baseTypeFullNames: null,
                assemblyPrefixes: new[] { "Assembly-CSharp-Editor" },
                evidenceTypeFullNames: new[] { typeof(FakeLacLikeOptimizer).FullName });

            var capability = QuickPlayToolRegistry.Evaluate(definition);

            Assert.AreEqual(QuickPlayToolAvailability.Incompatible, capability.Availability);
            StringAssert.Contains("保守拒绝", capability.Reason);
            Assert.IsNotEmpty(capability.Reason);
        }

        [Test]
        public void Evidence_NoEvidenceAtAll_IsNotInstalled()
        {
            var definition = new QuickPlayToolDefinition(
                "no-evidence-test",
                "No Evidence Test",
                "test",
                QuickPlayToolAction.RemoveComponents,
                false,
                exactTypeFullNames: new[] { "Totally.Absent.Tool.Optimizer" },
                baseTypeFullNames: null,
                assemblyPrefixes: new[] { "Totally.Absent" });

            var capability = QuickPlayToolRegistry.Evaluate(definition);

            Assert.AreEqual(QuickPlayToolAvailability.NotInstalled, capability.Availability);
        }

        [Test]
        public void D4Rk_UninstalledInThisProject_IsNotAvailable()
        {
            // 本机未安装 d4rk：既没有安装证据，也不得被标记为 Available。
            var capability = QuickPlayToolRegistry.Evaluate(QuickPlayToolRegistry.Find("d4rk-avatar-optimizer"));

            Assert.AreNotEqual(QuickPlayToolAvailability.Available, capability.Availability,
                "未经核验/未安装的 d4rk 版本不得被声明为可用。");
        }

        [Test]
        public void Vqt_StrictCapabilityChecksPassInThisProject()
        {
            // 本机 VQT：图标组件、状态字段、状态访问 API、转换设置字段与 pass 身份都必须通过校验。
            var capability = QuickPlayToolRegistry.Evaluate(QuickPlayToolRegistry.Find("vqt-menu-icons"));

            Assert.AreEqual(QuickPlayToolAvailability.Available, capability.Availability, capability.Reason);
        }

        [Test]
        public void D4Rk_EvidenceTypesAreTheVerifiedHookAndSettings()
        {
            var definition = QuickPlayToolRegistry.Find("d4rk-avatar-optimizer");
            CollectionAssert.Contains(definition.EvidenceTypeFullNames, "d4rkpl4y3r.AvatarOptimizer.AvatarBuildHook");
            CollectionAssert.Contains(definition.EvidenceTypeFullNames, "d4rkpl4y3r.AvatarOptimizer.AvatarOptimizerSettings");

            // 不得把 Assembly-CSharp 中的任意回调当作证据。
            CollectionAssert.DoesNotContain(definition.AssemblyEvidencePrefixes, "Assembly-CSharp");
            CollectionAssert.DoesNotContain(definition.AssemblyEvidencePrefixes, "Assembly-CSharp-Editor");
        }

        [Test]
        public void Availability_VrcFuryAaoAndVqtAvailableInThisProject()
        {
            // 本机环境事实：VRCFury / AAO / VQT 已安装，其余未安装。
            Assert.AreEqual(QuickPlayToolAvailability.Available, QuickPlayToolRegistry.Evaluate(QuickPlayToolRegistry.Find("vrcfury")).Availability);
            Assert.AreEqual(QuickPlayToolAvailability.Available, QuickPlayToolRegistry.Evaluate(QuickPlayToolRegistry.Find("aao")).Availability);
            Assert.AreEqual(QuickPlayToolAvailability.Available, QuickPlayToolRegistry.Evaluate(QuickPlayToolRegistry.Find("vqt-menu-icons")).Availability);
            Assert.AreEqual(QuickPlayToolAvailability.NotInstalled, QuickPlayToolRegistry.Evaluate(QuickPlayToolRegistry.Find("meshia")).Availability);
        }

        [Test]
        public void Availability_UninstalledToolsReportNotInstalled_WithoutThrowing()
        {
            foreach (var tool in QuickPlayToolRegistry.Tools)
            {
                Assert.DoesNotThrow(() => QuickPlayToolRegistry.Evaluate(tool));
            }
        }

        [Test]
        public void Matching_ExactFullNameWithAcceptedAssemblyMatches()
        {
            var definition = new QuickPlayToolDefinition(
                "test-fake",
                "Test Fake",
                "test",
                QuickPlayToolAction.RemoveComponents,
                false,
                new[] { typeof(FakeLacLikeOptimizer).FullName },
                null,
                new[] { "Assembly-CSharp-Editor" });

            Assert.IsTrue(QuickPlayToolRegistry.IsTargetType(definition, typeof(FakeLacLikeOptimizer)));
        }

        [Test]
        public void Matching_SameFullNameFromUnregisteredAssemblyIsRejected()
        {
            // “同名不同程序集”必须保留：全名一致但来源不匹配时不得命中。
            var definition = new QuickPlayToolDefinition(
                "test-fake",
                "Test Fake",
                "test",
                QuickPlayToolAction.RemoveComponents,
                false,
                new[] { typeof(FakeLacLikeOptimizer).FullName },
                null,
                new[] { "dev.limitex.avatar-compressor" });

            Assert.IsFalse(QuickPlayToolRegistry.IsTargetType(definition, typeof(FakeLacLikeOptimizer)));
        }

        [Test]
        public void Matching_UnknownComponentIsNotMatched()
        {
            var vrcFury = QuickPlayToolRegistry.Find("vrcfury");
            var aao = QuickPlayToolRegistry.Find("aao");

            Assert.IsFalse(QuickPlayToolRegistry.IsTargetType(vrcFury, typeof(Transform)));
            Assert.IsFalse(QuickPlayToolRegistry.IsTargetType(aao, typeof(FakeLacLikeOptimizer)));
            Assert.IsFalse(QuickPlayToolRegistry.IsTargetType(vrcFury, typeof(FakeLacLikeOptimizer)));
        }

        [Test]
        public void Matching_AaoUsesWholeSeriesBaseType()
        {
            var aao = QuickPlayToolRegistry.Find("aao");
            var traceAndOptimize = QuickPlayTestTypes.Find("Anatawa12.AvatarOptimizer.TraceAndOptimize");
            var makeChildren = QuickPlayTestTypes.Find("Anatawa12.AvatarOptimizer.MakeChildren");

            Assert.IsNotNull(traceAndOptimize, "本机应安装 AAO（TraceAndOptimize）。");
            Assert.IsNotNull(makeChildren, "本机应安装 AAO（单体组件 MakeChildren）。");

            Assert.IsTrue(QuickPlayToolRegistry.IsTargetType(aao, traceAndOptimize), "TraceAndOptimize 应命中 AAO 规则。");
            Assert.IsTrue(QuickPlayToolRegistry.IsTargetType(aao, makeChildren), "只有单体组件时也必须命中 AAO 规则。");
        }

        [Test]
        public void Matching_VrcFurySeriesIsRecognized()
        {
            var vrcFury = QuickPlayToolRegistry.Find("vrcfury");
            var main = QuickPlayTestTypes.Find("VF.Model.VRCFury");
            var hapticPlug = QuickPlayTestTypes.Find("VF.Component.VRCFuryHapticPlug");

            Assert.IsNotNull(main, "本机应安装 VRCFury。");
            Assert.IsNotNull(hapticPlug, "本机应安装 VRCFury Haptic 组件。");

            Assert.IsTrue(QuickPlayToolRegistry.IsTargetType(vrcFury, main));
            Assert.IsTrue(QuickPlayToolRegistry.IsTargetType(vrcFury, hapticPlug));
            Assert.IsFalse(QuickPlayToolRegistry.IsTargetType(vrcFury, typeof(Transform)));
        }

        [Test]
        public void Protection_SetCoversTransformMaSdkNdmfAndOwnTypes()
        {
            Assert.IsTrue(QuickPlayToolRegistry.IsProtected(typeof(Transform)));
            Assert.IsTrue(QuickPlayToolRegistry.IsProtected(typeof(RectTransform)));

            var descriptor = QuickPlayTestTypes.Find("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            Assert.IsNotNull(descriptor, "本机应有 VRChat SDK。");
            Assert.IsTrue(QuickPlayToolRegistry.IsProtected(descriptor));

            var maComponent = QuickPlayTestTypes.Find("nadena.dev.modular_avatar.core.ModularAvatarMergeArmature");
            Assert.IsNotNull(maComponent, "本机应有 Modular Avatar。");
            Assert.IsTrue(QuickPlayToolRegistry.IsProtected(maComponent));

            var ndmfRoot = QuickPlayTestTypes.Find("nadena.dev.ndmf.runtime.components.NDMFAvatarRoot");
            Assert.IsNotNull(ndmfRoot, "本机应有 NDMF。");
            Assert.IsTrue(QuickPlayToolRegistry.IsProtected(ndmfRoot));

            Assert.IsTrue(QuickPlayToolRegistry.IsProtected(typeof(QuickPlayCloneMarker)));
            Assert.IsTrue(QuickPlayToolRegistry.IsProtected(typeof(Animator)));
            Assert.IsTrue(QuickPlayToolRegistry.IsProtected(null));
        }

        [Test]
        public void Protection_TargetOptimizerTypesAreNotProtected()
        {
            var traceAndOptimize = QuickPlayTestTypes.Find("Anatawa12.AvatarOptimizer.TraceAndOptimize");
            var vrcFuryMain = QuickPlayTestTypes.Find("VF.Model.VRCFury");
            var menuIconResizer = QuickPlayTestTypes.Find("KRT.VRCQuestTools.Components.MenuIconResizer");

            Assert.IsNotNull(traceAndOptimize);
            Assert.IsNotNull(vrcFuryMain);
            Assert.IsNotNull(menuIconResizer);

            Assert.IsFalse(QuickPlayToolRegistry.IsProtected(traceAndOptimize));
            Assert.IsFalse(QuickPlayToolRegistry.IsProtected(vrcFuryMain));
            Assert.IsFalse(QuickPlayToolRegistry.IsProtected(menuIconResizer));
        }

        [Test]
        public void RegisteredTypeNamesAndAssemblies_MatchDocumentedRules()
        {
            CollectionAssert.Contains(QuickPlayToolRegistry.Find("meshia").ExactTypeFullNames, "Meshia.MeshSimplification.Ndmf.MeshiaMeshSimplifier");
            CollectionAssert.Contains(QuickPlayToolRegistry.Find("meshia").ExactTypeFullNames, "Meshia.MeshSimplification.Ndmf.MeshiaCascadingAvatarMeshSimplifier");
            CollectionAssert.Contains(QuickPlayToolRegistry.Find("lac").ExactTypeFullNames, "dev.limitex.avatar.compressor.TextureCompressor");
            CollectionAssert.Contains(QuickPlayToolRegistry.Find("overall-mesh-simplifier").ExactTypeFullNames, "com.aoyon.OverallNDMFMeshSimplifier.OverallNdmfMeshSimplifier");
            CollectionAssert.Contains(QuickPlayToolRegistry.Find("mantis-ndmf").ExactTypeFullNames, "MantisLODEditor.ndmf.NDMFMantisLODEditor");
            CollectionAssert.Contains(QuickPlayToolRegistry.Find("lil-ndmf-mesh-simplifier").ExactTypeFullNames, "jp.lilxyzw.ndmfmeshsimplifier.runtime.NDMFMeshSimplifier");
            CollectionAssert.Contains(QuickPlayToolRegistry.Find("ttt-optimization").ExactTypeFullNames, "net.rs64.TexTransTool.TextureAtlas.AtlasTexture");
            CollectionAssert.Contains(QuickPlayToolRegistry.Find("ttt-optimization").ExactTypeFullNames, "net.rs64.TexTransTool.TextureConfigurator");
            CollectionAssert.Contains(QuickPlayToolRegistry.Find("vqt-menu-icons").ExactTypeFullNames, "KRT.VRCQuestTools.Components.MenuIconResizer");
            CollectionAssert.Contains(QuickPlayToolRegistry.Find("d4rk-avatar-optimizer").ExactTypeFullNames, "d4rkpl4y3r.AvatarOptimizer.d4rkAvatarOptimizer");
            CollectionAssert.Contains(QuickPlayToolRegistry.Find("aao").BaseTypeFullNames, "Anatawa12.AvatarOptimizer.AvatarTagComponent");

            // TTT 规则只登记贴图优化类型，不得把整个 TTT 命名空间作为匹配依据。
            Assert.IsFalse(QuickPlayToolRegistry.Find("ttt-optimization").AssemblyPrefixes.Any(prefix => prefix == string.Empty));
            Assert.AreEqual(1, QuickPlayToolRegistry.Find("ttt-optimization").AssemblyPrefixes.Length);
        }

        [Test]
        public void IsKnownId_RejectsUnknownHistoricalIds()
        {
            Assert.IsTrue(QuickPlayToolRegistry.IsKnownId("vrcfury"));
            Assert.IsFalse(QuickPlayToolRegistry.IsKnownId("legacy-unknown-tool"));
            Assert.IsFalse(QuickPlayToolRegistry.IsKnownId(null));
        }
    }
}

#endif
