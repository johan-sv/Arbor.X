﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arbor.X.Core.BuildVariables;
using Arbor.X.Core.IO;
using Arbor.X.Core.Logging;

namespace Arbor.X.Core.Tools.Kudu
{
    [Priority(1100)]
    public class KuduDeploy : ITool
    {
        string _artifacts;
        string _deployBranchName;
        string _deploymentTargetDirectory;
        bool _kuduEnabled;
        string _platform;

        public async Task<ExitCode> ExecuteAsync(ILogger logger, IReadOnlyCollection<IVariable> buildVariables)
        {
            _kuduEnabled = bool.Parse(buildVariables.Require(WellKnownVariables.ExternalTools_Kudu_Enabled).Value);
            if (!_kuduEnabled)
            {
                return ExitCode.Success;
            }
            _artifacts = buildVariables.Require(WellKnownVariables.Artifacts).ThrowIfEmptyValue().Value;
            _platform = buildVariables.Require(WellKnownVariables.ExternalTools_Kudu_Platform).ThrowIfEmptyValue().Value;
            _deployBranchName = buildVariables.Require(WellKnownVariables.ExternalTools_Kudu_DeploymentBranchName).Value;
            _deploymentTargetDirectory =
                buildVariables.Require(WellKnownVariables.ExternalTools_Kudu_DeploymentTarget).Value;


            var websitesDirectory = new DirectoryInfo(Path.Combine(_artifacts, "Websites"));

            if (!websitesDirectory.Exists)
            {
                logger.Write("No websites found. Ignoring Kudu deployment.");
                return ExitCode.Success;
            }

            var builtWebsites = websitesDirectory.GetDirectories();

            if (!builtWebsites.Any())
            {
                logger.Write("No websites found. Ignoring Kudu deployment.");
                return ExitCode.Success;
            }

            if (builtWebsites.Count() > 1)
            {
                logger.WriteError(
                    string.Format(
                        "Found {0} websites. Kudu deployment is only supported with a single website. \r\nBuilt websites: {1}",
                        builtWebsites.Count(), string.Join(Environment.NewLine, builtWebsites.Select(dir => dir.Name))));

                return ExitCode.Failure;
            }

            var websiteToDeploy = builtWebsites.Single();

            if (!websiteToDeploy.GetDirectories().Any())
            {
                logger.WriteError("Could not find any platform for website " + websiteToDeploy.Name);
                return ExitCode.Failure;
            }


            if (websiteToDeploy.GetDirectories().Count() > 1)
            {
                logger.WriteError("Could not find exactly one platform for website " + websiteToDeploy.Name);
                return ExitCode.Failure;
            }

            var platform = GetPlatform(websiteToDeploy);

            if (!platform.GetDirectories().Any())
            {
                logger.WriteError("Could not find any configuration for website " + websiteToDeploy.Name);
                return ExitCode.Failure;
            }

            var configuration = GetConfiguration(platform, logger);

            if (configuration == null)
            {
                return ExitCode.Failure;
            }

            logger.Write(string.Format("___________________ Kudu deploy ___________________ \r\nDeploying website {0}, platform {1}, configuration {2}", websiteToDeploy.Name, platform.Name, configuration.Name));

            logger.Write(string.Format("Copying files and directories from '{0}' to '{1}'", configuration.FullName, _deploymentTargetDirectory));
            Copy(configuration.FullName, _deploymentTargetDirectory);

            return ExitCode.Success;
        }

        DirectoryInfo GetConfiguration(DirectoryInfo platformDirectory, ILogger logger)
        {
            var directoryInfos = platformDirectory.GetDirectories();

            if (directoryInfos.Count() == 1)
            {
                var directoryInfo = directoryInfos.Single();
                logger.Write("Found only one configuration: " + directoryInfo.Name);
                return directoryInfo;
            }

            if (_deployBranchName.Equals("master", StringComparison.InvariantCultureIgnoreCase) ||
                _deployBranchName.IndexOf("release", StringComparison.InvariantCultureIgnoreCase) >= 0)
            {
                logger.Write(string.Format("Using deployment branch name {0}", _deployBranchName));

                var productionConfig =
                    directoryInfos.SingleOrDefault(
                        di => di.Name.Equals("production", StringComparison.InvariantCultureIgnoreCase));

                if (productionConfig != null)
                {
                    logger.Write(string.Format("On master or release branch, using {0} configuration",
                        productionConfig.Name));
                    return productionConfig;
                }

                var releaseConfig =
                    directoryInfos.SingleOrDefault(
                        di => di.Name.Equals("release", StringComparison.InvariantCultureIgnoreCase));

                if (releaseConfig != null)
                {
                    logger.Write(string.Format("On master or release branch, using {0} configuration",
                        releaseConfig.Name));
                    return releaseConfig;
                }
            }

            logger.WriteError(string.Format("Could not determine configuration: [{0}]",
                string.Join(", ", directoryInfos.Select(di => di.Name))));
            return null;
        }

        DirectoryInfo GetPlatform(DirectoryInfo websiteToDeploy)
        {
            return websiteToDeploy.GetDirectories().Single();
        }

        void Copy(string sourceDir, string targetDir)
        {
            var sourceDirectory = new DirectoryInfo(sourceDir);
            new DirectoryInfo(targetDir).EnsureExists();

            foreach (var file in sourceDirectory.GetFiles())
                file.CopyTo(Path.Combine(targetDir, file.Name), overwrite: true);

            foreach (var directory in sourceDirectory.GetDirectories())
                Copy(directory.FullName, Path.Combine(targetDir, directory.Name));
        }
    }
}