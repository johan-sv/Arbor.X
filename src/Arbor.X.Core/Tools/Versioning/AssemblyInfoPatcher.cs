﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Arbor.Sorbus.Core;
using Arbor.X.Core.BuildVariables;
using Arbor.X.Core.Logging;

namespace Arbor.X.Core.Tools.Versioning
{
    [Priority(200)]
    public class AssemblyInfoPatcher : ITool
    {
        public Task<ExitCode> ExecuteAsync(ILogger logger, IReadOnlyCollection<IVariable> buildVariables)
        {
            var app = new AssemblyPatcherApp();

            var netAssemblyVersionVar = buildVariables.SingleOrDefault(@var => @var.Key == WellKnownVariables.NetAssemblyVersion);
            string netAssemblyVersion;

            if (netAssemblyVersionVar == null || string.IsNullOrWhiteSpace(netAssemblyVersionVar.Value))
            {
                logger.WriteWarning(string.Format("The build variable {0} is not defined or empty",
                    WellKnownVariables.NetAssemblyVersion));
                netAssemblyVersion = "0.0.1.0";

                logger.WriteWarning(string.Format("Using fall-back version {0}", netAssemblyVersion));
            }
            else
            {
                netAssemblyVersion = netAssemblyVersionVar.Value;
            }

            var assemblyVersion = new Version(netAssemblyVersion);


            var netAssemblyFileVersionVar = buildVariables.SingleOrDefault(@var => @var.Key == WellKnownVariables.NetAssemblyFileVersion);
            string netAssemblyFileVersion;

            if (netAssemblyFileVersionVar == null || string.IsNullOrWhiteSpace(netAssemblyFileVersionVar.Value))
            {
                logger.WriteWarning(string.Format("The build variable {0} is not defined or empty",
                    WellKnownVariables.NetAssemblyFileVersion));
                netAssemblyFileVersion = "0.0.1.1";

                logger.WriteWarning(string.Format("Using fall-back version {0}", netAssemblyFileVersion));
            }
            else
            {
                netAssemblyFileVersion = netAssemblyFileVersionVar.Value;
            }

            var assemblyFileVersion = new Version(netAssemblyFileVersion);

            try
            {
                app.Patch(new AssemblyVersion(assemblyVersion), new AssemblyFileVersion(assemblyFileVersion));
            }
            catch (Exception ex)
            {
                logger.WriteError(string.Format("Could not patch assembly infos. {0}", ex));
                return Task.FromResult(ExitCode.Failure);
            }
            return Task.FromResult(ExitCode.Success);
        }
    }
}