﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Arbor.X.Core.BuildVariables;
using Arbor.X.Core.Logging;

namespace Arbor.X.Core.Tools
{
    public abstract class EnvironmentVerification : ITool
    {
        protected readonly List<string> RequiredValues = new List<string>();

        public Task<ExitCode> ExecuteAsync(ILogger logger, IReadOnlyCollection<IVariable> buildVariables)
        {
            var missingKeys =
                RequiredValues.Where(
                    @var =>
                        !buildVariables.Any(
                            required => required.Key.Equals(@var, StringComparison.InvariantCultureIgnoreCase)))
                    .ToList();

            var missingValues =
                RequiredValues.Where(
                    @var =>
                    {
                        var value = buildVariables.SingleOrDefault(required => required.Key.Equals(@var, StringComparison.InvariantCultureIgnoreCase));

                        return value != null && string.IsNullOrWhiteSpace(value.Value);
                    }).ToList();

            var sb = new StringBuilder();

            if (missingKeys.Any())
            {
                sb.AppendLine(string.Format("Missing variables: [{0}]", missingKeys.Count));
                foreach (var missingKey in missingKeys)
                {
                    sb.AppendLine(missingKey);
                }
            }

            if (missingValues.Any())
            {
                sb.AppendLine(string.Format("Variables with empty values: [{0}]", missingValues.Count));
                foreach (var missingValue in missingValues)
                {
                    sb.AppendLine(missingValue);
                }
            }

            bool succeeded = !missingKeys.Any() && !missingValues.Any();

            succeeded &= PostVariableVerification(sb, buildVariables);
        
            if (!succeeded)
            {
                logger.WriteError(sb.ToString());
            }

            return Task.FromResult(succeeded ? ExitCode.Success : ExitCode.Failure);
        }

        protected virtual bool PostVariableVerification(StringBuilder errorLogger, IReadOnlyCollection<IVariable> buildVariables)
        {
            return true;
        }
    }
}