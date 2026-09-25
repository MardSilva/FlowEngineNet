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
        var homeCode = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "Pages", "HomePage.xaml.cs"));
        var explanation = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "Pages", "HowItWorksPage.xaml"));
        var operations = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "Pages", "OperationsPage.xaml"));
        var operationsCode = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "Pages", "OperationsPage.xaml.cs"));
        var library = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "Pages", "LibraryPage.xaml"));
        var preview = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "Pages", "PreviewPage.xaml"));
        var previewCode = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "Pages", "PreviewPage.xaml.cs"));
        var sizingCode = File.ReadAllText(Path.Combine(root, "src", "Flow.Windows", "NativeWindowSizing.cs"));

        Assert.Contains("<NavigationView", window, StringComparison.Ordinal);
        Assert.Contains("PaneDisplayMode=\"Auto\"", window, StringComparison.Ordinal);
        Assert.Contains("IsTabStop=\"True\"", window, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Light\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Dark\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HighContrast\"", app, StringComparison.Ordinal);
        Assert.Contains("XamlControlsResources", app, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HeadingLevel=\"Level1\"", home, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PageScrollViewer\"", home, StringComparison.Ordinal);
        Assert.Contains("HorizontalContentAlignment=\"Center\"", home, StringComparison.Ordinal);
        Assert.Contains("UpdateContentWidth", homeCode, StringComparison.Ordinal);
        Assert.Contains("PageContent.Width = Math.Min", homeCode, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HeadingLevel=\"Level1\"", explanation, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SectionList\"", explanation, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CompactSectionPicker\"", explanation, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SectionScrollViewer\"", explanation, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SectionContent\"", explanation, StringComparison.Ordinal);
        Assert.Contains("<Expander", explanation, StringComparison.Ordinal);
        var explanationCode = File.ReadAllText(
            Path.Combine(root, "src", "Flow.Windows", "Pages", "HowItWorksPage.xaml.cs"));
        Assert.Contains("SelectSection", explanationCode, StringComparison.Ordinal);
        Assert.Contains("UpdateSectionWidth", explanationCode, StringComparison.Ordinal);
        Assert.Contains("SectionContent.Width = Math.Min", explanationCode, StringComparison.Ordinal);
        Assert.Contains("_synchronizingSelection", explanationCode, StringComparison.Ordinal);
        Assert.Contains("AllowDrop=\"True\"", operations, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HeadingLevel=\"Level1\"", operations, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SourcePickerLayout\"", operations, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PageScrollViewer\"", operations, StringComparison.Ordinal);
        Assert.Contains("HorizontalContentAlignment=\"Center\"", operations, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AdvancedActionsGrid\"", operations, StringComparison.Ordinal);
        Assert.Contains("CancelButton_Click", operationsCode, StringComparison.Ordinal);
        Assert.Contains("ContentDialog", operationsCode, StringComparison.Ordinal);
        Assert.Contains("if (_initialized)", operationsCode, StringComparison.Ordinal);
        Assert.Contains("AdvancedPanel is null", operationsCode, StringComparison.Ordinal);
        Assert.Contains("UpdateImportOptionsVisibility", operationsCode, StringComparison.Ordinal);
        Assert.Contains("PageScrollViewer.ActualWidth", operationsCode, StringComparison.Ordinal);
        Assert.Contains("PageContent.Width = Math.Min", operationsCode, StringComparison.Ordinal);
        Assert.Contains("viewportWidth < 720", operationsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", operationsCode, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HeadingLevel=\"Level1\"", library, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BooksAndDetailsGrid\"", library, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DetailsContentGrid\"", library, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DetailsActionsGrid\"", library, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BackToBooksButton\"", library, StringComparison.Ordinal);
        var libraryCode = File.ReadAllText(
            Path.Combine(root, "src", "Flow.Windows", "Pages", "LibraryPage.xaml.cs"));
        Assert.Contains("UpdateResponsiveLayout", libraryCode, StringComparison.Ordinal);
        Assert.Contains("showingCompactDetails", libraryCode, StringComparison.Ordinal);
        Assert.Contains("BookList.Visibility", libraryCode, StringComparison.Ordinal);
        Assert.Contains("<WebView2", preview, StringComparison.Ordinal);
        Assert.Contains("SetVirtualHostNameToFolderMapping", previewCode, StringComparison.Ordinal);
        Assert.Contains("SelectInitialProfile(ActualWidth)", previewCode, StringComparison.Ordinal);
        Assert.Contains("WebResourceRequested", previewCode, StringComparison.Ordinal);
        Assert.Contains("DownloadStarting", previewCode, StringComparison.Ordinal);
        Assert.Contains("PermissionRequested", previewCode, StringComparison.Ordinal);
        Assert.Contains("args.Cancel = true", previewCode, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", previewCode, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WmGetMinMaxInfo", sizingCode, StringComparison.Ordinal);
        Assert.Contains("GetDpiForWindow", sizingCode, StringComparison.Ordinal);
        Assert.Contains("FlowWindowsWindowSizePolicy.GetMinimumSize", sizingCode, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryScreenUsesTheSharedThemeSurfaces()
    {
        var root = FindRepositoryRoot();
        var windowsRoot = Path.Combine(root, "src", "Flow.Windows");
        var app = File.ReadAllText(Path.Combine(windowsRoot, "App.xaml"));
        var window = File.ReadAllText(Path.Combine(windowsRoot, "MainWindow.xaml"));
        var windowCode = File.ReadAllText(Path.Combine(windowsRoot, "MainWindow.xaml.cs"));

        var surfaceKeys = new[]
        {
            "FlowWindowBackgroundBrush",
            "FlowNavigationBackgroundBrush",
            "FlowContentBackgroundBrush",
            "FlowCardBackgroundBrush",
            "FlowPreviewBackgroundBrush",
        };

        foreach (var key in surfaceKeys)
        {
            Assert.Contains($"x:Key=\"{key}\"", app, StringComparison.Ordinal);
        }

        Assert.Contains("Background=\"{ThemeResource FlowWindowBackgroundBrush}\"", window, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"NavigationViewDefaultPaneBackground\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"NavigationViewExpandedPaneBackground\"", app, StringComparison.Ordinal);
        Assert.Contains("Background=\"{ThemeResource FlowContentBackgroundBrush}\"", window, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Forms.OpenFileDialog", windowCode, StringComparison.Ordinal);
        Assert.Contains("*.epub", windowCode, StringComparison.Ordinal);
        Assert.DoesNotContain("*.json", windowCode, StringComparison.Ordinal);

        var pagePaths = Directory.GetFiles(Path.Combine(windowsRoot, "Pages"), "*Page.xaml");
        Assert.NotEmpty(pagePaths);
        foreach (var pagePath in pagePaths)
        {
            var page = File.ReadAllText(pagePath);
            Assert.Contains(
                "Background=\"{ThemeResource FlowContentBackgroundBrush}\"",
                page,
                StringComparison.Ordinal);
            Assert.Contains("x:Name=\"PageContent\"", page, StringComparison.Ordinal);
            Assert.Contains("<AdaptiveTrigger MinWindowWidth=\"0\"", page, StringComparison.Ordinal);
            Assert.Contains("<AdaptiveTrigger MinWindowWidth=\"960\"", page, StringComparison.Ordinal);
        }

        var preview = File.ReadAllText(Path.Combine(windowsRoot, "Pages", "PreviewPage.xaml"));
        Assert.Contains("Background=\"{ThemeResource FlowPreviewBackgroundBrush}\"", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"White\"", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsCiRestoresTheWinUiProjectBeforeBuildingWithoutRestore()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        var restore = "dotnet restore src/Flow.Windows/Flow.Windows.csproj -p:Platform=x64";
        var build = "dotnet build src/Flow.Windows/Flow.Windows.csproj --no-restore -p:Platform=x64";

        var restoreIndex = workflow.IndexOf(restore, StringComparison.Ordinal);
        var buildIndex = workflow.IndexOf(build, StringComparison.Ordinal);

        Assert.True(restoreIndex >= 0, "The workflow must restore the WinUI project explicitly.");
        Assert.True(buildIndex > restoreIndex, "The WinUI restore must run before its no-restore build.");
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
