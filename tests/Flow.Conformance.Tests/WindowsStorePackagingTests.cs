using System.Xml.Linq;

namespace Flow.Conformance.Tests;

public sealed class WindowsStorePackagingTests
{
    [Fact]
    public void ManifestMatchesReservedStoreIdentityAndDesktopScope()
    {
        var root = FindRoot();
        var package = XDocument.Load(Path.Combine(root, "installer", "Flow.Store", "AppxManifest.xml")).Root!;
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        var identity = package.Element(ns + "Identity")!;
        Assert.Equal("ESSoftwares.FlowEngine", (string?)identity.Attribute("Name"));
        Assert.Equal("CN=21D9EB03-6223-4C3C-91C6-B132CCE87A14", (string?)identity.Attribute("Publisher"));
        Assert.Equal("x64", (string?)identity.Attribute("ProcessorArchitecture"));
        var version = Version.Parse(identity.Attribute("Version")!.Value);
        Assert.InRange(version.Major, 1, 65535);
        Assert.InRange(version.Minor, 0, 65535);
        Assert.InRange(version.Build, 0, 65535);
        Assert.Equal(0, version.Revision);
        Assert.Equal("ES Softwares", package.Element(ns + "Properties")!.Element(ns + "PublisherDisplayName")!.Value);
        Assert.Equal(new[] { "en-US", "pt-BR" }, package.Element(ns + "Resources")!.Elements().Select(e => (string)e.Attribute("Language")!));
        var family = Assert.Single(package.Element(ns + "Dependencies")!.Elements());
        Assert.Equal("Windows.Desktop", (string?)family.Attribute("Name"));
        var app = Assert.Single(package.Element(ns + "Applications")!.Elements());
        Assert.Equal("app\\Flow.Windows.exe", (string?)app.Attribute("Executable"));
        Assert.Equal("Windows.FullTrustApplication", (string?)app.Attribute("EntryPoint"));
        Assert.Equal("runFullTrust", (string?)Assert.Single(package.Element(ns + "Capabilities")!.Elements()).Attribute("Name"));
    }

    [Fact]
    public void StorePackagingDoesNotRebuildOrInstallAndKeepsUpdatesInStore()
    {
        var root = FindRoot();
        var script = File.ReadAllText(Path.Combine(root, "eng", "build-windows-store.ps1"));
        Assert.DoesNotContain("dotnet publish", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Add-AppxPackage", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Payload hash or length mismatch", script, StringComparison.Ordinal);
        Assert.Contains("sourceRevision -ne $head", script, StringComparison.Ordinal);
        Assert.Contains("developmentOnly", script, StringComparison.Ordinal);
        var about = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "Pages", "AboutPage.xaml.cs"));
        Assert.Contains("StoreDistribution.IsStorePackage || _updateCancellation", about, StringComparison.Ordinal);
        var preview = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "Pages", "PreviewPage.xaml.cs"));
        Assert.Contains("CreateWithOptionsAsync(null, userDataFolder, null)", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void StoreHidesAndGuardsExternalShellAndManifestDeclaresPerMonitorDpi()
    {
        var root = FindRoot();
        var operations = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "Pages", "OperationsPage.xaml.cs"));
        Assert.Contains("OpenPowerShellButton.Visibility = StoreDistribution.IsStorePackage ? Visibility.Collapsed", operations, StringComparison.Ordinal);
        var window = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "MainWindow.xaml.cs"));
        var handler = window[window.IndexOf("private static Task OpenPowerShellAsync()", StringComparison.Ordinal)..];
        Assert.True(handler.IndexOf("if (StoreDistribution.IsStorePackage)", StringComparison.Ordinal) <
            handler.IndexOf("System.Diagnostics.Process.Start", StringComparison.Ordinal));
        var manifest = XDocument.Load(Path.Combine(root, "src", "Flow.Windows", "app.manifest"));
        XNamespace dpi = "http://schemas.microsoft.com/SMI/2016/WindowsSettings";
        Assert.Equal("PerMonitorV2, PerMonitor", manifest.Descendants(dpi + "dpiAwareness").Single().Value);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Flow.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository not found.");
    }
}
