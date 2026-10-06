#if UNITY_INCLUDE_TESTS

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace MarbleQuickPlay.Tests
{
    /// <summary>
    /// 仅用于本机验证的测试运行桥：通过 Unity TestRunnerApi 启动指定 Category 的 EditMode 测试，
    /// 并把结果汇总写入静态字段，方便工具（MCP/CLI）读取，不依赖 Test Runner 窗口。
    /// 生产代码不引用本类型；它只在 UNITY_INCLUDE_TESTS 下编译。
    /// </summary>
    public static class QuickPlayTestRunBridge
    {
        public static string Status = "idle";
        public static string Summary = string.Empty;
        public static int Passed;
        public static int Failed;
        public static int Skipped;

        private static TestRunnerApi _api;

        public static string StartEditModeGroup(string groupName)
        {
            Status = "starting";
            Summary = string.Empty;
            Passed = Failed = Skipped = 0;

            try
            {
                _api = ScriptableObject.CreateInstance<TestRunnerApi>();
                _api.RegisterCallbacks(new Callbacks());

                var filter = new Filter
                {
                    testMode = TestMode.EditMode,
                    groupNames = new[] { groupName },
                };

                var executionId = _api.Execute(new ExecutionSettings(filter));
                Status = "running";
                return "started:" + executionId;
            }
            catch (Exception exception)
            {
                Status = "error";
                Summary = exception.GetType().Name + ": " + exception.Message;
                return "error:" + Summary;
            }
        }

        public static string StartEditModeCategory(string category)
        {
            Status = "starting";
            Summary = string.Empty;
            Passed = Failed = Skipped = 0;

            try
            {
                _api = ScriptableObject.CreateInstance<TestRunnerApi>();
                _api.RegisterCallbacks(new Callbacks());

                var filter = new Filter
                {
                    testMode = TestMode.EditMode,
                    categoryNames = new[] { category },
                };

                var executionId = _api.Execute(new ExecutionSettings(filter));
                Status = "running";
                return "started:" + executionId;
            }
            catch (Exception exception)
            {
                Status = "error";
                Summary = exception.GetType().Name + ": " + exception.Message;
                return "error:" + Summary;
            }
        }

        public static string Describe()
        {
            return $"status={Status} passed={Passed} failed={Failed} skipped={Skipped}\n{Summary}";
        }

        private static string FormatResult(ITestResultAdaptor result)
        {
            var builder = new StringBuilder();
            builder.Append("name=").Append(result.Name)
                .Append(" result=").Append(result.TestStatus)
                .Append(" passed=").Append(result.PassCount)
                .Append(" failed=").Append(result.FailCount)
                .Append(" skipped=").Append(result.SkipCount)
                .Append('\n');

            foreach (var child in (result.Children ?? Enumerable.Empty<ITestResultAdaptor>()))
            {
                builder.Append(FormatNode(child));
            }

            return builder.ToString();
        }

        private static string FormatNode(ITestResultAdaptor node)
        {
            var builder = new StringBuilder();
            builder.Append(node.HasChildren ? "SUITE " : "TEST  ")
                .Append(node.TestStatus).Append(' ')
                .Append(node.FullName);

            if (!node.HasChildren && !node.TestStatus.ToString().StartsWith("Passed", StringComparison.Ordinal))
            {
                builder.Append(" :: ").Append(node.Message);
            }

            builder.Append('\n');

            foreach (var child in (node.Children ?? Enumerable.Empty<ITestResultAdaptor>()))
            {
                builder.Append(FormatNode(child));
            }

            return builder.ToString();
        }

        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                Status = "running";
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                Passed = result.PassCount;
                Failed = result.FailCount;
                Skipped = result.SkipCount;
                Summary = FormatResult(result);
                Status = Failed == 0 ? "passed" : "failed";
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }
        }
    }
}

#endif
