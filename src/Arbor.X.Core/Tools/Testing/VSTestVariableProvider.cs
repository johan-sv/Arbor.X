﻿using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Arbor.X.Core.BuildVariables;
using Arbor.X.Core.IO;
using Arbor.X.Core.Tools.Cleanup;
using JetBrains.Annotations;
using Serilog;

namespace Arbor.X.Core.Tools.Testing
{
    [UsedImplicitly]
    public class VsTestVariableProvider : IVariableProvider
    {
        public int Order => VariableProviderOrder.Ignored;

        public Task<ImmutableArray<IVariable>> GetBuildVariablesAsync(
            ILogger logger,
            IReadOnlyCollection<IVariable> buildVariables,
            CancellationToken cancellationToken)
        {
            string reportPath = buildVariables.Require(WellKnownVariables.ReportPath).ThrowIfEmptyValue().Value;

            var reportDirectory = new DirectoryInfo(reportPath);

            var vsTestReportPathDirectory = new DirectoryInfo(Path.Combine(reportDirectory.FullName, "VSTest"));

            vsTestReportPathDirectory.EnsureExists();

            IVariable[] environmentVariables =
            {
                new BuildVariable(
                    WellKnownVariables.ExternalTools_VSTest_ReportPath,
                    vsTestReportPathDirectory.FullName)
            };

            return Task.FromResult(environmentVariables.ToImmutableArray());
        }
    }
}
