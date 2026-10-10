using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Flow.Support;

/// <summary>Reads recognition for Flow without sending documents or local paths.</summary>
public sealed class FlowSupporterClient(HttpClient client)
{
    public static Uri ServiceOrigin { get; } = new("https://mard-cloud.duckdns.org/");

    public async Task<FlowSupporterStatus> GetStatusAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(ServiceOrigin, "api/v1/supporter/me?productId=flow"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("FlowEngineNet",
            typeof(FlowSupporterClient).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<FlowSupporterStatus>(cancellationToken)
            ?? throw new InvalidDataException("Empty supporter response.");
        if (status.TierIds is null || status.BenefitCodes is null || status.CurrentStatus is null)
        {
            throw new InvalidDataException("Incomplete supporter response.");
        }
        return status;
    }
}

/// <summary>Contains recognition only; the private Patreon name is deliberately omitted.</summary>
public sealed record FlowSupporterStatus(
    string CurrentStatus,
    string[] TierIds,
    string[] BenefitCodes,
    bool IsStale,
    bool PublicRecognitionEnabled,
    string? RecognitionDisplayName,
    DateTimeOffset? CurrentSupportStartedAt)
{
    public string DisplayName(string language) =>
        PublicRecognitionEnabled && !string.IsNullOrWhiteSpace(RecognitionDisplayName)
            ? RecognitionDisplayName : language == "pt-BR" ? "Apoiante anônimo" : "Anonymous supporter";
}
