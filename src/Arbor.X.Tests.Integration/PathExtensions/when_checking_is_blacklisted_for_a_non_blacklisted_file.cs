using Arbor.X.Core.IO;

using Machine.Specifications;
using Serilog.Core;

namespace Arbor.X.Tests.Integration.PathExtensions
{
    [Tags(Core.Tools.Testing.MSpecInternalConstants.RecursiveArborXTest)]
    public class when_checking_is_blacklisted_for_a_non_blacklisted_file
    {
        static readonly PathLookupSpecification path_lookup_specification =
            DefaultPaths.DefaultPathLookupSpecification.WithIgnoredFileNameParts(new[] { string.Empty });

        static bool result;

        Establish context = () => { };

        Because of = () =>
        {
            result = path_lookup_specification.IsFileBlackListed(@"C:\anyrandomfile.txt",
                allowNonExistingFiles: true,
                logger: Logger.None).Item1;
        };

        It should_be_true = () => result.ShouldBeFalse();
    }
}
