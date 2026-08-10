using System.Collections.Generic;
using BehaviourStudio.Tools;
using Xunit;

namespace BehaviourStudio.Tests;




public class SuiteTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (name, _) in Tools.Tests.Cases) yield return new object[] { name };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Check(string name) => Assert.Equal(0, Tools.Tests.RunOne(name));
}
