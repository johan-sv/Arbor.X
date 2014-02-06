﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arbor.Aesculus.Core;
using Arbor.Castanea;
using Arbor.X.Core.BuildVariables;
using Arbor.X.Core.Logging;

namespace Arbor.X.Core.Tools.NuGet
{
    [Priority(100)]
    public class NuGetRestorer : ITool
    {
        public Task<ExitCode> ExecuteAsync(ILogger logger, IReadOnlyCollection<IVariable> buildVariables)
        {
            var app = new CastaneaApplication();

            var vcsRoot = VcsPathHelper.TryFindVcsRootPath();

            var files = Directory.GetFiles(vcsRoot, "repositories.config", SearchOption.AllDirectories);

            foreach (var repositoriesConfig in files)
            {
                try
                {
                    var result = app.RestoreAllSolutionPackages(new NuGetConfig
                                                                    {
                                                                        RepositoriesConfig = repositoriesConfig
                                                                    });

                    Console.WriteLine("Restored {0} package configurations defined in {1}", result, repositoriesConfig);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Cloud not restore packages defined in '{0}'. {1}", repositoriesConfig, ex);
                }
            }

            return Task.FromResult(ExitCode.Success);
        }
    }
}