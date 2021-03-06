﻿using System;
using System.Collections.Generic;
using System.Linq;
using Arbor.X.Core.Exceptions;

namespace Arbor.X.Core.BuildVariables
{
    public static class RequireVariableExtensions
    {
        public static IVariable Require(this IReadOnlyCollection<IVariable> variables, string variableName)
        {
            if (variables == null)
            {
                throw new ArgumentNullException(nameof(variables));
            }

            if (string.IsNullOrWhiteSpace(variableName))
            {
                throw new ArgumentNullException(nameof(variableName));
            }

            List<IVariable> foundVariables = variables
                .Where(var => var.Key.Equals(variableName, StringComparison.InvariantCultureIgnoreCase))
                .ToList();

            if (foundVariables.Count() > 1)
            {
                throw new InvalidOperationException(
                    $"The are multiple variables with key '{variableName}'");
            }

            IVariable variable = foundVariables.SingleOrDefault();

            if (variable == null)
            {
                string message = $"The key '{variableName}' was not found in the variable collection";
                VariableDescription property = WellKnownVariables.AllVariables.SingleOrDefault(
                    item => item.InvariantName.Equals(variableName, StringComparison.InvariantCultureIgnoreCase));

                if (property != null)
                {
                    message +=
                        $". (The variable is a wellknown property {typeof(WellKnownVariables)}.{property.WellknownName})";
                }

                throw new BuildException(message, variables);
            }

            return variable;
        }
    }
}
