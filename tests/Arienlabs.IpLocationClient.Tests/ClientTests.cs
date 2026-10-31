using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Client = Arienlabs.IpLocationClient.IpLocationClient;

namespace Arienlabs.IpLocationClient.Tests;

public sealed class ClientTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return action(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Json(string value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private static HttpClient Http(Handler handler, string url = "http://localhost/prefix") => new(handler) { BaseAddress = new Uri(url) };

    [Fact]
    public async Task ReadsAllFieldsAndPreservesZeroInPolishCulture()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json("""
            {"country":"PL","stateprov":"Mazowieckie","stateprovCode":"MZ","city":"Warsaw",
             "latitude":"0.0","longitude":"-12.5","continent":"EU","timezone":"Europe/Warsaw",
             "usMetroCode":123,"accuracyRadius":10,"asn":15169,"asnOrganization":"Test","asnNetwork":"8.8.8.0/24"}
            """)));
        using var http = Http(handler);
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
            var value = await new Client(http).ResolveLocationAsync("8.8.8.8");
            Assert.NotNull(value);
            Assert.Equal(0m, value.Latitude);
            Assert.Equal(-12.5m, value.Longitude);
            Assert.Equal("PL", value.Country);
            Assert.Equal("Mazowieckie", value.Stateprov);
            Assert.Equal("MZ", value.StateprovCode);
            Assert.Equal("Warsaw", value.City);
            Assert.Equal("EU", value.Continent);
            Assert.Equal("Europe/Warsaw", value.Timezone);
            Assert.Equal(123u, value.UsMetroCode);
            Assert.Equal(10u, value.AccuracyRadius);
            Assert.Equal(15169u, value.Asn);
            Assert.Equal("Test", value.AsnOrganization);
            Assert.Equal("8.8.8.0/24", value.AsnNetwork);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Theory]
    [InlineData("8.8.8.8", "8.8.8.8")]
    [InlineData("2001:4860:4860::8888", "2001:4860:4860::8888")]
    [InlineData("::ffff:8.8.8.8", "8.8.8.8")]
    public async Task PreservesBasePathAndNormalizesLiteralAddress(string input, string canonical)
    {
        using var handler = new Handler((request, _) =>
        {
            Assert.Equal("/prefix/" + canonical, Uri.UnescapeDataString(request.RequestUri!.AbsolutePath));
            return Task.FromResult(Json("{\"latitude\":1.5}"));
        });
        using var http = Http(handler);
        var value = await new Client(http).ResolveLocationAsync(input);
        Assert.Equal(1.5m, value!.Latitude);
        Assert.Null(value.Longitude);
    }

    [Fact]
    public async Task NotFoundIsNullWithoutRetryOrFakeCountry()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json("{}", HttpStatusCode.NotFound)));
        using var http = Http(handler);
        Assert.Null(await new Client(http).ResolveLocationAsync("10.0.0.1"));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task ServiceFailuresAreExplicit(int status)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json("{\"error\":\"failure\"}", (HttpStatusCode)status)));
        using var http = Http(handler);
        var error = await Assert.ThrowsAsync<IpLocationApiException>(() => new Client(http).ResolveLocationAsync("8.8.8.8"));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.Contains("failure", error.ResponseBody);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("1")]
    [InlineData("127.1")]
    [InlineData("127.000.0.1")]
    [InlineData("0x7f000001")]
    [InlineData(" 8.8.8.8")]
    [InlineData("fe80::1%eth0")]
    [InlineData("[::1]")]
    [InlineData("8.8.8.8/../health")]
    [InlineData("::ffff:192.168.001.1")]
    public async Task InvalidInputNeverReachesNetwork(string ip)
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("Nie powinno być HTTP."));
        using var http = Http(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => new Client(http).ResolveLocationAsync(ip));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task BatchDeduplicatesAndNormalizesResponseKeys()
    {
        using var handler = new Handler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/prefix/", request.RequestUri!.AbsolutePath);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal(2, document.RootElement.GetArrayLength());
            return Json("{\"2001:db8:0:0:0:0:0:1\":{\"country\":\"PL\"},\"8.8.8.8\":{\"asn\":15169}}");
        });
        using var http = Http(handler);
        var result = await new Client(http).ResolveBatchAsync(["2001:DB8::1", "2001:db8::1", "::ffff:8.8.8.8"]);
        Assert.Equal(2, result.Count);
        Assert.Equal("PL", result["2001:db8::1"].Country);
        Assert.Equal(15169u, result["8.8.8.8"].Asn);
    }

    [Fact]
    public async Task EmptyBatchIsLocalAndLargeBatchIsRejected()
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException());
        using var http = Http(handler);
        var client = new Client(http);
        Assert.Empty(await client.ResolveBatchAsync([]));
        await Assert.ThrowsAsync<ArgumentException>(() => client.ResolveBatchAsync(Enumerable.Repeat("8.8.8.8", 101)));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"bad-key\":{}}")]
    [InlineData("{\"8.8.8.8\":null}")]
    public async Task InvalidBatchResponseIsRejected(string json)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(json)));
        using var http = Http(handler);
        await Assert.ThrowsAsync<JsonException>(() => new Client(http).ResolveBatchAsync(["8.8.8.8"]));
    }

    [Fact]
    public async Task UnavailableHealthIsAValidState()
    {
        using var handler = new Handler((request, _) =>
        {
            Assert.EndsWith("/prefix/health", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Json("{\"ready\":false,\"updateEnabled\":true,\"cityBuild\":\"2026-10-27T17:52:02Z\"}", HttpStatusCode.ServiceUnavailable));
        });
        using var http = Http(handler);
        var value = await new Client(http).GetHealthAsync();
        Assert.False(value.Ready);
        Assert.True(value.UpdateEnabled);
        Assert.Equal(2026, value.CityBuild!.Value.Year);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}{}")]
    [InlineData("{\"latitude\":\"not-a-number\"}")]
    public async Task MalformedLocationIsNotReturnedAsEmptySuccess(string json)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(json)));
        using var http = Http(handler);
        await Assert.ThrowsAsync<JsonException>(() => new Client(http).ResolveLocationAsync("8.8.8.8"));
    }

    [Fact]
    public async Task BoundsResponseSize()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(new string(' ', 1024 * 1024 + 1))));
        using var http = Http(handler);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => new Client(http).ResolveLocationAsync("8.8.8.8"));
    }

    [Fact]
    public async Task PropagatesCancellationAndTimeout()
    {
        using var handler = new Handler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Json("{}"); });
        using var http = Http(handler);
        http.Timeout = TimeSpan.FromMilliseconds(30);
        var client = new Client(http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ResolveLocationAsync("8.8.8.8"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ResolveLocationAsync("8.8.8.8", cancelled.Token));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task SupportsConcurrentUse()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json("{\"country\":\"US\"}")));
        using var http = Http(handler);
        var client = new Client(http);
        var values = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => client.ResolveLocationAsync("8.8.8.8")));
        Assert.All(values, value => Assert.Equal("US", value!.Country));
    }

    [Fact]
    public async Task RegistersTypedClientAndAllowsOwnHandler()
    {
        var services = new ServiceCollection();
        services.AddIpLocationClient(options => options.BaseAddress = new Uri("http://service.test/api"))
            .ConfigurePrimaryHttpMessageHandler(() => new Handler((request, _) =>
            {
                Assert.Equal("http://service.test/api/8.8.8.8", request.RequestUri!.AbsoluteUri);
                return Task.FromResult(Json("{\"country\":\"US\"}"));
            }));
        using var provider = services.BuildServiceProvider();
        Assert.Equal("US", (await provider.GetRequiredService<IIpLocationClient>().ResolveLocationAsync("8.8.8.8"))!.Country);
    }

    [Theory]
    [InlineData("ftp://example.com/")]
    [InlineData("http://example.com/?x=y")]
    [InlineData("http://user:password@example.com/")]
    public void RejectsInvalidConfiguration(string url)
    {
        using var http = new HttpClient { BaseAddress = new Uri(url) };
        Assert.Throws<ArgumentException>(() => new Client(http));
    }
}
