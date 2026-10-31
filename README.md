# Arienlabs IpLocationClient for .NET 10

<img src="https://raw.githubusercontent.com/arienlabs/ip-location-client-dotnet/main/assets/arienlabs.png" alt="Arienlabs" width="128">

A typed HTTP client for [Arienlabs IP Location API](https://github.com/arienlabs/ip-location-api). Supports IPv4, IPv6, ASN data, batch lookup and service health. Uses System.Text.Json source generation and IHttpClientFactory. It does not invent a location for private addresses or hide service failures.

## Install

Download the package from [GitHub Releases](https://github.com/arienlabs/ip-location-client-dotnet/releases). Public release assets do not require a GitHub or NuGet.org account.

```powershell
New-Item -ItemType Directory -Force vendor/arienlabs | Out-Null
Invoke-WebRequest `
  https://github.com/arienlabs/ip-location-client-dotnet/releases/download/v1.0.0/Arienlabs.IpLocationClient.1.0.0.nupkg `
  -OutFile vendor/arienlabs/Arienlabs.IpLocationClient.1.0.0.nupkg
dotnet nuget add source "$PWD/vendor/arienlabs" --name ArienlabsLocal
dotnet add package Arienlabs.IpLocationClient --version 1.0.0
```

The package targets **.NET 10**. Keep the normal NuGet.org source enabled for Microsoft dependencies; downloading those dependencies does not require an account. You can put the local feed in your application's `NuGet.Config` instead of changing user-level NuGet sources. Release assets contain `.nupkg`, `.snupkg` and SHA-256 checksums. Publication to NuGet.org is planned separately; GitHub Releases is the initial distribution channel.

## Dependency injection

```csharp
using Arienlabs.IpLocationClient;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddIpLocationClient(options =>
{
    options.BaseAddress = new Uri("http://localhost:8888/");
    options.Timeout = TimeSpan.FromSeconds(5);
});

using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<IIpLocationClient>();
var location = await client.ResolveLocationAsync("8.8.8.8");
Console.WriteLine(location?.Country ?? "Not found");
```

`AddIpLocationClient` returns an `IHttpClientBuilder`, so callers can configure their own handlers, authentication and resilience policies. No retry policy is installed by this library. The DI defaults are `http://localhost:8888/` and a five-second timeout. Do not capture a typed client in a singleton service without considering HttpClientFactory lifetimes.

## Use your own HttpClient

```csharp
using var http = new HttpClient
{
    BaseAddress = new Uri("http://localhost:8888/"),
    Timeout = TimeSpan.FromSeconds(5)
};
IIpLocationClient client = new IpLocationClient(http);
var location = await client.ResolveLocationAsync("2001:4860:4860::8888");
```

The library does not dispose the supplied HttpClient or override its timeout. URL path prefixes are supported. Base URLs must be absolute HTTP/HTTPS URLs without query parameters, fragments or embedded credentials.

## Batch and health

```csharp
var results = await client.ResolveBatchAsync(
    ["8.8.8.8", "2001:4860:4860::8888", "8.8.8.8"],
    cancellationToken);

var health = await client.GetHealthAsync(cancellationToken);
Console.WriteLine($"Ready: {health.Ready}, updates: {health.UpdateEnabled}");
```

- Up to 100 input addresses per batch. Duplicate normalized addresses are sent once.
- Batch keys are normalized IP literals, including compressed IPv6 and unmapped IPv4-mapped IPv6. Missing addresses are omitted. An empty batch does not perform HTTP.
- Coordinates accept JSON numbers and quoted numbers. Missing coordinates are `null`; zero remains zero. Parsing does not depend on the current culture.
- IPv4/IPv6 literals only. Hostnames, CIDR ranges, zone IDs and ambiguous shorthand/octal IPv4 are rejected before HTTP.
- Responses are limited to 1 MiB. Malformed JSON is an error.

## Error handling

Single lookup returns `null` only for HTTP 404. Other unsuccessful responses throw `IpLocationApiException`, which exposes `StatusCode` and a size-limited `ResponseBody`. Transport errors, timeouts and cancellation retain standard HttpClient exception behavior. HTTP 503 from the health endpoint is decoded as a valid health state with `Ready == false`.

There are no automatic retries and no silent fallback to an empty location. A missing lookup, an unavailable service and a cancelled operation remain distinct.

## Migrating from Deegitals.GeoIpClient

1. Replace the package with `Arienlabs.IpLocationClient` and target .NET 10.
2. Replace `AddGeoIpClient` with `AddIpLocationClient`; configure `BaseAddress` instead of `GeoIpServiceUrl`.
3. Inject `IIpLocationClient` instead of `ILocationResolverService`. The single lookup method remains `ResolveLocationAsync` and accepts a cancellation token.
4. Handle nullable results and explicit failures. Coordinates are nullable decimals.
5. Keep environment-specific private-address rules in the consuming application. This client does **not** automatically return `PL` for local/private addresses. If that behavior is required, implement it in an application adapter with its own tests.

Migration of existing services should be incremental; this SDK does not change any production deployment.

## Build, test and pack

```sh
dotnet test Arienlabs.IpLocationClient.sln -c Release
dotnet pack src/Arienlabs.IpLocationClient -c Release -o artifacts
dotnet list Arienlabs.IpLocationClient.sln package --vulnerable --include-transitive
```

Tests cover the full wire contract, quoted/numeric/zero coordinates, Polish culture, IPv6, path prefixes, not-found semantics, errors, batch normalization, input validation, response limits, cancellation, timeouts, concurrent use and DI. JSON reflection is disabled in the test project. The package includes the Arienlabs icon, README and XML documentation.

## Publishing to NuGet.org

GitHub authentication does not authenticate a publisher to NuGet.org. Configure [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) on the NuGet.org account that will own `Arienlabs.IpLocationClient`:

1. Sign in to NuGet.org, open your profile menu and select **Trusted Publishing**.
2. Add a GitHub policy: owner `arienlabs`, repository `ip-location-client-dotnet`, workflow file `publish.yml` (file name only), no environment restriction.
3. Permit creating and updating `Arienlabs.IpLocationClient`.
4. Set the GitHub repository variable `NUGET_USER` to your NuGet.org **profile name**, not your email address.
5. Run the release workflow for the desired version after the workflow has been installed. The workflow exchanges GitHub OIDC identity for a short-lived NuGet API key.

Do not commit API keys or credentials. Instructions and release artifacts can be public; publisher credentials cannot.
