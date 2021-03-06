﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Arbor.Exceptions;
using Arbor.Processing.Core;
using Arbor.X.Core.BuildVariables;
using Arbor.X.Core.IO;
using Arbor.X.Core.Logging;
using JetBrains.Annotations;

namespace Arbor.X.Core.Tools.Cleanup
{
    [Priority(41)]
    [UsedImplicitly]
    public class ArtifactCleanup : ITool
    {
        public async Task<ExitCode> ExecuteAsync(
            ILogger logger,
            IReadOnlyCollection<IVariable> buildVariables,
            CancellationToken cancellationToken)
        {
            bool cleanupBeforeBuildEnabled =
                buildVariables.GetBooleanByKey(
                    WellKnownVariables.CleanupArtifactsBeforeBuildEnabled,
                    false);

            if (!cleanupBeforeBuildEnabled)
            {
                logger.WriteVerbose("Cleanup before build is disabled");
                return ExitCode.Success;
            }

            string artifactsPath = buildVariables.Require(WellKnownVariables.Artifacts).ThrowIfEmptyValue().Value;

            var artifactsDirectory = new DirectoryInfo(artifactsPath);

            if (!artifactsDirectory.Exists)
            {
                return ExitCode.Success;
            }

            int maxAttempts = 5;

            int attemptCount = 1;

            bool cleanupSucceeded = false;

            while (attemptCount <= maxAttempts && !cleanupSucceeded)
            {
                bool result = TryCleanup(logger, artifactsDirectory, attemptCount == maxAttempts);

                if (result)
                {
                    logger.WriteVerbose($"Cleanup succeeded on attempt {attemptCount}");
                    cleanupSucceeded = true;
                }
                else
                {
                    logger.WriteVerbose(
                        $"Attempt {attemptCount} of {maxAttempts} failed, could not cleanup the artifacts folder, retrying");
                    await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
                }

                attemptCount++;
            }

            return ExitCode.Success;
        }

        private static bool TryCleanup(
            ILogger logger,
            DirectoryInfo artifactsDirectory,
            bool throwExceptionOnFailure = false)
        {
            try
            {
                DoCleanup(logger, artifactsDirectory);
            }
            catch (Exception ex)
            {
                if (ex.IsFatal())
                {
                    throw;
                }

                if (throwExceptionOnFailure)
                {
                    throw;
                }

                return false;
            }

            return true;
        }

        private static void DoCleanup(ILogger logger, DirectoryInfo artifactsDirectory)
        {
            logger.Write(
                $"Artifact cleanup is enabled, removing all files and folders in '{artifactsDirectory.FullName}'");

            artifactsDirectory.DeleteIfExists();
            artifactsDirectory.Refresh();
            artifactsDirectory.EnsureExists();
        }
    }
}
