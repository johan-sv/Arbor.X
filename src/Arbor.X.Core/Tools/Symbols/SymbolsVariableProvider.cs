﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Arbor.X.Core.BuildVariables;
using Arbor.X.Core.Logging;

namespace Arbor.X.Core.Tools.Symbols
{
    public class SymbolsVariableProvider : IVariableProvider
    {
        public Task<IEnumerable<IVariable>> GetEnvironmentVariablesAsync(ILogger logger,
            IReadOnlyCollection<IVariable> buildVariables)
        {

            //TODO add symbol api and key
            var variables = new List<IVariable>
                            {
                                new EnvironmentVariable(WellKnownVariables.ExternalTools_SymbolServer_ApiKey,
                                    "TODO"),
                                new EnvironmentVariable(WellKnownVariables.ExternalTools_SymbolServer_Uri,
                                    "TODO")
                            };

            return Task.FromResult<IEnumerable<IVariable>>(variables);
        }
    }
}