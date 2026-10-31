using System.Text.Json.Serialization;

namespace Arienlabs.IpLocationClient;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(IpLocation))]
[JsonSerializable(typeof(IpLocationHealth))]
[JsonSerializable(typeof(Dictionary<string, IpLocation>))]
[JsonSerializable(typeof(string[]))]
internal partial class ClientJsonContext : JsonSerializerContext;
