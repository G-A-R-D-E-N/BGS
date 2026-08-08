using System.Collections.Generic;
using BehaviourStudio.Tools;
using Xunit;

namespace BehaviourStudio.Tests;

// Wraps the console suite so `dotnet test` reports the same checks with one named result each.
// The checks themselves live in Tests.cs and are the same code the CI console runner walks, so the
// two cannot report different things.
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
