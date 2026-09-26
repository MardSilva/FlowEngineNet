using System.Text.Json;
using Flow.Windows.Shell;

namespace Flow.Windows.Tests;

public sealed class FlowWindowsProductIdentityTests
{
    [Fact]
    public void DevelopmentNeverInfersARevisionFromAssemblyMetadata()
    {
        var identity = FlowWindowsProductIdentity.Resolve("x64", null);
        Assert.Equal(FlowWindowsIdentityState.Development, identity.State);
        Assert.Null(identity.Revision);
        Assert.DoesNotContain('+', identity.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchingDistributionRetainsPublicFactsOnly(bool dirty)
    {
        var identity = FlowWindowsProductIdentity.Resolve("x64", Manifest(dirty: dirty));
        Assert.Equal(FlowWindowsIdentityState.Distribution, identity.State);
        Assert.Equal(new string('a', 40), identity.Revision);
        Assert.Equal(dirty, identity.ModifiedSource);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(identity), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"format\":3}")]
    public void MalformedManifestDoesNotBreakThePage(string manifest)
    {
        Assert.Equal(FlowWindowsIdentityState.InvalidManifest,
            FlowWindowsProductIdentity.Resolve("x64", manifest).State);
    }

    [Fact]
    public void MismatchedOrPrivateMetadataIsNotDisplayed()
    {
        foreach (var manifest in new[]
        {
            Manifest(version: "99.0.0"), Manifest(architecture: "arm64"),
            Manifest(revision: "C:/private/build"), Manifest(revision: new string('z', 40)),
            new string(' ', 16385),
        })
        {
            var identity = FlowWindowsProductIdentity.Resolve("x64", manifest);
            Assert.Equal(FlowWindowsIdentityState.InvalidManifest, identity.State);
            Assert.Null(identity.Revision);
            Assert.Equal(FlowWindowsProductIdentity.PublicVersion, identity.Version);
        }
    }

    private static string Manifest(string? version = null, string architecture = "x64", string? revision = null, bool dirty = false) =>
        JsonSerializer.Serialize(new
        {
            format = "flow-windows-combined-payload-0.1",
            product = "Flow Engine .NET",
            version = version ?? FlowWindowsProductIdentity.PublicVersion,
            architecture,
            sourceRevision = revision ?? new string('a', 40),
            sourceTreeDirty = dirty,
            ignoredPrivatePath = "C:/private/build",
        });
}
