#if UNITY_INCLUDE_TESTS

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;

namespace MarbleQuickPlay.Tests
{
    /// <summary>
    /// 直接执行定向测试方法的运行器（不经 Unity Test Runner）。
    ///
    /// 背景：本机不能启动 Test Runner —— <c>EditModeLauncher.OpenNewScene</c> 会
    /// <c>RemoveUntitledScenes()</c> / <c>ReloadUnsavedDirtyScene()</c>，可能关闭或重载用户未保存的场景。
    /// 因此这里用反射执行 [SetUp]/[Test]/[TearDown]，并为使用 <c>LogAssert.Expect</c> 的测试
    /// 通过反射建立一个 <c>UnityEngine.TestTools.Logging.LogScope</c>（否则 LogAssert 会抛
    /// “No log scope is available”）。只校验「期望的日志是否出现」，不把其它错误日志当作失败。
    ///
    /// 本运行器不是发布产物的一部分：它只在 UNITY_INCLUDE_TESTS 下编译。
    /// </summary>
    public static class QuickPlayDirectTestRunner
    {
        private const string QuickPlayTestTypes =
            "MarbleQuickPlay.Tests.QuickPlayBasicReleaseTests," +
            "MarbleQuickPlay.Tests.QuickPlaySessionLifecycleTests," +
            "MarbleQuickPlay.Tests.QuickPlaySessionRecordTests," +
            "MarbleQuickPlay.Tests.QuickPlayPreparePipelineTests," +
            "MarbleQuickPlay.Tests.QuickPlayNdmfFailurePropagationTests," +
            "MarbleQuickPlay.Tests.QuickPlayPreflightTests," +
            "MarbleQuickPlay.Tests.QuickPlayMenuAndWindowTests," +
            "MarbleQuickPlay.Tests.QuickPlayComponentRulesTests," +
            "MarbleQuickPlay.Tests.QuickPlayToolRegistryTests," +
            "MarbleQuickPlay.Tests.QuickPlayPreferencesTests," +
            "MarbleQuickPlay.Tests.QuickPlaySettingsPersistenceTests," +
            "MarbleQuickPlay.Tests.QuickPlayTargetSelectionTests," +
            "MarbleQuickPlay.Tests.QuickPlayVqtSuppressionTests," +
            "MarbleQuickPlay.Tests.QuickPlayTestIsolationTests";

        public static string RunAllQuickPlayTests() => RunTypes(QuickPlayTestTypes);

        public static string RunTypes(string commaSeparatedTypeNames)
        {
            var assembly = typeof(QuickPlayDirectTestRunner).Assembly;
            var report = new StringBuilder();
            var total = new Counts();
            var scopeAvailability = QuickPlayLogScopeBridge.Probe(out var scopeError);
            report.Append("LogScope=").Append(scopeAvailability ? "available" : "unavailable")
                .Append(scopeAvailability ? string.Empty : " (" + scopeError + ")").Append('\n');

            foreach (var typeName in commaSeparatedTypeNames.Split(',').Select(name => name.Trim()).Where(name => name.Length > 0))
            {
                var type = assembly.GetType(typeName);
                if (type == null)
                {
                    report.Append("MISSING TYPE ").Append(typeName).Append('\n');
                    total.Missing++;
                    continue;
                }

                report.Append("== ").Append(typeName).Append('\n');
                RunType(type, report, total);
            }

            report.Append("SUMMARY passed=").Append(total.Passed)
                .Append(" failed=").Append(total.Failed)
                .Append(" skipped=").Append(total.Skipped)
                .Append(" missingTypes=").Append(total.Missing)
                .Append('\n');
            return report.ToString();
        }

        private static void RunType(Type type, StringBuilder report, Counts total)
        {
            var setUp = FindAttributedMethod(type, typeof(SetUpAttribute));
            var tearDown = FindAttributedMethod(type, typeof(TearDownAttribute));

            var tests = type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.GetCustomAttributes(typeof(TestAttribute), true).Length > 0)
                .OrderBy(method => method.Name, StringComparer.Ordinal)
                .ToList();

            if (tests.Count == 0)
            {
                report.Append("  (no [Test] methods)\n");
                return;
            }

            foreach (var test in tests)
            {
                var instance = Activator.CreateInstance(type);
                string failure = null;
                var skipReason = string.Empty;
                var scopeActive = QuickPlayLogScopeBridge.Begin();

                try
                {
                    setUp?.Invoke(instance, null);
                    test.Invoke(instance, null);
                }
                catch (TargetInvocationException invocation)
                {
                    var inner = invocation.InnerException ?? invocation;
                    if (inner is IgnoreException)
                    {
                        skipReason = inner.Message;
                    }
                    else
                    {
                        failure = inner.GetType().Name + ": " + inner.Message + "\n" + inner.StackTrace;
                    }
                }
                catch (Exception exception)
                {
                    failure = exception.GetType().Name + ": " + exception.Message + "\n" + exception.StackTrace;
                }
                finally
                {
                    try
                    {
                        tearDown?.Invoke(instance, null);
                    }
                    catch (Exception tearDownException)
                    {
                        failure = (failure ?? string.Empty) + "\n[TearDown] " + tearDownException.GetType().Name + ": " + tearDownException.Message;
                    }

                    if (scopeActive)
                    {
                        var unmet = QuickPlayLogScopeBridge.End();
                        if (unmet != null && failure == null)
                        {
                            failure = "未满足的 LogAssert.Expect：" + unmet;
                        }
                    }
                }

                if (skipReason.Length > 0)
                {
                    total.Skipped++;
                    report.Append("  SKIP  ").Append(test.Name).Append(" :: ").Append(skipReason).Append('\n');
                }
                else if (failure == null)
                {
                    total.Passed++;
                    report.Append("  PASS  ").Append(test.Name).Append('\n');
                }
                else
                {
                    total.Failed++;
                    report.Append("  FAIL  ").Append(test.Name).Append(" :: ").Append(failure).Append('\n');
                }
            }
        }

        private static MethodInfo FindAttributedMethod(Type type, Type attributeType)
        {
            return type
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(method => method.GetCustomAttributes(attributeType, true).Length > 0);
        }

        private sealed class Counts
        {
            public int Passed;
            public int Failed;
            public int Skipped;
            public int Missing;
        }
    }

    /// <summary>
    /// 通过反射建立/关闭 <c>UnityEngine.TestTools.Logging.LogScope</c>（内部类型），
    /// 让 <c>LogAssert.Expect</c> 能在 Test Runner 之外工作；只报告未满足的期望。
    /// </summary>
    internal static class QuickPlayLogScopeBridge
    {
        private static Type _scopeType;
        private static ConstructorInfo _scopeConstructor;
        private static PropertyInfo _expectedLogsProperty;
        private static MethodInfo _processExpectedLogsMethod;
        private static MethodInfo _disposeMethod;
        private static object _scope;

        public static bool Probe(out string error)
        {
            error = string.Empty;
            try
            {
                var logAssertType = Type.GetType("UnityEngine.TestTools.LogAssert, UnityEngine.TestRunner");
                _scopeType = logAssertType?.Assembly.GetType("UnityEngine.TestTools.Logging.LogScope");
                if (_scopeType == null)
                {
                    error = "未找到 UnityEngine.TestTools.Logging.LogScope。";
                    return false;
                }

                _scopeConstructor = _scopeType.GetConstructor(Type.EmptyTypes);
                _expectedLogsProperty = _scopeType.GetProperty("ExpectedLogs");
                _processExpectedLogsMethod = _scopeType.GetMethod("ProcessExpectedLogs", Type.EmptyTypes);
                _disposeMethod = _scopeType.GetMethod("Dispose", Type.EmptyTypes);
                if (_scopeConstructor == null || _expectedLogsProperty == null || _processExpectedLogsMethod == null || _disposeMethod == null)
                {
                    error = "LogScope 反射接口不完整（constructor/ExpectedLogs/ProcessExpectedLogs/Dispose）。";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        public static bool Begin()
        {
            if (_scope != null) End();
            if (_scopeConstructor == null && !Probe(out _)) return false;

            try
            {
                _scope = _scopeConstructor.Invoke(null);
                return true;
            }
            catch (Exception)
            {
                _scope = null;
                return false;
            }
        }

        /// <summary>关闭 scope；返回未满足的期望描述（无未满足项时返回 null）。</summary>
        public static string End()
        {
            if (_scope == null) return null;

            var unmet = new List<string>();
            try
            {
                // 真实 Test Runner 在帧末调用 ProcessExpectedLogs；这里同步补上，否则期望不会被匹配。
                _processExpectedLogsMethod.Invoke(_scope, null);

                if (_expectedLogsProperty.GetValue(_scope) is System.Collections.IEnumerable expected)
                {
                    foreach (var match in expected)
                    {
                        if (match == null) continue;
                        var messageProperty = match.GetType().GetProperty("Message");
                        var regexProperty = match.GetType().GetProperty("MessageRegex");
                        var message = messageProperty?.GetValue(match) as string;
                        var regex = regexProperty?.GetValue(match) as System.Text.RegularExpressions.Regex;
                        unmet.Add(message ?? regex?.ToString() ?? "<unknown>");
                    }
                }
            }
            catch (Exception exception)
            {
                unmet.Add("<读取期望失败：" + exception.Message + ">");
            }
            finally
            {
                try
                {
                    _disposeMethod.Invoke(_scope, null);
                }
                catch (Exception)
                {
                    // 忽略：scope 清理失败不影响测试结论
                }

                _scope = null;
            }

            return unmet.Count == 0 ? null : string.Join("；", unmet);
        }
    }
}

#endif
