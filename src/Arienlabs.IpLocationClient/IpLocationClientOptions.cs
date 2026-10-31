namespace Arienlabs.IpLocationClient;

public sealed class IpLocationClientOptions
{
    public Uri BaseAddress { get; set; } = new("http://localhost:8888/");
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}
