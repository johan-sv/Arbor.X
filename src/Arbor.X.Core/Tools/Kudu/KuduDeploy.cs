﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Arbor.Processing.Core;
using Arbor.X.Core.BuildVariables;
using Arbor.X.Core.IO;
using Arbor.X.Core.Logging;
using Arbor.X.Core.Tools.Git;
using JetBrains.Annotations;

namespace Arbor.X.Core.Tools.Kudu
{
    [Priority(1100)]
    [UsedImplicitly]
    public class KuduDeploy : ITool
    {
        private string _artifacts;
        private bool _clearTarget;
        private bool _deleteExistingAppOfflineHtm;
        private BranchName _deployBranch;
        private string _deploymentTargetDirectory;
        private bool _excludeAppData;
        private string _ignoreDeleteDirectories;
        private string _ignoreDeleteFiles;
        private string _kuduConfigurationFallback;
        private bool _kuduEnabled;
        private bool _useAppOfflineFile;
        private string _vcsRoot;

        public async Task<ExitCode> ExecuteAsync(
            ILogger logger,
            IReadOnlyCollection<IVariable> buildVariables,
            CancellationToken cancellationToken)
        {
            _kuduEnabled = buildVariables.HasKey(WellKnownVariables.ExternalTools_Kudu_Enabled) &&
                           bool.Parse(buildVariables.Require(WellKnownVariables.ExternalTools_Kudu_Enabled).Value);
            if (!_kuduEnabled)
            {
                return ExitCode.Success;
            }

            _vcsRoot = buildVariables.Require(WellKnownVariables.SourceRoot).ThrowIfEmptyValue().Value;
            _artifacts = buildVariables.Require(WellKnownVariables.Artifacts).ThrowIfEmptyValue().Value;
            buildVariables.Require(WellKnownVariables.ExternalTools_Kudu_Platform).ThrowIfEmptyValue();
            _deployBranch = new BranchName(buildVariables
                .Require(WellKnownVariables.ExternalTools_Kudu_DeploymentBranchName)
                .Value);
            _deploymentTargetDirectory =
                buildVariables.Require(WellKnownVariables.ExternalTools_Kudu_DeploymentTarget).Value;

            _kuduConfigurationFallback = buildVariables.HasKey(WellKnownVariables.KuduConfigurationFallback)
                ? buildVariables.Require(WellKnownVariables.KuduConfigurationFallback).Value
                : string.Empty;

            _clearTarget = buildVariables.GetBooleanByKey(WellKnownVariables.KuduClearFilesAndDirectories, false);
            _useAppOfflineFile = buildVariables.GetBooleanByKey(WellKnownVariables.KuduUseAppOfflineHtmFile, false);
            _excludeAppData = buildVariables.GetBooleanByKey(WellKnownVariables.KuduExcludeDeleteAppData, true);
            _deleteExistingAppOfflineHtm =
                buildVariables.GetBooleanByKey(WellKnownVariables.KuduDeleteExistingAppOfflineHtmFile, true);
            _ignoreDeleteFiles =
                buildVariables.GetVariableValueOrDefault(WellKnownVariables.KuduIgnoreDeleteFiles, string.Empty);
            _ignoreDeleteDirectories =
                buildVariables.GetVariableValueOrDefault(WellKnownVariables.KuduIgnoreDeleteDirectories, string.Empty);

            string branchNameOverride =
                buildVariables.GetVariableValueOrDefault(
                    WellKnownVariables.ExternalTools_Kudu_DeploymentBranchNameOverride,
                    string.Empty);

            if (!string.IsNullOrWhiteSpace(branchNameOverride))
            {
                logger.Write(
                    $"Using branch name override '{branchNameOverride}' instead of branch name '{_deployBranch}'");
                _deployBranch = new BranchName(branchNameOverride);
            }

            var websitesDirectory = new DirectoryInfo(Path.Combine(_artifacts, "Websites"));

            if (!websitesDirectory.Exists)
            {
                logger.Write("No websites found. Ignoring Kudu deployment.");
                return ExitCode.Success;
            }

            DirectoryInfo[] builtWebsites = websitesDirectory.GetDirectories();

            if (!builtWebsites.Any())
            {
                logger.Write("No websites found. Ignoring Kudu deployment.");
                return ExitCode.Success;
            }

            DirectoryInfo websiteToDeploy;

            if (builtWebsites.Length == 1)
            {
                websiteToDeploy = builtWebsites.Single();
            }
            else
            {
                string siteToDeploy =
                    buildVariables.GetVariableValueOrDefault(WellKnownVariables.KuduSiteToDeploy, string.Empty);
                if (!string.IsNullOrWhiteSpace(siteToDeploy))
                {
                    DirectoryInfo foundDir = builtWebsites.SingleOrDefault(
                        dir => dir.Name.Equals(siteToDeploy, StringComparison.InvariantCultureIgnoreCase));

                    if (foundDir == null)
                    {
                        logger.WriteError(
                            $"Found {builtWebsites.Length} websites. Kudu deployment is specified for site {siteToDeploy} but it was not found");
                        return ExitCode.Failure;
                    }

                    websiteToDeploy = foundDir;
                }
                else
                {
                    logger.WriteError(
                        $"Found {builtWebsites.Length} websites. Kudu deployment is only supported with a single website. \r\nBuilt websites: {string.Join(Environment.NewLine, builtWebsites.Select(dir => dir.Name))}. You can use variable '{WellKnownVariables.KuduSiteToDeploy}' to specify a single website to be built");
                    return ExitCode.Failure;
                }
            }

            if (!websiteToDeploy.GetDirectories().Any())
            {
                logger.WriteError($"Could not find any platform for website {websiteToDeploy.Name}");
                return ExitCode.Failure;
            }

            if (websiteToDeploy.GetDirectories().Length > 1)
            {
                logger.WriteError($"Could not find exactly one platform for website {websiteToDeploy.Name}");
                return ExitCode.Failure;
            }

            DirectoryInfo platform = GetPlatform(websiteToDeploy);

            if (!platform.GetDirectories().Any())
            {
                logger.WriteError($"Could not find any configuration for website {websiteToDeploy.Name}");
                return ExitCode.Failure;
            }

            DirectoryInfo configuration = GetConfigurationDirectory(platform, logger);

            if (configuration == null)
            {
                logger.WriteError("No configuration for Kudu");
                return ExitCode.Failure;
            }

            string appOfflinePath = Path.Combine(_deploymentTargetDirectory, "app_offline.htm");

            logger.Write(
                $"___________________ Kudu deploy ___________________ \r\nDeploying website {websiteToDeploy.Name}, platform {platform.Name}, configuration {configuration.Name}");
            try
            {
                if (_useAppOfflineFile)
                {
                    logger.WriteVerbose($"Flag '{WellKnownVariables.KuduUseAppOfflineHtmFile}' is set");
                    try
                    {
                        using (var fs = new FileStream(appOfflinePath, FileMode.Create, FileAccess.Write))
                        {
                            using (var streamWriter = new StreamWriter(fs, Encoding.UTF8))
                            {
                                streamWriter.WriteLine(
                                    "File created by Arbor.X Kudu at {0} (UTC)",
                                    DateTime.UtcNow.ToString("O"));
                            }
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        logger.WriteWarning(
                            $"Could not create app_offline.htm file in '{_deploymentTargetDirectory}', {ex}");
                    }
                    catch (IOException ex)
                    {
                        logger.WriteWarning(
                            $"Could not create app_offline.htm file in '{_deploymentTargetDirectory}', {ex}");
                    }
                }
                else
                {
                    logger.WriteVerbose($"Flag '{WellKnownVariables.KuduUseAppOfflineHtmFile}' is not set");
                }

                if (_clearTarget)
                {
                    logger.WriteVerbose($"Flag '{WellKnownVariables.KuduClearFilesAndDirectories}' is set");
                    logger.Write($"Removing files and directories from target '{_deploymentTargetDirectory}'");
                    try
                    {
                        var directoryFilters = new List<string>();

                        if (_excludeAppData)
                        {
                            directoryFilters.Add("App_Data");
                        }

                        string[] customFileExcludes = GetExcludes(_ignoreDeleteFiles).ToArray();
                        string[] customDirectoryExcludes = GetExcludes(_ignoreDeleteDirectories).ToArray();

                        var fileFilters = new List<string>();

                        if (_useAppOfflineFile || !_deleteExistingAppOfflineHtm)
                        {
                            fileFilters.Add("app_offline.htm");
                        }

                        if (customDirectoryExcludes.Any())
                        {
                            logger.WriteVerbose("Adding directory ignore patterns " + string.Join(
                                                    "|",
                                                    $"'{customDirectoryExcludes.Select(item => (object)item)}'"));
                        }

                        if (customFileExcludes.Any())
                        {
                            logger.WriteVerbose("Adding file ignore patterns " + string.Join(
                                                    "|",
                                                    $"'{customFileExcludes.Select(item => (object)item)}'"));
                        }

                        directoryFilters.AddRange(customDirectoryExcludes);
                        fileFilters.AddRange(customFileExcludes);

                        var deleter = new DirectoryDelete(directoryFilters, fileFilters, logger);

                        deleter.Delete(_deploymentTargetDirectory, false, true);
                    }
                    catch (IOException ex)
                    {
                        logger.WriteWarning(
                            $"Could not clear all files and directories from target '{_deploymentTargetDirectory}', {ex}");
                    }
                }
                else
                {
                    logger.WriteVerbose(
                        $"Flag '{WellKnownVariables.KuduClearFilesAndDirectories}' is not set, skipping deleting files and directories from target '{_deploymentTargetDirectory}'");
                }

                logger.Write(
                    $"Copying files and directories from '{configuration.FullName}' to '{_deploymentTargetDirectory}'");

                try
                {
                    ExitCode exitCode = await DirectoryCopy.CopyAsync(
                        configuration.FullName,
                        _deploymentTargetDirectory,
                        logger,
                        rootDir: _vcsRoot,
                        pathLookupSpecificationOption: new PathLookupSpecification());

                    if (!exitCode.IsSuccess)
                    {
                        return exitCode;
                    }
                }
                catch (Exception ex)
                {
                    logger.WriteError($"Kudu deploy could not copy files {ex}");
                    return ExitCode.Failure;
                }
            }
            finally
            {
                if (_useAppOfflineFile || _deleteExistingAppOfflineHtm)
                {
                    if (File.Exists(appOfflinePath))
                    {
                        try
                        {
                            File.Delete(appOfflinePath);
                        }
                        catch (IOException ex)
                        {
                            logger.WriteWarning(
                                $"Could not delete app_offline.htm file in '{_deploymentTargetDirectory}', {ex}");
                        }
                    }
                }
            }

            return ExitCode.Success;
        }

        private IEnumerable<string> GetExcludes(string ignores)
        {
            if (string.IsNullOrWhiteSpace(ignores))
            {
                yield break;
            }

            string[] splitted = ignores.Split('|');

            foreach (string item in splitted)
            {
                yield return item;
            }
        }

        private DirectoryInfo GetConfigurationDirectory(DirectoryInfo platformDirectory, ILogger logger)
        {
            DirectoryInfo[] directoryInfos = platformDirectory.GetDirectories();

            if (directoryInfos.Length == 1)
            {
                DirectoryInfo directoryInfo = directoryInfos.Single();
                logger.Write("Found only one configuration: " + directoryInfo.Name);
                return directoryInfo;
            }

            if (_deployBranch.IsProductionBranch())
            {
                logger.Write($"Using deployment branch name {_deployBranch}");

                DirectoryInfo productionConfig =
                    directoryInfos.SingleOrDefault(
                        di => di.Name.Equals("production", StringComparison.InvariantCultureIgnoreCase));

                if (productionConfig != null)
                {
                    logger.Write($"On master or release branch, using {productionConfig.Name} configuration");
                    return productionConfig;
                }

                DirectoryInfo releaseConfig =
                    directoryInfos.SingleOrDefault(
                        di => di.Name.Equals("release", StringComparison.InvariantCultureIgnoreCase));

                if (releaseConfig != null)
                {
                    logger.Write($"On master or release branch, using {releaseConfig.Name} configuration");
                    return releaseConfig;
                }
            }
            else if (_deployBranch.IsDevelopBranch())
            {
                DirectoryInfo developConfig =
                    directoryInfos.SingleOrDefault(
                        di => di.Name.Equals("develop", StringComparison.InvariantCultureIgnoreCase) ||
                              di.Name.Equals("dev", StringComparison.InvariantCultureIgnoreCase));

                if (developConfig != null)
                {
                    logger.Write($"On develop branch, using {developConfig.Name} configuration");
                    return developConfig;
                }

                DirectoryInfo debugConfig =
                    directoryInfos.SingleOrDefault(
                        di => di.Name.Equals("debug", StringComparison.InvariantCultureIgnoreCase));

                if (debugConfig != null)
                {
                    logger.Write($"On develop branch, using {debugConfig.Name} configuration");
                    return debugConfig;
                }
            }
            else if (!string.IsNullOrWhiteSpace(_kuduConfigurationFallback))
            {
                DirectoryInfo configDir = directoryInfos.SingleOrDefault(
                    dir => dir.Name.Equals(_kuduConfigurationFallback, StringComparison.InvariantCultureIgnoreCase));

                logger.Write($"Kudu fallback is '{_kuduConfigurationFallback}'");

                if (configDir != null)
                {
                    logger.Write($"Using Kudu fallback configuration {configDir.Name}");

                    return configDir;
                }

                logger.WriteWarning($"Kudu fallback configuration '{_kuduConfigurationFallback}' was not found");
            }

            logger.WriteError(
                $"Could not determine Kudu deployment configuration: [{string.Join(", ", directoryInfos.Select(di => di.Name))}]");
            return null;
        }

        private DirectoryInfo GetPlatform(DirectoryInfo websiteToDeploy)
        {
            return websiteToDeploy.GetDirectories().Single();
        }
    }
}
