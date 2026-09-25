using System.Collections.Immutable;
using Flow.Application;

namespace Flow.Windows.Shell;

/// <summary>Contains one reconstructible local EPUB entry.</summary>
public sealed record FlowWindowsLibraryItem(
    string SourcePath,
    FlowWindowsBookSummary? Summary,
    ImmutableArray<FlowApplicationDiagnostic> Diagnostics,
    bool IsUsable);

/// <summary>Contains a bounded, noncanonical snapshot of one user-selected directory.</summary>
public sealed record FlowWindowsLibraryIndex(
    string DirectoryPath,
    ImmutableArray<FlowWindowsLibraryItem> Items,
    bool WasTruncated);

/// <summary>Discovers local EPUBs without persisting the reconstructible index.</summary>
public sealed class FlowWindowsLibraryService
{
    public const int MaximumBooks = 500;
    private readonly IFlowWindowsOperationService _operations;

    public FlowWindowsLibraryService(IFlowWindowsOperationService operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public async Task<FlowWindowsLibraryIndex> DiscoverAsync(
        string directoryPath,
        IProgress<(int Completed, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var root = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"The selected personal folder was not found: {root}");
        }

        var candidates = Directory.EnumerateFiles(
                root,
                "*.epub",
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    MatchCasing = MatchCasing.CaseInsensitive,
                })
            .Select(Path.GetFullPath)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumBooks + 1)
            .ToArray();
        var wasTruncated = candidates.Length > MaximumBooks;
        var selected = candidates.Take(MaximumBooks).ToArray();
        var items = ImmutableArray.CreateBuilder<FlowWindowsLibraryItem>(selected.Length);

        for (var index = 0; index < selected.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = selected[index];
            try
            {
                var inspection = await _operations.ExecuteAsync(
                        new FlowWindowsOperationRequest(FlowWindowsOperationKind.Inspect, sourcePath),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                var validation = await _operations.ExecuteAsync(
                        new FlowWindowsOperationRequest(FlowWindowsOperationKind.Validate, sourcePath),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                var summary = Merge(inspection.Summary, validation.Summary);
                var diagnostics = inspection.Diagnostics
                    .Concat(validation.Diagnostics)
                    .Distinct()
                    .ToImmutableArray();
                items.Add(new FlowWindowsLibraryItem(
                    sourcePath,
                    summary,
                    diagnostics,
                    inspection.Succeeded && validation.Succeeded));
            }
            catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or InvalidDataException
                                              or NotSupportedException)
            {
                items.Add(new FlowWindowsLibraryItem(
                    sourcePath,
                    null,
                    [new FlowApplicationDiagnostic("FLOWWINLIB001", FlowApplicationDiagnosticSeverity.Error, exception.Message)],
                    false));
            }

            progress?.Report((index + 1, selected.Length));
        }

        return new FlowWindowsLibraryIndex(root, items.ToImmutable(), wasTruncated);
    }

    private static FlowWindowsBookSummary? Merge(
        FlowWindowsBookSummary? inspection,
        FlowWindowsBookSummary? validation)
    {
        if (inspection is null)
        {
            return validation;
        }

        if (validation is null)
        {
            return inspection;
        }

        return inspection with
        {
            AssetCount = validation.AssetCount,
            CoverMediaType = validation.CoverMediaType,
            CoverBytes = validation.CoverBytes,
        };
    }
}
