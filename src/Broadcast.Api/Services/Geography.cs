using System.Net;
using System.Text.Json;
using Broadcast.Api.Domain;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;

namespace Broadcast.Api.Services;
public sealed record GeoLocation(string? Country, string? Region);
public interface IGeography
{
    bool Available { get; }
    bool SupportsRegions { get; }
    GeoLocation Locate(IPAddress? ip);
}
public sealed class Geography : IGeography, IDisposable
{
    private readonly DatabaseReader? reader;
    public bool Available => reader is not null;
    public bool SupportsRegions { get; }
    public Geography(IConfiguration cfg, ILogger<Geography> logger)
    {
        var path = cfg["GeoIP:Database"] ?? "/geoip/GeoLite2-City.mmdb";
        if (!File.Exists(path)) return;
        try { reader = new DatabaseReader(path); SupportsRegions = reader.Metadata.DatabaseType.Contains("City", StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) { logger.LogError(e, "Не удалось открыть базу GeoIP"); }
    }
    public GeoLocation Locate(IPAddress? ip)
    {
        if (ip is null || reader is null) return new(null, null);
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        try
        {
            if (SupportsRegions)
            {
                var city = reader!.City(ip);
                var country = city.Country.IsoCode;
                var subdivision = city.MostSpecificSubdivision.IsoCode;
                return new(country, country is not null && subdivision is not null ? $"{country}-{subdivision}" : null);
            }
            return new(reader!.Country(ip).Country.IsoCode, null);
        }
        catch (AddressNotFoundException) { return new(null, null); }
    }
    public void Dispose() => reader?.Dispose();
}
public static class RegionalPolicy
{
    public static bool Blocked(Channel c, GeoLocation location)
    {
        if (!c.RegionRestrictionEnabled) return false;
        if (location.Country is null) return c.BlockUnknownCountry;
        var rules = JsonSerializer.Deserialize<string[]>(c.BlockedCountriesJson) ?? [];
        if (rules.Contains(location.Country, StringComparer.OrdinalIgnoreCase)) return true;
        if (location.Region is not null && rules.Contains(location.Region, StringComparer.OrdinalIgnoreCase)) return true;
        return location.Region is null && rules.Any(x => x.StartsWith(location.Country + "-", StringComparison.OrdinalIgnoreCase)) && c.BlockUnknownCountry;
    }
}
