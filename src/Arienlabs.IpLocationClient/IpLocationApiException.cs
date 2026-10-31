using System.Net;

namespace Arienlabs.IpLocationClient;

/// <summary>Odpowiedź błędu HTTP od usługi lokalizacji.</summary>
public sealed class IpLocationApiException(HttpStatusCode statusCode, string responseBody)
    : HttpRequestException($"IP Location API: HTTP {(int)statusCode}.", null, statusCode)
{
    public string ResponseBody { get; } = responseBody;
}
