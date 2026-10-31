namespace Arienlabs.IpLocationClient;

public interface IIpLocationClient
{
    /// <summary>Zwraca null wyłącznie wtedy, gdy API odpowie HTTP 404.</summary>
    Task<IpLocation?> ResolveLocationAsync(string ipAddress, CancellationToken cancellationToken = default);
    /// <summary>Wyszukuje do 100 adresów. Wynik ma kanoniczne klucze IP, bez duplikatów.</summary>
    Task<IReadOnlyDictionary<string, IpLocation>> ResolveBatchAsync(IEnumerable<string> ipAddresses, CancellationToken cancellationToken = default);
    /// <summary>HTTP 503 ze stanem gotowości zwracane jest jako Ready=false, nie jako awaria transportu.</summary>
    Task<IpLocationHealth> GetHealthAsync(CancellationToken cancellationToken = default);
}
