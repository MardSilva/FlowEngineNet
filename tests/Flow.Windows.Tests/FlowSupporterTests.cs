using System.Net;
using System.Text;
using Flow.Support;

namespace Flow.Windows.Tests;

public sealed class FlowSupporterTests
{
    [Theory]
    [InlineData(false, "Private name", "pt-BR", "Apoiante anônimo")]
    [InlineData(false, "Private name", "en-US", "Anonymous supporter")]
    [InlineData(true, "Mard", "en-US", "Mard")]
    [InlineData(true, null, "en-US", "Anonymous supporter")]
    public void RecognitionRespectsConsent(bool consent, string? name, string language, string expected)
    {
        var status = new FlowSupporterStatus("free", [], [], false, consent, name, null);
        Assert.Equal(expected, status.DisplayName(language));
    }

    [Fact]
    public async Task RequestUsesFlowProductAndBearerWithoutPrivateName()
    {
        using var handler = new StubHandler();
        using var http = new HttpClient(handler);
        var status = await new FlowSupporterClient(http).GetStatusAsync("test-key");
        Assert.Equal("Anonymous supporter", status.DisplayName("en-US"));
        Assert.Equal("free", status.CurrentStatus);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://mard-cloud.duckdns.org/api/v1/supporter/me?productId=flow", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-key", request.Headers.Authorization.Parameter);
            Assert.Null(request.Content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"displayName":"Private Patreon name","currentStatus":"free","tierIds":[],
                     "benefitCodes":[],"isStale":false,"publicRecognitionEnabled":false,
                     "recognitionDisplayName":"Private Patreon name","currentSupportStartedAt":null}
                    """, Encoding.UTF8, "application/json"),
            });
        }
    }
}
