using System.Linq;
using System.Reflection;
using Arbor.X.Core.Assemblies;
using Arbor.X.Core.BuildVariables;
using Arbor.X.Core.GenericExtensions;
using Arbor.X.Core.Tools.Environments;
using Autofac;
using JetBrains.Annotations;
using Module = Autofac.Module;

namespace Arbor.X.Core.Configuration.AutofacModules
{
    [UsedImplicitly]
    public class VariableProviderModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            Assembly[] assemblies = AssemblyFetcher.GetAssemblies().ToArray();

            builder.RegisterType<SourcePathVariableProvider>().AsImplementedInterfaces();

            builder.RegisterAssemblyTypes(assemblies)
                .Where(type => type.IsConcretePublicClassImplementing<IVariableProvider>())
                .AsImplementedInterfaces();
        }
    }
}
