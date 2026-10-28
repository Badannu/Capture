using CaptureDemo.Core;
using Xunit;

namespace CaptureDemo.Core.Tests;

public class AppInfoTests
{
    [Fact]
    public void VersionIsPlainSemanticAndHasNoBuildMetadata()
    {
        // The settings footer shows this raw, so "+<commit>" or a four-part assembly
        // version would leak into the UI. Both are stripped by AppInfo.Read.
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppInfo.Version);
    }

    [Fact]
    public void LabelJoinsProductAndVersion()
    {
        Assert.Equal(AppInfo.Product + " v" + AppInfo.Version, AppInfo.Label);
    }

    [Fact]
    public void VersionComesFromTheSharedBuildProperty()
    {
        // Directory.Build.props is the single place a release bumps; if this fails the
        // property was overridden somewhere and the UI would disagree with the binary.
        Assert.NotEqual("0.0.0", AppInfo.Version);
    }
}
