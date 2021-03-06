﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arbor.X.Core.BuildVariables;
using Arbor.X.Core.Logging;
using Arbor.X.Core.Tools.Cleanup;
using JetBrains.Annotations;
using Microsoft.Win32;

namespace Arbor.X.Core.Tools.VisualStudio
{
    [UsedImplicitly]
    public class VisualStudioVariableProvider : IVariableProvider
    {
        private bool _allowPreReleaseVersions;

        public int Order => VariableProviderOrder.Ignored;

        public Task<IEnumerable<IVariable>> GetEnvironmentVariablesAsync(
            ILogger logger,
            IReadOnlyCollection<IVariable> buildVariables,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(buildVariables.GetVariableValueOrDefault(
                WellKnownVariables.ExternalTools_VisualStudio_Version,
                string.Empty)))
            {
                return Task.FromResult(new List<IVariable>().AsEnumerable());
            }

            _allowPreReleaseVersions =
                buildVariables.GetBooleanByKey(WellKnownVariables.ExternalTools_VisualStudio_Version_Allow_PreRelease);

            int currentProcessBits = Environment.Is64BitProcess ? 64 : 32;
            const int registryLookupBits = 32;
            logger.WriteVerbose(
                $"Running current process [id {Process.GetCurrentProcess().Id}] as a {currentProcessBits}-bit process");

            const string registryKeyName = @"SOFTWARE\Microsoft\VisualStudio";

            logger.WriteVerbose(
                $"Looking for Visual Studio versions in {registryLookupBits}-bit registry key 'HKEY_LOCAL_MACHINE\\{registryKeyName}'");

            string visualStudioVersion = GetVisualStudioVersion(logger, registryKeyName);

            string vsTestExePath = null;

            if (!string.IsNullOrWhiteSpace(visualStudioVersion))
            {
                logger.WriteVerbose($"Found Visual Studio version {visualStudioVersion}");

                vsTestExePath = GetVSTestExePath(logger, registryKeyName, visualStudioVersion);
            }
            else
            {
                logger.WriteWarning("Could not find any Visual Studio version");
            }

            var environmentVariables = new[]
            {
                new EnvironmentVariable(
                    WellKnownVariables.ExternalTools_VisualStudio_Version,
                    visualStudioVersion),
                new EnvironmentVariable(WellKnownVariables.ExternalTools_VSTest_ExePath, vsTestExePath)
            };

            return Task.FromResult<IEnumerable<IVariable>>(environmentVariables);
        }

        private static string GetVSTestExePath(ILogger logger, string registryKeyName, string visualStudioVersion)
        {
            string path = null;

            using (RegistryKey view32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
            {
                using (RegistryKey vsKey = view32.OpenSubKey(registryKeyName))
                {
                    if (vsKey != null)
                    {
                        using (RegistryKey versionKey = vsKey.OpenSubKey(visualStudioVersion))
                        {
                            if (versionKey == null)
                            {
                                throw new InvalidOperationException(
                                    $"Expected key {vsKey.Name} to contain a subkey with name {visualStudioVersion}");
                            }

                            const string installdir = "InstallDir";
                            object installDir = versionKey.GetValue(installdir, null);

                            if (string.IsNullOrWhiteSpace(installDir?.ToString()))
                            {
                                logger.WriteWarning(
                                    $"Expected key {versionKey.Name} to contain a value with name {installdir} and a non-empty value");
                                return null;
                            }

                            string exePath = Path.Combine(
                                installDir.ToString(),
                                "CommonExtensions",
                                "Microsoft",
                                "TestWindow",
                                "vstest.console.exe");

                            if (!File.Exists(exePath))
                            {
                                throw new InvalidOperationException($"The file '{exePath}' does not exist");
                            }

                            path = exePath;
                        }
                    }
                }
            }

            return path;
        }

        private string GetVisualStudioVersion(ILogger logger, string registryKeyName)
        {
            string visualStudioVersion = null;

            using (RegistryKey view32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
            {
                using (RegistryKey vsKey = view32.OpenSubKey(registryKeyName))
                {
                    if (vsKey != null)
                    {
                        List<Version> versions = vsKey.GetSubKeyNames()
                            .Where(subKeyName => char.IsDigit(subKeyName.First()))
                            .Select(
                                keyName =>
                                {
                                    Version version;
                                    if (!Version.TryParse(keyName, out version))
                                    {
                                        if (_allowPreReleaseVersions)
                                        {
                                            string preReleaseSeparator = "_";

                                            int indexOf = keyName.IndexOf(
                                                preReleaseSeparator,
                                                StringComparison.OrdinalIgnoreCase);

                                            if (indexOf >= 0)
                                            {
                                                string versionOnly = keyName.Substring(0, indexOf);

                                                if (Version.TryParse(versionOnly, out version))
                                                {
                                                    logger.WriteDebug(
                                                        $"Found pre-release Visual Studio version {version}");
                                                }
                                            }
                                        }
                                    }

                                    if (version == null)
                                    {
                                        logger.WriteDebug(
                                            $"Could not parse Visual Studio version from registry key name '{keyName}', skipping that version.");
                                    }

                                    return version;
                                })
                            .Where(version => version != null)
                            .OrderByDescending(name => name)
                            .ToList();

                        logger.WriteVerbose(
                            $"Found {versions.Count} Visual Studio versions: {string.Join(", ", versions.Select(version => version.ToString(2)))}");

                        if (versions.Any(version => version == new Version(15, 0)))
                        {
                            visualStudioVersion = "15.0";
                        }

                        if (versions.Any(version => version == new Version(14, 0)))
                        {
                            visualStudioVersion = "14.0";
                        }
                        else if (versions.Any(version => version == new Version(12, 0)))
                        {
                            visualStudioVersion = "12.0";
                        }
                        else if (versions.Any(version => version == new Version(11, 0)))
                        {
                            visualStudioVersion = "11.0";
                        }
                        else if (versions.Any())
                        {
                            visualStudioVersion = versions.First().ToString(2);
                        }
                    }
                }
            }

            return visualStudioVersion;
        }
    }
}
