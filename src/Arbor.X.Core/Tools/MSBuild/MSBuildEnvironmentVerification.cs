﻿using Arbor.X.Core.BuildVariables;

namespace Arbor.X.Core.Tools.MSBuild
{
    [Priority(51)]
    public class MSBuildEnvironmentVerification : EnvironmentVerification
    {
        public MSBuildEnvironmentVerification()
        {
            RequiredValues.Add(WellKnownVariables.ExternalTools_MSBuild_ExePath);
        }
    }
}