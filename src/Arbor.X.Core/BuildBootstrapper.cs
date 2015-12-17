using System.Linq;
using System.Threading.Tasks;

using Arbor.X.Core.Assemblies;
using Arbor.X.Core.GenericExtensions;

using Autofac;

namespace Arbor.X.Core
{
    public class BuildBootstrapper
    {
        public static Task<IContainer> StartAsync()
        {
            ContainerBuilder builder = new ContainerBuilder();

            var assemblies = AssemblyFetcher.GetAssemblies().ToArray();

            builder.RegisterAssemblyModules(assemblies);

            IContainer container = builder.Build();

            return container.AsCompletedTask();
        }
    }
}
