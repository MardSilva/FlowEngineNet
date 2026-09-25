using System.Xml.Linq;
using Flow.Windows.Shell;

namespace Flow.Windows.Tests;

public sealed class FlowWindowsProjectConformanceTests
{
    [Fact]
    public void WinUiHostIsGraphicalUnpackagedAndDoesNotReferenceCli()
    {
        var root = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(root, "src", "Flow.Windows", "Flow.Windows.csproj"));
        var source = project.ToString();

        Assert.Equal("WinExe", project.Descendants("OutputType").Single().Value);
        Assert.Equal("true", project.Descendants("UseWinUI").Single().Value);
        Assert.Equal("None", project.Descendants("WindowsPackageType").Single().Value);
        Assert.Equal("true", project.Descendants("WindowsAppSDKSelfContained").Single().Value);
        Assert.Contains(project.Descendants("PackageReference"), element =>
            string.Equals((string?)element.Attribute("Include"), "Microsoft.WindowsAppSDK.WinUI", StringComparison.Ordinal));
        Assert.DoesNotContain(project.Descendants("PackageReference"), element =>
            string.Equals((string?)element.Attribute("Include"), "Microsoft.WindowsAppSDK", StringComparison.Ordinal));
        Assert.Contains("Flow.Windows.Shell", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Flow.Cli", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Flow.Application", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellDeclaresResponsiveNavigationThemesAndAccessibleStructure()
    {
        var root = FindRepositoryRoot();
        var window = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "MainWindow.xaml"));
        var app = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "App.xaml"));
        var home = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "Pages", "HomePage.xaml"));
        var explanation = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "Pages", "HowItWorksPage.xaml"));
        var operations = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "Pages", "OperationsPage.xaml"));
        var operationsCode = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "Pages", "OperationsPage.xaml.cs"));
        var library = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "Pages", "LibraryPage.xaml"));
        var preview = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "Pages", "PreviewPage.xaml"));
        var previewCode = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "Pages", "PreviewPage.xaml.cs"));

        Assert.Contains("<NavigationView", window, StringComparison.Ordinal);
        Assert.Contains("PaneDisplayMode=\"Auto\"", window, StringComparison.Ordinal);
        Assert.Contains("IsTabStop=\"True\"", window, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Light\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Dark\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HighContrast\"", app, StringComparison.Ordinal);
        Assert.Contains("XamlControlsResources", app, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HeadingLevel=\"Level1\"", home, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HeadingLevel=\"Level1\"", explanation, StringComparison.Ordinal);
        Assert.Contains("AllowDrop=\"True\"", operations, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HeadingLevel=\"Level1\"", operations, StringComparison.Ordinal);
        Assert.Contains("CancelButton_Click", operationsCode, StringComparison.Ordinal);
        Assert.Contains("ContentDialog", operationsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", operationsCode, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HeadingLevel=\"Level1\"", library, StringComparison.Ordinal);
        Assert.Contains("<WebView2", preview, StringComparison.Ordinal);
        Assert.Contains("SetVirtualHostNameToFolderMapping", previewCode, StringComparison.Ordinal);
        Assert.Contains("WebResourceRequested", previewCode, StringComparison.Ordinal);
        Assert.Contains("DownloadStarting", previewCode, StringComparison.Ordinal);
        Assert.Contains("PermissionRequested", previewCode, StringComparison.Ordinal);
        Assert.Contains("args.Cancel = true", previewCode, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", previewCode, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OperationLayerDoesNotReferenceCliWinUiOrNetworkClients()
    {
        var references = typeof(FlowWindowsOperationService).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name)
            .ToArray();

        Assert.DoesNotContain("Flow.Cli", references);
        Assert.DoesNotContain("Microsoft.WinUI", references);
        Assert.DoesNotContain("System.Net.Http", references);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Flow.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new DirectoryNotFoundException("Could not locate the Flow repository root.");
    }
}
