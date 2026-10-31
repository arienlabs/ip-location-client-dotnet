namespace Arienlabs.IpLocationClient;

/// <summary>Lokalizacja oraz dane ASN. Brak współrzędnej różni się od wartości zero.</summary>
public sealed record IpLocation
{
    public string? Country { get; init; }
    public string? Stateprov { get; init; }
    public string? StateprovCode { get; init; }
    public string? City { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public string? Continent { get; init; }
    public string? Timezone { get; init; }
    public uint? UsMetroCode { get; init; }
    public uint? AccuracyRadius { get; init; }
    public uint? Asn { get; init; }
    public string? AsnOrganization { get; init; }
    public string? AsnNetwork { get; init; }
}
