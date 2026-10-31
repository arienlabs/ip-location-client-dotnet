using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Arienlabs.IpLocationClient;

/// <summary>Współbieżny klient HTTP. Nie przejmuje własności przekazanego HttpClient.</summary>
public sealed class IpLocationClient : IIpLocationClient
{
    private const long MaxResponseBytes = 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly Uri _baseAddress;

    public IpLocationClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _baseAddress = ValidateBaseAddress(httpClient.BaseAddress);
    }

    internal static Uri ValidateBaseAddress(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https")
            || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
            throw new ArgumentException("Wymagany bezwzględny URL HTTP/HTTPS bez query, fragmentu i danych logowania.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    internal static string NormalizeAddress(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOfAny(['%', '[', ']']) >= 0 || value.Any(char.IsWhiteSpace)
            || !IPAddress.TryParse(value, out var address))
            throw new ArgumentException("Wymagany literalny adres IPv4 lub IPv6.", nameof(value));
        if (address.AddressFamily == AddressFamily.InterNetwork || value.Contains('.'))
        {
            // IPAddress akceptuje również skróty i zapis ósemkowy, których API nie przyjmuje.
            var parts = value[(value.LastIndexOf(':') + 1)..].Split('.');
            if (parts.Length != 4 || parts.Any(p => p.Length == 0 || (p.Length > 1 && p[0] == '0') || p.Any(c => c is < '0' or > '9')))
                throw new ArgumentException("Wymagany pełny dziesiętny adres IPv4.", nameof(value));
        }
        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }

    public async Task<IpLocation?> ResolveLocationAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseAddress, Uri.EscapeDataString(NormalizeAddress(ipAddress))));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(response, ClientJsonContext.Default.IpLocation, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, IpLocation>> ResolveBatchAsync(IEnumerable<string> ipAddresses, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ipAddresses);
        cancellationToken.ThrowIfCancellationRequested();
        var values = ipAddresses.Take(101).ToArray();
        if (values.Length > 100) throw new ArgumentException("API obsługuje maksymalnie 100 adresów w jednej operacji.", nameof(ipAddresses));
        var normalized = values.Select(NormalizeAddress).Distinct(StringComparer.Ordinal).ToArray();
        if (normalized.Length == 0) return new ReadOnlyDictionary<string, IpLocation>(new Dictionary<string, IpLocation>());
        using var request = new HttpRequestMessage(HttpMethod.Post, _baseAddress)
        {
            Content = JsonContent.Create(normalized, ClientJsonContext.Default.StringArray)
        };
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var wire = await ReadAsync(response, ClientJsonContext.Default.DictionaryStringIpLocation, cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<string, IpLocation>(StringComparer.Ordinal);
        foreach (var (key, value) in wire)
        {
            if (value is null) throw new JsonException("API zwróciło pusty element batch.");
            try { result[NormalizeAddress(key)] = value; }
            catch (ArgumentException ex) { throw new JsonException("API zwróciło nieprawidłowy klucz IP.", ex); }
        }
        return new ReadOnlyDictionary<string, IpLocation>(result);
    }

    public async Task<IpLocationHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseAddress, "health"));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(response, ClientJsonContext.Default.IpLocationHealth, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        await response.Content.LoadIntoBufferAsync(MaxResponseBytes, cancellationToken).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("API zwróciło JSON null zamiast obiektu.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        await response.Content.LoadIntoBufferAsync(MaxResponseBytes, cancellationToken).ConfigureAwait(false);
        throw new IpLocationApiException(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }
}
