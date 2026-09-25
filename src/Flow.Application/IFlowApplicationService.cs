namespace Flow.Application;

/// <summary>
/// Exposes presentation-neutral Flow use cases to command-line and graphical hosts.
/// The caller retains ownership of streams, paths, prompts, persistence, and exit codes.
/// </summary>
public interface IFlowApplicationService
{
    public Task<InspectEpubResult> InspectEpubAsync(
        InspectEpubRequest request,
        IProgress<FlowApplicationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    public Task<ImportEpubResult> ImportEpubAsync(
        ImportEpubRequest request,
        IProgress<FlowApplicationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    public Task<ValidateDocumentResult> ValidateDocumentAsync(
        ValidateDocumentRequest request,
        IProgress<FlowApplicationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    public Task<CalculateDocumentHashResult> CalculateDocumentHashAsync(
        CalculateDocumentHashRequest request,
        IProgress<FlowApplicationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    public Task<RenderHtmlResult> RenderHtmlAsync(
        RenderHtmlRequest request,
        IProgress<FlowApplicationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    public Task<RenderHtmlBookResult> RenderHtmlBookAsync(
        RenderHtmlBookRequest request,
        IProgress<FlowApplicationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
