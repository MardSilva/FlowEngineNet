using System.Reflection;
using Flow.Documents;
using Flow.Epub;

namespace Flow.Cli.Tests;

public sealed class CliDiagnosticLocalizationTests
{
    [Fact]
    public void PortugueseCatalog_CoversEveryStableDocumentAndEpubDiagnosticCode()
    {
        var catalog = new CliTextCatalog(CliTextCatalog.PortugueseBrazilCultureName);

        foreach (var code in StableDiagnosticCodes())
        {
            var message = catalog.DiagnosticMessage(code, "original technical detail");

            Assert.False(message.StartsWith("original technical detail", StringComparison.Ordinal));
            Assert.Contains("Detalhe técnico: original technical detail", message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EnglishCatalog_PreservesOriginalDiagnosticMessage()
    {
        var catalog = new CliTextCatalog(CliTextCatalog.DefaultCultureName);

        var message = catalog.DiagnosticMessage(
            EpubDiagnosticCodes.MissingResource,
            "Manifest item 'chapter' is missing.");

        Assert.Equal("Manifest item 'chapter' is missing.", message);
    }

    [Fact]
    public void PortugueseCatalog_PreservesFallbackForUnknownDiagnosticCode()
    {
        var catalog = new CliTextCatalog(CliTextCatalog.PortugueseBrazilCultureName);

        var message = catalog.DiagnosticMessage("FUTURE_DIAGNOSTIC", "Future technical detail.");

        Assert.Equal("Future technical detail.", message);
    }

    private static IEnumerable<string> StableDiagnosticCodes() =>
        ConstantsFrom(typeof(EpubDiagnosticCodes))
            .Concat(ConstantsFrom(typeof(ValidationDiagnosticCodes)))
            .Concat(ConstantsFrom(typeof(FlowSerializationDiagnosticCodes)))
            .OrderBy(static code => code, StringComparer.Ordinal);

    private static IEnumerable<string> ConstantsFrom(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(static field => (string)field.GetRawConstantValue()!);
}
