﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Arbor.Processing;
using Arbor.Processing.Core;
using Arbor.X.Core.BuildVariables;
using Arbor.X.Core.Logging;
using JetBrains.Annotations;
using NUnit.Framework;

namespace Arbor.X.Core.Tools.Testing
{
    [Priority(400)]
    [UsedImplicitly]
    public class NUnitTestRunner : ITool
    {
        private string _sourceRoot;

        public async Task<ExitCode> ExecuteAsync(
            ILogger logger,
            IReadOnlyCollection<IVariable> buildVariables,
            CancellationToken cancellationToken)
        {
            bool enabled = buildVariables.GetBooleanByKey(WellKnownVariables.NUnitEnabled, true);

            if (!enabled)
            {
                logger.WriteWarning("NUnit not enabled");
                return ExitCode.Success;
            }

            IVariable externalTools = buildVariables.Require(WellKnownVariables.ExternalTools).ThrowIfEmptyValue();
            IVariable reportPath = buildVariables.Require(WellKnownVariables.ReportPath).ThrowIfEmptyValue();

            IVariable ignoreTestFailuresVariable =
                buildVariables.SingleOrDefault(key => key.Key == WellKnownVariables.IgnoreTestFailures);

            bool testsEnabled = buildVariables.GetBooleanByKey(WellKnownVariables.TestsEnabled, true);

            _sourceRoot = buildVariables.Require(WellKnownVariables.SourceRoot).ThrowIfEmptyValue().Value;

            if (!testsEnabled)
            {
                logger.WriteWarning(
                    $"Tests are disabled (build variable '{WellKnownVariables.TestsEnabled}' is false)");
                return ExitCode.Success;
            }

            bool runTestsInReleaseConfiguration =
                buildVariables.GetBooleanByKey(
                    WellKnownVariables.RunTestsInReleaseConfigurationEnabled,
                    true);

            bool ignoreTestFailures = ignoreTestFailuresVariable.GetValueOrDefault(false);

            if (ignoreTestFailures)
            {
                string message =
                    $"The exit code from NUnit test was not successful, but the environment variable {WellKnownVariables.IgnoreTestFailures} is set to true, thus returning success";

                try
                {
                    ExitCode exitCode = await RunNUnitAsync(
                        externalTools,
                        logger,
                        reportPath,
                        runTestsInReleaseConfiguration);

                    if (exitCode.IsSuccess)
                    {
                        return exitCode;
                    }

                    logger.WriteWarning(message);

                    return ExitCode.Success;
                }
                catch (Exception ex)
                {
                    logger.WriteWarning($"{message}. {ex}");
                }

                return ExitCode.Success;
            }

            return await RunNUnitAsync(externalTools, logger, reportPath, runTestsInReleaseConfiguration);
        }

        private static void LogExecution(ILogger logger, IEnumerable<string> nunitArgs, string nunitExe)
        {
            string args = string.Join(" ", nunitArgs.Select(item => $"\"{item}\""));
            logger.Write($"Running NUnit {nunitExe} {args}");
        }

        private static string GetNunitExePath(IVariable externalTools)
        {
            string nunitExe = Path.Combine(externalTools.Value, "nunit", "nunit3-console.exe");
            return nunitExe;
        }

        private static IEnumerable<string> GetNUnitConsoleOptions(string reportFile)
        {
            var options = new List<string> { "--output", reportFile };
            return options;
        }

        private static string GetNUnitXmlReportFilePath(IVariable reportPath)
        {
            string xmlReportName = $"{Guid.NewGuid()}.xml";

            string reportFile = Path.Combine(reportPath.Value, "nunit", xmlReportName);

            return reportFile;
        }

        private async Task<ExitCode> RunNUnitAsync(
            IVariable externalTools,
            ILogger logger,
            IVariable reportPath,
            bool runTestsInReleaseConfiguration)
        {
            Type fixtureAttribute = typeof(TestFixtureAttribute);
            Type testMethodAttribute = typeof(TestAttribute);

            var directory = new DirectoryInfo(_sourceRoot);

            var typesToFind = new List<Type> { fixtureAttribute, testMethodAttribute };

            List<string> testDlls = new UnitTestFinder(typesToFind)
                .GetUnitTestFixtureDlls(directory, runTestsInReleaseConfiguration)
                .ToList();

            if (!testDlls.Any())
            {
                logger.WriteWarning(
                    $"Could not find any NUnit tests in directory '{directory.FullName}' or any sub-directory");
                return ExitCode.Success;
            }

            string nunitExePath = GetNunitExePath(externalTools);

            var results = new List<Tuple<string, ExitCode>>();

            foreach (string testDll in testDlls)
            {
                var nunitConsoleArguments = new List<string> { testDll };

                string reportFilePath = GetNUnitXmlReportFilePath(reportPath);

                EnsureNUnitReportDirectoryExists(reportFilePath);

                IEnumerable<string> options = GetNUnitConsoleOptions(reportFilePath);

                nunitConsoleArguments.AddRange(options);

                LogExecution(logger, nunitConsoleArguments, nunitExePath);

                ExitCode result = await ProcessRunner.ExecuteAsync(
                    nunitExePath,
                    arguments: nunitConsoleArguments,
                    standardOutLog: logger.Write,
                    standardErrorAction: logger.WriteError,
                    toolAction: logger.Write);

                results.Add(Tuple.Create(testDll, result));
            }

            if (results.All(result => result.Item2.IsSuccess))
            {
                return ExitCode.Success;
            }

            var failedTestsBuilder = new StringBuilder();
            failedTestsBuilder.AppendLine("The following DLL files were not tested successfully:");
            foreach (Tuple<string, ExitCode> result in results.Where(r => !r.Item2.IsSuccess))
            {
                failedTestsBuilder.AppendLine(result.Item1);
            }

            logger.WriteError(failedTestsBuilder.ToString());

            return ExitCode.Failure;
        }

        private void EnsureNUnitReportDirectoryExists(string reportFile)
        {
            var fileInfo = new FileInfo(reportFile);

// ReSharper disable AssignNullToNotNullAttribute
            if (!Directory.Exists(fileInfo.DirectoryName))
            {
                Directory.CreateDirectory(fileInfo.DirectoryName);
            }

// ReSharper restore AssignNullToNotNullAttribute
        }
    }
}
