using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Arbor.Defensive;

namespace Arbor.X.Core.BuildVariables
{
    public static class BuildVariableExtensions
    {
        public static bool HasKey(
            this IReadOnlyCollection<IVariable> buildVariables,
            string key)
        {
            return buildVariables.Any(
                bv => bv.Key.Equals(
                    key,
                    StringComparison.InvariantCultureIgnoreCase));
        }

        public static IVariable GetVariable(
            this IReadOnlyCollection<IVariable> buildVariables,
            string key)
        {
            return buildVariables.Single(
                bv => bv.Key.Equals(
                    key,
                    StringComparison.InvariantCultureIgnoreCase));
        }

        public static Maybe<IVariable> GetOptionalVariable(
            this IReadOnlyCollection<IVariable> buildVariables,
            string key)
        {
            IVariable variable = buildVariables.SingleOrDefault(
                bv => bv.Key.Equals(
                    key,
                    StringComparison.InvariantCultureIgnoreCase));

            if (variable is null)
            {
                return Maybe<IVariable>.Empty();
            }

            return new Maybe<IVariable>(variable);
        }

        public static string GetVariableValueOrDefault(
            this IReadOnlyCollection<IVariable> buildVariables,
            string key,
            string defaultValue)
        {
            if (!buildVariables.HasKey(key))
            {
                return defaultValue;
            }

            return buildVariables.GetVariable(key).Value;
        }

        public static bool GetBooleanByKey(
            this IReadOnlyCollection<IVariable> buildVariables,
            string key,
            bool defaultValue = false)
        {
            if (!buildVariables.HasKey(key))
            {
                return defaultValue;
            }

            string value = buildVariables.GetVariableValueOrDefault(
                key,
                defaultValue.ToString());

            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            bool parsed;

            if (!bool.TryParse(
                value,
                out parsed))
            {
                return defaultValue;
            }

            return parsed;
        }

        public static int GetInt32ByKey(
            this IReadOnlyCollection<IVariable> buildVariables,
            string key,
            int defaultValue = default,
            int? minValue = null)
        {
            int? returnValue = null;

            if (buildVariables.HasKey(key))
            {
                string value = buildVariables.GetVariableValueOrDefault(
                    key,
                    defaultValue.ToString(CultureInfo.InvariantCulture));

                if (!string.IsNullOrWhiteSpace(value))
                {
                    int parsed;
                    if (int.TryParse(value, out parsed))
                    {
                        returnValue = parsed;
                    }
                }
            }

            if (!returnValue.HasValue)
            {
                returnValue = defaultValue;
            }

            if (returnValue < minValue)
            {
                returnValue = minValue;
            }

            return returnValue.Value;
        }

        public static long GetInt64ByKey(
            this IReadOnlyCollection<IVariable> buildVariables,
            string key,
            long defaultValue = default)
        {
            if (!buildVariables.HasKey(key))
            {
                return defaultValue;
            }

            string value = buildVariables.GetVariableValueOrDefault(
                key,
                defaultValue.ToString(CultureInfo.InvariantCulture));

            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            long parsed;

            if (!long.TryParse(
                value,
                out parsed))
            {
                return defaultValue;
            }

            return parsed;
        }

        public static bool GetValueOrDefault(
            this IVariable variable,
            bool defaultValue = false)
        {
            if (variable == null)
            {
                return defaultValue;
            }

            if (string.IsNullOrWhiteSpace(variable.Value))
            {
                return defaultValue;
            }

            bool parsed;

            if (!bool.TryParse(
                variable.Value,
                out parsed))
            {
                return defaultValue;
            }

            return parsed;
        }

        public static int GetValueOrDefault(
            this IVariable variable,
            int defaultValue = default)
        {
            if (variable == null)
            {
                return defaultValue;
            }

            if (string.IsNullOrWhiteSpace(variable.Value))
            {
                return defaultValue;
            }

            int parsed;

            if (!int.TryParse(
                variable.Value,
                out parsed))
            {
                return defaultValue;
            }

            return parsed;
        }

        public static long GetValueOrDefault(
            this IVariable variable,
            long defaultValue = default)
        {
            if (variable == null)
            {
                return defaultValue;
            }

            if (string.IsNullOrWhiteSpace(variable.Value))
            {
                return defaultValue;
            }

            long parsed;

            if (!long.TryParse(
                variable.Value,
                out parsed))
            {
                return defaultValue;
            }

            return parsed;
        }
    }
}
