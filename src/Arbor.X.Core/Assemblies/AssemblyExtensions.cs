using System; using Serilog;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Arbor.X.Core.BuildVariables;

using JetBrains.Annotations;
using Mono.Cecil;
using Serilog.Core;

namespace Arbor.X.Core.Assemblies
{
    public static class AssemblyExtensions
    {
        public static bool? IsDebugAssembly(
            [NotNull] this AssemblyDefinition assemblyDefinition,
            [NotNull] FileInfo fileInfo,
            ILogger logger = null)
        {
            if (assemblyDefinition == null)
            {
                throw new ArgumentNullException(nameof(assemblyDefinition));
            }

            if (fileInfo == null)
            {
                throw new ArgumentNullException(nameof(fileInfo));
            }

            ILogger usedLogger = logger ?? Logger.None;

            Assembly loadedAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .SingleOrDefault(assembly => !assembly.IsDynamic
                                             && assembly.FullName.Equals(assemblyDefinition.FullName,
                                                 StringComparison.OrdinalIgnoreCase));

            if (loadedAssembly != null)
            {
                usedLogger.Debug("Assembly '{FullName}' is already loaded in the app domain", assemblyDefinition.FullName);

                return IsDebugAssembly(loadedAssembly, usedLogger);
            }

            Assembly loadedReflectionOnlyAssembly = AppDomain.CurrentDomain.ReflectionOnlyGetAssemblies()
                .SingleOrDefault(assembly => !assembly.IsDynamic
                                             && assembly.FullName.Equals(assemblyDefinition.FullName,
                                                 StringComparison.OrdinalIgnoreCase));

            if (loadedReflectionOnlyAssembly != null)
            {
                usedLogger.Debug("Assembly '{FullName}' is already loaded in the app domain with reflection only", assemblyDefinition.FullName);

                return IsDebugAssembly(loadedReflectionOnlyAssembly, usedLogger);
            }

            if (!bool.TryParse(Environment.GetEnvironmentVariable(WellKnownVariables.AssemblyUseReflectionOnlyMode),
                    out bool enabled) || enabled)
            {
                try
                {
                    byte[] assemblyBytes;

                    using (var fs = new FileStream(fileInfo.FullName, FileMode.Open))
                    {
                        var buffer = new byte[16 * 1024];
                        using (var ms = new MemoryStream())
                        {
                            int read;
                            while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                ms.Write(buffer, 0, read);
                            }
                            assemblyBytes = ms.ToArray();
                        }
                    }

                    Assembly reflectedAssembly = Assembly.ReflectionOnlyLoad(assemblyBytes);

                    if (reflectedAssembly != null)
                    {
                        bool? isDebugAssembly = IsDebugAssembly(reflectedAssembly, usedLogger);

                        usedLogger.Verbose("Assembly is debug from reflected assembly: {IsDebug}", isDebugAssembly?.ToString(CultureInfo.InvariantCulture) ?? "N/A");

                        return isDebugAssembly;
                    }

                    usedLogger.Verbose("Reflected assembly from assembly definition {FullName} was null", assemblyDefinition.FullName);
                }
                catch (Exception ex)
                {
                    usedLogger.Error(ex, "Error while getting reflected assembly definition from assembly definition {FullName}", assemblyDefinition.FullName);
                    return null;
                }
            }

            try
            {
                Type type = typeof(DebuggableAttribute);

                CustomAttribute customAttribute =
                    assemblyDefinition.CustomAttributes.SingleOrDefault(s => s.AttributeType.FullName == type.FullName);

                if (customAttribute != null)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                usedLogger.Error(ex, "Error while getting is debug from assembly definition {FullName}", assemblyDefinition.FullName);
                return null;
            }

            return false;
        }

        public static bool? IsDebugAssembly([NotNull] this Assembly assembly, [NotNull] ILogger usedLogger)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            if (usedLogger == null)
            {
                throw new ArgumentNullException(nameof(usedLogger));
            }

            Type debuggableAttributeType = typeof(DebuggableAttribute);

            if (assembly.ReflectionOnly)
            {
                try
                {
                    IList<CustomAttributeData> customAttributeDatas = CustomAttributeData.GetCustomAttributes(assembly);

                    CustomAttributeData customAttributeData =
                        customAttributeDatas.SingleOrDefault(cat => cat.AttributeType == debuggableAttributeType);

                    if (customAttributeData != null)
                    {
                        foreach (CustomAttributeTypedArgument cata in customAttributeData.ConstructorArguments)
                        {
                            if (cata.Value.GetType() != typeof(ReadOnlyCollection<CustomAttributeTypedArgument>))
                            {
                                bool isDebugAssembly =
                                    (uint)(((DebuggableAttribute.DebuggingModes)cata.Value) &
                                           DebuggableAttribute.DebuggingModes.Default) > 0U;

                                return isDebugAssembly;
                            }
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    usedLogger.Error(ex, "Error while getting is debug from reflected assembly {FullName}", assembly.FullName);

                    return null;
                }
            }

            bool? isDebugBuild = null;

            try
            {
                object[] attribs = assembly.GetCustomAttributes(debuggableAttributeType,
                    false);

                if (attribs.Length > 0)
                {
                    if (attribs[0] is DebuggableAttribute debuggableAttribute)
                    {
                        isDebugBuild = debuggableAttribute.IsJITOptimizerDisabled;
                    }
                }
                else
                {
                    isDebugBuild = false;
                }
            }
            catch (Exception ex)
            {
                usedLogger.Error(ex, "Error while is debug from assembly {FullName}", assembly.FullName);
            }

            return isDebugBuild;
        }
    }
}
