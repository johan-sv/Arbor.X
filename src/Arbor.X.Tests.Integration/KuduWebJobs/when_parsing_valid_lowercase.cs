using Arbor.X.Core.Tools.Kudu;
using Machine.Specifications;

namespace Arbor.X.Tests.Integration.KuduWebJobs
{
    [Subject(typeof(KuduWebJobType))]
    public class when_parsing_valid_lowercase
    {
        static KuduWebJobType parsed;

        Because of = () => { parsed = KuduWebJobType.Parse("continuous"); };

        It should_return_a_valid_type = () => parsed.ShouldEqual(KuduWebJobType.Continuous);
    }
}
