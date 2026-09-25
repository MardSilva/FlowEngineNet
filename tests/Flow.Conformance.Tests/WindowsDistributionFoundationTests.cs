using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace Flow.Conformance.Tests;

public sealed class WindowsDistributionFoundationTests
{
    private const string UpgradeCode = "{C412C622-FA2F-400C-88EE-BA5D4A573F7D}";
    private const string ProductCode = "{D4F3061D-355D-4DFC-8814-A99165AEEA42}";
    private static readonly IReadOnlyDictionary<string, string> ExpectedSourceHashes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["symbol-on-light"] = "57372E66178421105A9BBC49CF3C6C07E46C124C0C3492F6BD0BFDEF0B3EF8E2",
            ["symbol-on-dark"] = "69C2C8D6F3F0D11C7AE71DEFB461CFD3622F0047BC175AAF1BE27118D104FD6C",
            ["symbol-monochrome"] = "18FC81574203C41EB49FDC92B314EC7FDC266547EA2F080496F43FA710BF8002",
        };

    [Fact]
    public void BrandSources_MatchDeclaredHashes()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "assets", "branding", "brand-assets.json");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));

        Assert.Equal("flow-brand-assets-0.1", manifest.RootElement.GetProperty("format").GetString());
        Assert.DoesNotContain("C:\\Users\\", File.ReadAllText(manifestPath), StringComparison.OrdinalIgnoreCase);

        var sources = manifest.RootElement.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(3, sources.Length);
        foreach (var source in sources)
        {
            var id = source.GetProperty("id").GetString()!;
            var relativePath = source.GetProperty("path").GetString()!;
            Assert.False(Path.IsPathRooted(relativePath));
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Brand source is missing: {relativePath}");
            Assert.Equal(ExpectedSourceHashes[id], source.GetProperty("sha256").GetString());
            Assert.Equal(ExpectedSourceHashes[id], Sha256Upper(path));
            AssertPngDimensions(path, 512, 512);
        }
    }

    [Fact]
    public void GeneratedAssets_MatchManifestAndRequiredDimensions()
    {
        var root = FindRepositoryRoot();
        var output = Path.Combine(root, "assets", "branding", "windows");
        var manifestPath = Path.Combine(output, "manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));

        Assert.Equal("flow-windows-brand-assets-0.1", manifest.RootElement.GetProperty("format").GetString());
        Assert.Equal(4.5, manifest.RootElement.GetProperty("contrast").GetProperty("minimum").GetDouble());
        Assert.True(manifest.RootElement.GetProperty("contrast").GetProperty("lightOnNavy").GetDouble() >= 4.5);
        Assert.True(manifest.RootElement.GetProperty("contrast").GetProperty("lightBlueOnNavy").GetDouble() >= 4.5);

        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var relativePath = file.GetProperty("path").GetString()!;
            var path = Path.Combine(output, relativePath);
            Assert.True(File.Exists(path), $"Generated brand asset is missing: {relativePath}");
            Assert.Equal(file.GetProperty("sha256").GetString(), Sha256Lower(path));

            if (Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                AssertPngDimensions(
                    path,
                    file.GetProperty("width").GetInt32(),
                    file.GetProperty("height").GetInt32());
            }
        }

        AssertIcoSizes(Path.Combine(output, "flow.ico"), [16, 32, 48, 256]);
    }

    [Fact]
    public void WindowsProductProperties_DefineStablePerUserIdentity()
    {
        var root = FindRepositoryRoot();
        var sharedProperties = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        var windowsProperties = XDocument.Load(Path.Combine(root, "eng", "Flow.WindowsProduct.props"));
        var propertyElements = windowsProperties.Descendants("PropertyGroup").Elements().ToArray();
        var properties = propertyElements
            .Where(static element => element.Name.LocalName != "FlowPublicVersion")
            .ToDictionary(static element => element.Name.LocalName, static element => element.Value, StringComparer.Ordinal);

        Assert.Contains(
            sharedProperties.Descendants("Import"),
            static import => ((string?)import.Attribute("Project"))?.EndsWith("eng/Flow.WindowsProduct.props", StringComparison.Ordinal) == true);
        Assert.Equal("FlowEngineNet", properties["FlowProductId"]);
        Assert.Equal("Flow Engine .NET", properties["FlowProductName"]);
        Assert.Equal("Flow Engine contributors", properties["FlowProductPublisher"]);
        Assert.Equal("0.2.3", properties["FlowWindowsInstallerVersion"]);
        Assert.Equal(UpgradeCode, properties["FlowWindowsUpgradeCode"]);
        Assert.True(Guid.TryParse(properties["FlowWindowsUpgradeCode"], out _));
        Assert.Equal(ProductCode, properties["FlowWindowsProductCode"]);
        Assert.True(Guid.TryParse(properties["FlowWindowsProductCode"], out _));
        Assert.Equal("win-x64", properties["FlowWindowsRuntimeIdentifier"]);
        Assert.Equal("x64", properties["FlowWindowsArchitecture"]);
        Assert.Equal("perUser", properties["FlowWindowsInstallScope"]);
        Assert.Equal("LocalAppDataFolder", properties["FlowWindowsInstallRoot"]);
        Assert.Equal("Programs\\FlowEngineNet", properties["FlowWindowsInstallDirectory"]);

        var installerVersion = Version.Parse(properties["FlowWindowsInstallerVersion"]);
        Assert.Equal(3, installerVersion.Build);
        Assert.Contains(
            propertyElements,
            static element => element.Name.LocalName == "FlowPublicVersion" && element.Value == "$(VersionPrefix)-$(VersionSuffix)");

        var owned = windowsProperties.Descendants("FlowWindowsOwnedComponent")
            .Select(static element => (string?)element.Attribute("Include"))
            .ToArray();
        var preserved = windowsProperties.Descendants("FlowWindowsPreservedUserData")
            .Select(static element => (string?)element.Attribute("Include"))
            .ToArray();
        Assert.DoesNotContain(owned, item => item is null);
        Assert.DoesNotContain(preserved, item => item is null);
        Assert.Empty(owned.Intersect(preserved, StringComparer.Ordinal));
        Assert.Contains("ApplicationPayload", owned);
        Assert.Contains("FlowDocuments", preserved);
        Assert.Contains("Books", preserved);
        Assert.Contains("Preferences", preserved);
    }

    [Fact]
    public void WindowsPortableContract_IsSelfContainedSingleFileAndAuditable()
    {
        var root = FindRepositoryRoot();
        var windowsProperties = XDocument.Load(Path.Combine(root, "eng", "Flow.WindowsProduct.props"));
        var properties = windowsProperties.Descendants("PropertyGroup").Elements()
            .Where(static element => element.Name.LocalName is not "FlowPublicVersion")
            .ToDictionary(static element => element.Name.LocalName, static element => element.Value, StringComparer.Ordinal);
        var cliProject = XDocument.Load(Path.Combine(root, "src", "Flow.Cli", "Flow.Cli.csproj"));
        var buildScript = File.ReadAllText(Path.Combine(root, "eng", "build-windows-portable.ps1"));
        var testScript = File.ReadAllText(Path.Combine(root, "eng", "test-windows-portable.ps1"));

        Assert.Equal("flow-windows-portable-0.1", properties["FlowWindowsPortableFormat"]);
        Assert.Equal("true", properties["FlowWindowsPortableSingleFile"]);
        Assert.Equal("true", properties["FlowWindowsPortableSelfContained"]);
        Assert.Contains(
            cliProject.Descendants("ApplicationIcon"),
            static icon => icon.Value.EndsWith("assets\\branding\\windows\\flow.ico", StringComparison.Ordinal));

        Assert.Contains("--self-contained', 'true'", buildScript, StringComparison.Ordinal);
        Assert.Contains("-p:PublishSingleFile=true", buildScript, StringComparison.Ordinal);
        Assert.Contains("-p:IncludeAllContentForSelfExtract=false", buildScript, StringComparison.Ordinal);
        Assert.Contains("New-DeterministicZip", buildScript, StringComparison.Ordinal);
        Assert.Contains("CycloneDX", buildScript, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS", buildScript, StringComparison.Ordinal);
        Assert.Contains("LICENSE.txt", buildScript, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\Users\\", buildScript, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("DOTNET_ROOT", testScript, StringComparison.Ordinal);
        Assert.Contains("FLOWCLI_MENU_REQUIRES_INTERACTIVE", testScript, StringComparison.Ordinal);
        Assert.Contains("render-html", testScript, StringComparison.Ordinal);
        Assert.DoesNotContain("eym_s", testScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowsInstallerContract_IsPerUserUpgradeableAndSafelyRemovable()
    {
        var root = FindRepositoryRoot();
        var packagePath = Path.Combine(root, "installer", "Flow.WindowsInstaller", "Package.wxs");
        var projectPath = Path.Combine(root, "installer", "Flow.WindowsInstaller", "Flow.WindowsInstaller.wixproj");
        var package = XDocument.Load(packagePath);
        var project = XDocument.Load(projectPath);
        var windowsProperties = XDocument.Load(Path.Combine(root, "eng", "Flow.WindowsProduct.props"));
        var packageElement = package.Descendants().Single(static element => element.Name.LocalName == "Package");
        var source = File.ReadAllText(packagePath);
        var buildScript = File.ReadAllText(Path.Combine(root, "eng", "build-windows-installer.ps1"));
        var testScript = File.ReadAllText(Path.Combine(root, "eng", "test-windows-installer.ps1"));

        Assert.Equal("WixToolset.Sdk/4.0.6", (string?)project.Root?.Attribute("Sdk"));
        Assert.Contains(
            project.Descendants("PackageReference"),
            static reference => (string?)reference.Attribute("Include") == "WixToolset.UI.wixext");
        Assert.Equal("perUser", (string?)packageElement.Attribute("Scope"));
        Assert.Equal("500", (string?)packageElement.Attribute("InstallerVersion"));
        Assert.Equal("$(var.ProductCode)", (string?)packageElement.Attribute("ProductCode"));
        Assert.Contains("<MajorUpgrade", source, StringComparison.Ordinal);
        Assert.Contains("IgnoreLanguage=\"yes\"", source, StringComparison.Ordinal);
        Assert.Contains("DowngradeErrorMessage", source, StringComparison.Ordinal);
        Assert.Contains("LocalAppDataFolder", source, StringComparison.Ordinal);
        Assert.Contains("Name=\"PATH\"", source, StringComparison.Ordinal);
        Assert.Contains("System=\"no\"", source, StringComparison.Ordinal);
        Assert.Contains("Permanent=\"no\"", source, StringComparison.Ordinal);
        Assert.Contains("ARPPRODUCTICON", source, StringComparison.Ordinal);
        Assert.Contains("ARPINSTALLLOCATION", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ARPSYSTEMCOMPONENT", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Shortcut", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Extension", source, StringComparison.Ordinal);

        Assert.Contains("WixToolset.Sdk", buildScript, StringComparison.Ordinal);
        Assert.Contains(
            windowsProperties.Descendants("FlowWindowsInstallerToolLicense"),
            static license => license.Value == "MS-RL");
        Assert.Contains("flow-windows-installer-0.1", buildScript, StringComparison.Ordinal);
        Assert.Contains("FlowWindowsProductCode", buildScript, StringComparison.Ordinal);
        Assert.Contains("Major upgrade", testScript, StringComparison.Ordinal);
        Assert.Contains("downgrade-refused", testScript, StringComparison.Ordinal);
        Assert.Contains("path-preserved", testScript, StringComparison.Ordinal);
        Assert.DoesNotContain("eym_s", buildScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eym_s", testScript, StringComparison.OrdinalIgnoreCase);

        foreach (var culture in new[] { "en-US", "pt-BR" })
        {
            var localization = XDocument.Load(Path.Combine(root, "installer", "Flow.WindowsInstaller", $"Installer.{culture}.wxl"));
            Assert.Equal(culture, (string?)localization.Root?.Attribute("Culture"));
            Assert.Contains(localization.Descendants(), static element =>
                element.Name.LocalName == "String" && (string?)element.Attribute("Id") == "DowngradeError");
        }
    }

    [Fact]
    public void GeneratedAssets_AreReproducibleOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = FindRepositoryRoot();
        var expectedDirectory = Path.Combine(root, "assets", "branding", "windows");
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"flow-brand-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(Path.Combine(root, "eng", "New-FlowWindowsBrandAssets.ps1"));
            startInfo.ArgumentList.Add("-OutputDirectory");
            startInfo.ArgumentList.Add(temporaryDirectory);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, $"Asset generation failed. stdout: {standardOutput} stderr: {standardError}");

            foreach (var expectedPath in Directory.EnumerateFiles(expectedDirectory).Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(expectedPath);
                var actualPath = Path.Combine(temporaryDirectory, name);
                Assert.True(File.Exists(actualPath), $"Reproduced asset is missing: {name}");
                Assert.Equal(File.ReadAllBytes(expectedPath), File.ReadAllBytes(actualPath));
            }
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static void AssertPngDimensions(string path, int expectedWidth, int expectedHeight)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= 24, $"PNG is too short: {path}");
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
        Assert.Equal(expectedWidth, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(expectedHeight, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
    }

    private static void AssertIcoSizes(string path, int[] expectedSizes)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= 6 + (16 * expectedSizes.Length));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2)));
        Assert.Equal(expectedSizes.Length, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2)));

        var actualSizes = new int[expectedSizes.Length];
        for (var index = 0; index < actualSizes.Length; index++)
        {
            var width = bytes[6 + (index * 16)];
            var height = bytes[7 + (index * 16)];
            actualSizes[index] = width == 0 ? 256 : width;
            Assert.Equal(actualSizes[index], height == 0 ? 256 : height);
        }

        Assert.Equal(expectedSizes, actualSizes);
    }

    private static string Sha256Upper(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string Sha256Lower(string path) =>
        Sha256Upper(path).ToLowerInvariant();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Flow.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the Flow repository root from the test output directory.");
    }
}
