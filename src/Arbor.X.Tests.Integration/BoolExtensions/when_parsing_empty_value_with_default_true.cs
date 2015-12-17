using Arbor.X.Core;
using Arbor.X.Core.Extensions;

using Machine.Specifications;

namespace Arbor.X.Tests.Integration.BoolExtensions
{
    [Subject(typeof(Core.Extensions.BoolExtensions))]
    public class when_parsing_empty_value_with_default_true
    {
        static bool result;
        Because of = () => { result = "".TryParseBool(defaultValue: true); };

        It should_be_true = () => result.ShouldBeTrue();
    }
}