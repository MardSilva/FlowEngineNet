using System.Xml.Linq;

namespace Flow.Conformance.Tests;

public sealed class ProjectDependencyConformanceTests
{
    [Fact]
    public void Repository_DefinesVersionAndDisablesPackageCreation()
    {
        var properties = XDocument.Load(Path.Combine(FindRepositoryRoot(), "Directory.Build.props"));

        Assert.Equal("0.2.0", properties.Descendants("VersionPrefix").Single().Value);
        Assert.Equal("false", properties.Descendants("IsPackable").Single().Value);
        Assert.Equal("true", properties.Descendants("GenerateDocumentationFile").Single().Value);
    }

    [Fact]
    public void ProductionProjects_HaveOnlyTheApprovedDirectDependencies()
    {
        var root = FindRepositoryRoot();
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Flow.Core"] = [],
            ["Flow.Documents"] = ["Flow.Core"],
            ["Flow.Layout"] = ["Flow.Core", "Flow.Documents"],
            ["Flow.Rendering"] = ["Flow.Documents", "Flow.Layout"],
            ["Flow.Rendering.Html"] = ["Flow.Rendering"],
            ["Flow.Security"] = ["Flow.Documents"],
            ["Flow.Epub"] = ["Flow.Core", "Flow.Documents"],
            ["Flow.Cli"] = ["Flow.Documents", "Flow.Epub", "Flow.Layout", "Flow.Rendering.Html", "Flow.Security"],
        };

        foreach (var (projectName, expectedReferences) in expected)
        {
            var projectPath = Path.Combine(root, "src", projectName, $"{projectName}.csproj");
            var actualReferences = XDocument.Load(projectPath)
                .Descendants("ProjectReference")
                .Select(static element => (string?)element.Attribute("Include"))
                .Where(static include => include is not null)
                .Select(static include => ProjectNameFromReference(include!))
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expectedReferences.Order(StringComparer.Ordinal), actualReferences);
        }
    }

    private static string ProjectNameFromReference(string include)
    {
        var normalized = include.Replace('\\', '/');
        return Path.GetFileNameWithoutExtension(normalized[(normalized.LastIndexOf('/') + 1)..]);
    }

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
