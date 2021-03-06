﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Arbor.Aesculus.Core;
using Arbor.Exceptions;
using Arbor.Processing;
using Arbor.Processing.Core;
using Arbor.X.Core.BuildVariables;
using Arbor.X.Core.GenericExtensions;
using Arbor.X.Core.IO;
using Arbor.X.Core.Logging;
using Arbor.X.Core.Parsing;
using Arbor.X.Core.ProcessUtils;
using Arbor.X.Core.Tools.Git;
using Arbor.X.Core.Tools.Kudu;
using JetBrains.Annotations;

namespace Arbor.X.Bootstrapper
{
    public class Bootstrapper
    {
        private const int MaxBuildTimeInSeconds = 600;
        private static readonly string _Prefix = $"[{nameof(Arbor)}.{nameof(X)}.{nameof(Bootstrapper)}] ";
        private readonly ILogger _logger;
        private bool _directoryCloneEnabled;

        private bool _failed;
        private BootstrapStartOptions _startOptions;

        public Bootstrapper(LogLevel logLevel)
        {
            var nlogLogger = new NLogLogger(logLevel);

            if (Debugger.IsAttached)
            {
                _logger = new DebugLogger(nlogLogger);
            }
            else
            {
                _logger = nlogLogger;
            }

            _logger.WriteVerbose($"{_Prefix}LogLevel is {logLevel}");
        }

        public async Task<ExitCode> StartAsync(string[] args)
        {
            BootstrapStartOptions startOptions;
            if (Debugger.IsAttached)
            {
                startOptions = await StartWithDebuggerAsync(args);
            }
            else
            {
                startOptions = BootstrapStartOptions.Parse(args);
            }

            ExitCode exitCode = await StartAsync(startOptions);

            _logger.Write($"Bootstrapper exit code: {exitCode}");

            if (_failed)
            {
                exitCode = ExitCode.Failure;
            }

            return exitCode;
        }

        public async Task<ExitCode> StartAsync(BootstrapStartOptions startOptions)
        {
            _startOptions = startOptions ?? new BootstrapStartOptions();

            SetEnvironmentVariables();

            var stopwatch = new Stopwatch();
            stopwatch.Start();
            ExitCode exitCode;
            try
            {
                exitCode = await TryStartAsync();
                stopwatch.Stop();
            }
            catch (AggregateException ex)
            {
                stopwatch.Stop();
                exitCode = ExitCode.Failure;
                _logger.WriteError(ex.ToString(), _Prefix);

                foreach (Exception innerEx in ex.InnerExceptions)
                {
                    _logger.WriteError(innerEx.ToString(), _Prefix);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                exitCode = ExitCode.Failure;
                _logger.WriteError(ex.ToString(), _Prefix);
            }

            ParseResult<int> exitDelayInMilliseconds =
                Environment.GetEnvironmentVariable(WellKnownVariables.BootstrapperExitDelayInMilliseconds)
                    .TryParseInt32(0);

            if (exitDelayInMilliseconds > 0)
            {
                _logger.Write(
                    $"Delaying bootstrapper exit with {exitDelayInMilliseconds} milliseconds as specified in '{WellKnownVariables.BootstrapperExitDelayInMilliseconds}'",
                    _Prefix);
                await Task.Delay(TimeSpan.FromMilliseconds(exitDelayInMilliseconds));
            }

            _logger.Write(
                $"Arbor.X.Bootstrapper total inclusive Arbor.X.Build elapsed time in seconds: {stopwatch.Elapsed.TotalSeconds.ToString("F")}",
                _Prefix);

            return exitCode;
        }

        private static void KillAllProcessesSpawnedBy(uint parentProcessId, ILogger logger)
        {
            logger.WriteDebug("Finding processes spawned by process with Id [" + parentProcessId + "]");

            var searcher =
                new ManagementObjectSearcher($"SELECT * FROM Win32_Process WHERE ParentProcessId={parentProcessId}");
            ManagementObjectCollection collection = searcher.Get();
            if (collection.Count > 0)
            {
                logger.WriteDebug(
                    $"Killing [{collection.Count}] processes spawned by process with Id [{parentProcessId}]");

                foreach (ManagementBaseObject item in collection)
                {
                    uint childProcessId = (uint)item["ProcessId"];
                    if ((int)childProcessId != Process.GetCurrentProcess().Id)
                    {
                        KillAllProcessesSpawnedBy(childProcessId, logger);

                        try
                        {
                            Process childProcess = Process.GetProcessById((int)childProcessId);
                            if (!childProcess.HasExited)
                            {
                                logger.WriteDebug(
                                    $"Killing child process [{childProcess.ProcessName}] with Id [{childProcessId}]");
                                childProcess.Kill();

                                logger.WriteVerbose($"Child process with id {childProcessId} was killed");
                            }
                        }
                        catch (Exception ex) when (!ex.IsFatal() &&
                                                   (ex is ArgumentException || ex is InvalidOperationException))
                        {
                            logger.WriteWarning($"Child process with id {childProcessId} could not be killed");
                        }
                    }
                }
            }
        }

        private async Task<BootstrapStartOptions> StartWithDebuggerAsync([NotNull] string[] args)
        {
            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            string baseDir = VcsPathHelper.FindVcsRootPath(AppDomain.CurrentDomain.BaseDirectory);

            var tempDirectory = new DirectoryInfo(Path.Combine(
                Path.GetTempPath(),
                $"{DefaultPaths.TempPathPrefix}_Boot_Debug",
                DateTime.Now.ToString("yyyyMMddHHmmssfff")));

            tempDirectory.EnsureExists();

            WriteDebug($"Using temp directory '{tempDirectory}'");

            await DirectoryCopy.CopyAsync(baseDir, tempDirectory.FullName);

            Environment.SetEnvironmentVariable(WellKnownVariables.BranchNameVersionOverrideEnabled, "true");
            Environment.SetEnvironmentVariable(WellKnownVariables.VariableOverrideEnabled, "true");

            var bootstrapStartOptions = new BootstrapStartOptions(
                tempDirectory.FullName,
                true,
                "refs/heads/develop/12.34.56");

            WriteDebug("Starting with debugger attached");

            return bootstrapStartOptions;
        }

        private void WriteDebug(string message)
        {
            Debug.WriteLine(_Prefix + message);
            _logger.WriteDebug(message, _Prefix);
        }

        private void SetEnvironmentVariables()
        {
            if (!string.IsNullOrWhiteSpace(_startOptions.BaseDir) && Directory.Exists(_startOptions.BaseDir))
            {
                Environment.SetEnvironmentVariable(WellKnownVariables.SourceRoot, _startOptions.BaseDir);
            }

            if (_startOptions.PrereleaseEnabled.HasValue)
            {
                Environment.SetEnvironmentVariable(
                    WellKnownVariables.AllowPrerelease,
                    _startOptions.PrereleaseEnabled.Value.ToString().ToLowerInvariant());
            }

            if (!string.IsNullOrWhiteSpace(_startOptions.BranchName))
            {
                Environment.SetEnvironmentVariable(WellKnownVariables.BranchName, _startOptions.BranchName);
            }
        }

        private async Task<ExitCode> TryStartAsync()
        {
            _logger.Write("Starting Arbor.X Bootstrapper", _Prefix);

            string directoryCloneValue = Environment.GetEnvironmentVariable(WellKnownVariables.DirectoryCloneEnabled);

            _directoryCloneEnabled = directoryCloneValue
                .TryParseBool(true);

            if (!_directoryCloneEnabled)
            {
                _logger.WriteVerbose(
                    $"Environment variable '{WellKnownVariables.DirectoryCloneEnabled}' has value '{directoryCloneValue}'",
                    _Prefix);
            }

            string baseDir = await GetBaseDirectoryAsync(_startOptions);

            DirectoryInfo buildDir = new DirectoryInfo(Path.Combine(baseDir, "build")).EnsureExists();

            _logger.WriteVerbose($"Using base directory '{baseDir}'", _Prefix);

            string customNuGetPath =
                Environment.GetEnvironmentVariable(WellKnownVariables.ExternalTools_NuGet_ExePath_Custom);

            string nugetExePath;

            if (!string.IsNullOrWhiteSpace(customNuGetPath) && File.Exists(customNuGetPath))
            {
                nugetExePath = customNuGetPath;
            }
            else
            {
                nugetExePath = Path.Combine(buildDir.FullName, "nuget.exe");

                bool nuGetExists = await TryDownloadNuGetAsync(buildDir.FullName, nugetExePath);

                if (!nuGetExists)
                {
                    _logger.WriteError(
                        $"NuGet.exe could not be downloaded and it does not already exist at path '{nugetExePath}'",
                        _Prefix);
                    return ExitCode.Failure;
                }
            }

            string outputDirectoryPath = await DownloadNuGetPackageAsync(buildDir.FullName, nugetExePath);

            if (string.IsNullOrWhiteSpace(outputDirectoryPath))
            {
                return ExitCode.Failure;
            }

            ExitCode exitCode;
            try
            {
                ExitCode buildToolsResult = await RunBuildToolsAsync(buildDir.FullName, outputDirectoryPath);

                if (buildToolsResult.IsSuccess)
                {
                    _logger.Write("The build tools succeeded", _Prefix);
                }
                else
                {
                    _logger.WriteError(
                        $"The build tools process was not successful, exit code {buildToolsResult}",
                        _Prefix);
                }

                exitCode = buildToolsResult;
            }
            catch (TaskCanceledException)
            {
                try
                {
                    if (Environment.GetEnvironmentVariable("KillSpawnedProcess").TryParseBool(true))
                    {
                        KillAllProcessesSpawnedBy((uint)Process.GetCurrentProcess().Id, _logger);
                    }
                }
                catch (Exception ex) when (!ex.IsFatal())
                {
                    _logger.WriteError(ex.ToString());
                }

                _logger.WriteError("The build timed out", _Prefix);
                exitCode = ExitCode.Failure;
                _failed = true;
            }

            return exitCode;
        }

        private async Task<string> DownloadNuGetPackageAsync(string buildDir, string nugetExePath)
        {
            const string buildToolPackageName = "Arbor.X";

            string outputDirectoryPath = Path.Combine(buildDir, buildToolPackageName);

            var outputDirectory = new DirectoryInfo(outputDirectoryPath);

            bool reinstall = !outputDirectory.Exists ||
                             Environment.GetEnvironmentVariable(WellKnownVariables.NuGetReinstallArborPackageEnabled)
                                 .TryParseBool(true);

            if (!reinstall)
            {
                return outputDirectoryPath;
            }

            outputDirectory.DeleteIfExists();
            outputDirectory.EnsureExists();

            string version = Environment.GetEnvironmentVariable(WellKnownVariables.ArborXNuGetPackageVersion);

            var nugetArguments = new List<string>
            {
                "install",
                buildToolPackageName,
                "-ExcludeVersion",
                "-OutputDirectory",
                buildDir.TrimEnd('\\')
            };

            if (LogLevel.Verbose.Level <= _logger.LogLevel.Level)
            {
                nugetArguments.Add("-Verbosity");
                nugetArguments.Add("detailed");
            }

            string nuGetSource = Environment.GetEnvironmentVariable(WellKnownVariables.ArborXNuGetPackageSource);

            if (!string.IsNullOrWhiteSpace(nuGetSource))
            {
                nugetArguments.Add("-Source");
                nugetArguments.Add(nuGetSource);
            }

            string noCache = Environment.GetEnvironmentVariable(WellKnownVariables.ArborXNuGetPackageNoCacheEnabled);

            if (noCache.TryParseBool(false))
            {
                nugetArguments.Add("-NoCache");
            }

            if (!string.IsNullOrWhiteSpace(version))
            {
                nugetArguments.Add("-Version");
                nugetArguments.Add(version);

                _logger.WriteVerbose(
                    $"'{WellKnownVariables.ArborXNuGetPackageVersion}' flag is set, using specific version of Arbor.X: {version}",
                    _Prefix);
            }
            else
            {
                _logger.WriteVerbose(
                    $"'{WellKnownVariables.ArborXNuGetPackageVersion}' flag is not set, using latest version of Arbor.X",
                    _Prefix);

                bool allowPrerelease;
                if (_startOptions.PrereleaseEnabled.HasValue)
                {
                    allowPrerelease = _startOptions.PrereleaseEnabled.Value;

                    if (allowPrerelease)
                    {
                        _logger.WriteVerbose(
                            "Prerelease option is set via start options, using latest version of Arbor.X allowing prerelease versions",
                            _Prefix);
                    }
                }
                else
                {
                    allowPrerelease =
                        Environment.GetEnvironmentVariable(WellKnownVariables.AllowPrerelease)
                            .TryParseBool(false);

                    if (allowPrerelease)
                    {
                        _logger.WriteVerbose(
                            $"'{WellKnownVariables.AllowPrerelease}' flag is set, using latest version of Arbor.X allowing prerelease versions",
                            _Prefix);
                    }
                    else
                    {
                        _logger.WriteVerbose(
                            $"'{WellKnownVariables.AllowPrerelease}' flag is not set, using latest stable version of Arbor.X",
                            _Prefix);
                    }
                }

                if (allowPrerelease)
                {
                    nugetArguments.Add("-Prerelease");
                }
            }

            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(MaxBuildTimeInSeconds));

            ExitCode exitCode = await ProcessRunner.ExecuteAsync(
                nugetExePath,
                arguments: nugetArguments,
                cancellationToken: cancellationTokenSource.Token,
                standardOutLog: _logger.Write,
                standardErrorAction: _logger.WriteError,
                toolAction: _logger.Write,
                verboseAction: _logger.WriteVerbose,
                addProcessRunnerCategory: true,
                addProcessNameAsLogCategory: true,
                parentPrefix: _Prefix);

            if (!exitCode.IsSuccess)
            {
                outputDirectoryPath = string.Empty;
            }

            return outputDirectoryPath;
        }

        private async Task<string> GetBaseDirectoryAsync(BootstrapStartOptions startOptions)
        {
            string baseDir;

            if (!string.IsNullOrWhiteSpace(startOptions.BaseDir) && Directory.Exists(startOptions.BaseDir))
            {
                _logger.Write($"Using base directory '{startOptions.BaseDir}' from start options", _Prefix);

                baseDir = startOptions.BaseDir;
            }
            else
            {
                if (IsBetterRunOnLocalTempStorage() && await IsCurrentDirectoryClonableAsync())
                {
                    string clonedDirectory = await CloneDirectoryAsync();

                    baseDir = clonedDirectory;
                }
                else
                {
                    baseDir = VcsPathHelper.FindVcsRootPath();
                }
            }

            return baseDir;
        }

        private bool IsBetterRunOnLocalTempStorage()
        {
            bool isKuduAware = KuduHelper.IsKuduAware(
                EnvironmentVariableHelper.GetBuildVariablesFromEnvironmentVariables(_logger),
                _logger);

            bool isBetterRunOnLocalTempStorage = isKuduAware;

            _logger.WriteVerbose("Is Kudu-aware: " + isKuduAware, _Prefix);

            return isBetterRunOnLocalTempStorage;
        }

        private async Task<string> CloneDirectoryAsync()
        {
            string targetDirectoryPath = Path.Combine(
                Path.GetTempPath(),
                DefaultPaths.TempPathPrefix,
                "R",
                Guid.NewGuid().ToString().Substring(0, 8));

            var targetDirectory = new DirectoryInfo(targetDirectoryPath);

            targetDirectory.EnsureExists();

            string gitExePath = GitHelper.GetGitExePath(_logger);

            string sourceRoot = VcsPathHelper.TryFindVcsRootPath();

            IEnumerable<string> cloneArguments = new List<string>
            {
                "clone",
                sourceRoot,
                targetDirectory.FullName
            };

            _logger.WriteVerbose($"Using temp storage to clone: '{targetDirectory.FullName}'", _Prefix);

            ExitCode cloneExitCode = await ProcessHelper.ExecuteAsync(
                gitExePath,
                cloneArguments,
                _logger,
                addProcessNameAsLogCategory: true,
                addProcessRunnerCategory: true,
                parentPrefix: _Prefix);

            if (!cloneExitCode.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Could not clone directory '{sourceRoot}' to '{targetDirectory.FullName}'");
            }

            return targetDirectory.FullName;
        }

        private async Task<bool> IsCurrentDirectoryClonableAsync()
        {
            if (!_directoryCloneEnabled)
            {
                _logger.WriteVerbose("Directory clone is disabled");
                return false;
            }

            _logger.WriteVerbose("Directory clone is enabled");

            string sourceRoot = VcsPathHelper.TryFindVcsRootPath();

            if (string.IsNullOrWhiteSpace(sourceRoot))
            {
                _logger.WriteWarning("Could not find source root", _Prefix);
                return false;
            }

            bool isClonable = false;

            string gitExePath = GitHelper.GetGitExePath(_logger);

            if (!string.IsNullOrWhiteSpace(gitExePath))
            {
                string gitDir = Path.Combine(sourceRoot, ".git");

                var statusAllArguments = new[]
                {
                    $"--git-dir={gitDir}",
                    $"--work-tree={sourceRoot}",
                    "status"
                };

                var argumentVariants = new List<string[]> { new[] { "status" }, statusAllArguments };

                foreach (string[] argumentVariant in argumentVariants)
                {
                    ExitCode statusExitCode = await ProcessRunner.ExecuteAsync(
                        gitExePath,
                        arguments: argumentVariant,
                        standardOutLog: _logger.WriteVerbose,
                        standardErrorAction: _logger.WriteVerbose,
                        toolAction: _logger.Write,
                        verboseAction: _logger.WriteVerbose);

                    if (statusExitCode.IsSuccess)
                    {
                        isClonable = true;
                        break;
                    }
                }
            }

            _logger.WriteVerbose($"Is directory clonable: {isClonable}");

            return isClonable;
        }

        private async Task<ExitCode> RunBuildToolsAsync(string buildDir, string buildToolDirectoryName)
        {
            string buildToolDirectoryPath = Path.Combine(buildDir, buildToolDirectoryName);

            var buildToolDirectory = new DirectoryInfo(buildToolDirectoryPath);

            List<FileInfo> exeFiles =
                buildToolDirectory.GetFiles("*.exe", SearchOption.TopDirectoryOnly)
                    .Where(file => !file.Name.Equals("nuget.exe", StringComparison.InvariantCultureIgnoreCase))
                    .ToList();

            if (exeFiles.Count != 1)
            {
                PrintInvalidExeFileCount(exeFiles, buildToolDirectoryPath);
                return ExitCode.Failure;
            }

            FileInfo buildToolExe = exeFiles.Single();

            string timeoutKey = WellKnownVariables.BuildToolTimeoutInSeconds;
            string timeoutInSecondsFromEnvironment = Environment.GetEnvironmentVariable(timeoutKey);

            ParseResult<int> parseResult =
                timeoutInSecondsFromEnvironment.TryParseInt32(MaxBuildTimeInSeconds);

            if (parseResult.Parsed)
            {
                _logger.WriteVerbose($"Using timeout from environment variable {timeoutKey}", _Prefix);
            }

            int usedTimeoutInSeconds = parseResult;

            _logger.Write($"Using build timeout {usedTimeoutInSeconds} seconds", _Prefix);

            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(usedTimeoutInSeconds));

            const string buildApplicationPrefix = "[Arbor.X] ";

            IEnumerable<string> arguments = Enumerable.Empty<string>();
            ExitCode result = await ProcessRunner.ExecuteAsync(
                buildToolExe.FullName,
                cancellationTokenSource.Token,
                arguments,
                (message, prefix) => _logger.Write(message, buildApplicationPrefix),
                (message, prefix) => _logger.WriteError(message, buildApplicationPrefix),
                _logger.Write,
                _logger.WriteVerbose);

            return result;
        }

        private void PrintInvalidExeFileCount(List<FileInfo> exeFiles, string buildToolDirectoryPath)
        {
            string multiple =
                $"Found {exeFiles.Count} such files: {string.Join(", ", exeFiles.Select(file => file.Name))}";
            const string Single = ". Found no such files";
            string found = exeFiles.Any() ? Single : multiple;

            _logger.WriteError(
                $"Expected directory {buildToolDirectoryPath} to contain exactly one executable file with extensions .exe. {found}",
                _Prefix);
        }

        private async Task<bool> TryDownloadNuGetAsync(string baseDir, string targetFile)
        {
            bool update = Environment.GetEnvironmentVariable(WellKnownVariables.NuGetVersionUpdatedEnabled)
                .TryParseBool(false);

            bool hasNugetExe = File.Exists(targetFile);

            try
            {
                if (!hasNugetExe)
                {
                    await DownloadNuGetExeAsync(baseDir, targetFile);
                    update = false;
                }
            }
            catch (HttpRequestException ex)
            {
                if (!File.Exists(targetFile))
                {
                    return false;
                }

                update = true;

                _logger.WriteWarning($"NuGet.exe could not be downloaded, using existing nuget.exe. {ex}", _Prefix);
            }

            if (update)
            {
                try
                {
                    var arguments = new List<string> { "update", "-self" };

                    await ProcessHelper.ExecuteAsync(
                        targetFile,
                        arguments,
                        _logger,
                        addProcessNameAsLogCategory: true,
                        addProcessRunnerCategory: true,
                        parentPrefix: _Prefix);
                }
                catch (Exception ex)
                {
                    _logger.WriteError(ex.ToString());
                }
            }

            bool exists = File.Exists(targetFile);

            return exists;
        }

        private async Task DownloadNuGetExeAsync(string baseDir, string targetFile)
        {
            string tempFile = Path.Combine(baseDir, "nuget.exe.tmp");

            const string nugetExeUri = "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe";

            string nugetDownloadUriEnvironmentVariable =
                Environment.GetEnvironmentVariable(WellKnownVariables.NuGetExeDownloadUri);

            if (string.IsNullOrWhiteSpace(nugetDownloadUriEnvironmentVariable)
                || !Uri.TryCreate(nugetDownloadUriEnvironmentVariable, UriKind.Absolute, out Uri nugetDownloadUri))
            {
                nugetDownloadUri = new Uri(nugetExeUri, UriKind.Absolute);
                _logger.WriteVerbose($"Downloading nuget.exe from default URI, '{nugetExeUri}'", _Prefix);
            }
            else
            {
                _logger.WriteVerbose(
                    $"Downloading nuget.exe from user specified URI '{nugetDownloadUriEnvironmentVariable}'",
                    _Prefix);
            }

            _logger.WriteVerbose($"Downloading '{nugetDownloadUri}' to '{tempFile}'", _Prefix);

            using (var client = new HttpClient())
            {
                using (Stream stream = await client.GetStreamAsync(nugetDownloadUri))
                {
                    using (var fs = new FileStream(tempFile, FileMode.Create))
                    {
                        await stream.CopyToAsync(fs);
                    }
                }

                if (File.Exists(tempFile))
                {
                    File.Copy(tempFile, targetFile, true);
                    _logger.WriteVerbose($"Copied '{tempFile}' to '{targetFile}'", _Prefix);
                    File.Delete(tempFile);
                    _logger.WriteVerbose($"Deleted temp file '{tempFile}'", _Prefix);
                }
            }
        }
    }
}
