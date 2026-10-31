using Microsoft.Extensions.DependencyInjection;

namespace Arienlabs.IpLocationClient;

public static class ServiceCollectionExtensions
{
    /// <summary>Rejestruje klienta typowanego. Zwrócony builder umożliwia dodanie własnych handlerów.</summary>
    public static IHttpClientBuilder AddIpLocationClient(this IServiceCollection services, Action<IpLocationClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new IpLocationClientOptions();
        configure?.Invoke(options);
        var address = IpLocationClient.ValidateBaseAddress(options.BaseAddress);
        if (options.Timeout <= TimeSpan.Zero || options.Timeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(configure), "Timeout musi być dodatni i mieścić się w zakresie HttpClient.");
        var timeout = options.Timeout;
        return services.AddHttpClient<IIpLocationClient, IpLocationClient>(client =>
        {
            client.BaseAddress = address;
            client.Timeout = timeout;
        });
    }
}
