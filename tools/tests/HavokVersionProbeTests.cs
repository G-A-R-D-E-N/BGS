using System.Text;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class HavokVersionProbeTests
{
    [Fact]
    public void ReadsFo4VersionFromTheFirst96Bytes()
    {
        byte[] header = new byte[HavokVersionProbe.HeaderBytes];
        Encoding.ASCII.GetBytes("hk_2014.1.0-r1").CopyTo(header, 24);

        var result = HavokVersionProbe.Read(header);

        Assert.NotNull(result);
        Assert.Equal("hk_2014.1.0-r1", result!.Version);
        Assert.Equal(24, result.Offset);
    }

    [Fact]
    public void Reads2018VersionWithoutAssumingAContainerLayout()
    {
        byte[] header = new byte[HavokVersionProbe.HeaderBytes];
        Encoding.ASCII.GetBytes("hk_2018.2.0-r1").CopyTo(header, 41);

        var result = HavokVersionProbe.Read(header);

        Assert.NotNull(result);
        Assert.Equal("hk_2018.2.0-r1", result!.Version);
        Assert.Equal(41, result.Offset);
    }

    [Fact]
    public void StopsAtTheEndOfTheVersionToken()
    {
        var result = HavokVersionProbe.Read(Encoding.ASCII.GetBytes("xxxxhk_2015.1.0-r1\0other"));

        Assert.NotNull(result);
        Assert.Equal("hk_2015.1.0-r1", result!.Version);
    }

    [Fact]
    public void DoesNotScanPastTheHeaderWindow()
    {
        byte[] bytes = new byte[160];
        Encoding.ASCII.GetBytes("hk_2018.2.0-r1").CopyTo(bytes, 110);

        Assert.Null(HavokVersionProbe.Read(bytes));
    }

    [Fact]
    public void ReturnsNullWhenNoHavokVersionIsPresent()
    {
        Assert.Null(HavokVersionProbe.Read(Encoding.ASCII.GetBytes("not a havok header")));
    }
}
