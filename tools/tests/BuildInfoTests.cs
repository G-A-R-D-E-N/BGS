using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class BuildInfoTests
{
    [Fact]
    public void ReportCarriesVersionBuildAndRuntime()
    {
        Assert.Equal("1.1.0", BuildInfo.Version);
        Assert.False(string.IsNullOrWhiteSpace(BuildInfo.Build));
        Assert.False(string.IsNullOrWhiteSpace(BuildInfo.Runtime));
        Assert.Equal(
            $"Behaviour Graph Studio {BuildInfo.Version} · build {BuildInfo.Build} · {BuildInfo.Runtime}",
            BuildInfo.Report);
    }
}
