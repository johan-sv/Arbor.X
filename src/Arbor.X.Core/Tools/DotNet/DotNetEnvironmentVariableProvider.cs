﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arbor.Processing.Core;
using Arbor.X.Core.BuildVariables;
using Arbor.X.Core.Logging;
using Arbor.X.Core.Tools.Cleanup;
using JetBrains.Annotations;

namespace Arbor.X.Core.Tools.DotNet
{
    [UsedImplicitly]
    public class DotNetEnvironmentVariableProvider : IVariableProvider
    {
        public int Order => VariableProviderOrder.Ignored;

        public async Task<IEnumerable<IVariable>> GetEnvironmentVariablesAsync(
            ILogger logger,
            IReadOnlyCollection<IVariable> buildVariables,
            CancellationToken cancellationToken)
        {
            string dotNetExePath =
                buildVariables.GetVariableValueOrDefault(WellKnownVariables.DotNetExePath, string.Empty);

            if (!string.IsNullOrWhiteSpace(dotNetExePath))
            {
                return Array.Empty<IVariable>();
            }

            if (string.IsNullOrWhiteSpace(dotNetExePath))
            {
                var sb = new List<string>(10);

                string winDir = Environment.GetEnvironmentVariable("WINDIR");

                if (string.IsNullOrWhiteSpace(winDir))
                {
                    logger.WriteWarning("Error finding Windows directory");
                    return Array.Empty<IVariable>();
                }

                string whereExePath = Path.Combine(winDir, "System32", "where.exe");

                ExitCode exitCode = await Processing.ProcessRunner.ExecuteAsync(
                    whereExePath,
                    arguments: new[] { "dotnet.exe" },
                    standardOutLog: (message, _) => sb.Add(message),
                    cancellationToken: cancellationToken);

                if (!exitCode.IsSuccess)
                {
                    logger.WriteError("Failed to find dotnet.exe with where.exe");
                }

                dotNetExePath =
                    sb.FirstOrDefault(item => item.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))?.Trim();
            }
            else if (!File.Exists(dotNetExePath))
            {
                logger.WriteWarning(
                    $"The specified path to dotnet.exe is from variable '{WellKnownVariables.DotNetExePath}' is set to '{dotNetExePath}' but the file does not exist");
                return Array.Empty<IVariable>();
            }

            return new[] { new EnvironmentVariable(WellKnownVariables.DotNetExePath, dotNetExePath) };
        }
    }
}
