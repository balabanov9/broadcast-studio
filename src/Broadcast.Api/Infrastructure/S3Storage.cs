using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Broadcast.Api.Domain;
using Microsoft.AspNetCore.DataProtection;

namespace Broadcast.Api.Infrastructure;
public sealed record S3Credentials(string AccessKey, string SecretKey, string? SessionToken);
public sealed record S3Object(string Key, long Size);
public sealed record S3Page(List<S3Object> Objects, string? NextToken);
public interface IS3Storage
{
    Task<string> SignedUrl(StorageConnection connection, string key, CancellationToken ct);
    Task<S3Page> List(StorageConnection connection, string prefix, string? token, CancellationToken ct);
    Task<HttpResponseMessage> Download(StorageConnection connection, string key, CancellationToken ct);
}
public static class S3Signing
{
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static byte[] Hmac(byte[] key, string text) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(text));
    public static void Sign(HttpRequestMessage request, S3Credentials credentials, string region, DateTimeOffset now)
    {
        var uri = request.RequestUri!;
        var date = now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var timestamp = now.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var hash = Hash("");
        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
        { ["host"] = uri.Authority, ["x-amz-content-sha256"] = hash, ["x-amz-date"] = timestamp };
        if (!string.IsNullOrEmpty(credentials.SessionToken)) headers["x-amz-security-token"] = credentials.SessionToken;
        foreach (var (key, value) in headers) if (key != "host") request.Headers.TryAddWithoutValidation(key, value);
        request.Headers.Host = uri.Authority;
        var signed = string.Join(';', headers.Keys);
        var canonical = $"{request.Method}\n{uri.AbsolutePath}\n{uri.Query.TrimStart('?')}\n{string.Concat(headers.Select(x => $"{x.Key}:{x.Value.Trim()}\n"))}\n{signed}\n{hash}";
        var scope = $"{date}/{region}/s3/aws4_request";
        var keyDate = Hmac(Encoding.UTF8.GetBytes("AWS4" + credentials.SecretKey), date);
        var keyRegion = Hmac(keyDate, region); var keyService = Hmac(keyRegion, "s3"); var keySigning = Hmac(keyService, "aws4_request");
        var signature = Convert.ToHexString(Hmac(keySigning, $"AWS4-HMAC-SHA256\n{timestamp}\n{scope}\n{Hash(canonical)}")).ToLowerInvariant();
        request.Headers.TryAddWithoutValidation("Authorization", $"AWS4-HMAC-SHA256 Credential={credentials.AccessKey}/{scope},SignedHeaders={signed},Signature={signature}");
    }
    public static string Presign(Uri endpoint, string bucket, string objectKey, S3Credentials credentials, string region, DateTimeOffset now, bool pathStyle = true)
    {
        if (objectKey.Split('/').Any(x => x is "." or "..")) throw new ArgumentException("Неподдерживаемый ключ S3");
        var date = now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var timestamp = now.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var scope = $"{date}/{region}/s3/aws4_request";
        var authority = pathStyle ? endpoint.Authority : bucket + "." + endpoint.Authority;
        var path = (pathStyle ? "/" + Uri.EscapeDataString(bucket) : "") + "/" + string.Join('/', objectKey.Split('/').Select(Uri.EscapeDataString));
        var query = new Dictionary<string, string> { ["X-Amz-Algorithm"] = "AWS4-HMAC-SHA256", ["X-Amz-Credential"] = credentials.AccessKey + "/" + scope, ["X-Amz-Date"] = timestamp, ["X-Amz-Expires"] = "604800", ["X-Amz-SignedHeaders"] = "host" };
        if (!string.IsNullOrEmpty(credentials.SessionToken)) query["X-Amz-Security-Token"] = credentials.SessionToken;
        var canonicalQuery = string.Join('&', query.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
        var canonical = $"GET\n{path}\n{canonicalQuery}\nhost:{authority}\n\nhost\nUNSIGNED-PAYLOAD";
        var signingKey = Hmac(Hmac(Hmac(Hmac(Encoding.UTF8.GetBytes("AWS4" + credentials.SecretKey), date), region), "s3"), "aws4_request");
        var signature = Convert.ToHexString(Hmac(signingKey, $"AWS4-HMAC-SHA256\n{timestamp}\n{scope}\n{Hash(canonical)}")).ToLowerInvariant();
        return endpoint.Scheme + "://" + authority + path + "?" + canonicalQuery + "&X-Amz-Signature=" + signature;
    }
    public static bool PublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return (bytes[0] & 0xe0) == 0x20 && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
        }
        var b = address.GetAddressBytes();
        return b[0] is not (0 or 10 or 127) && b[0] < 224 && !(b[0] == 100 && b[1] is >= 64 and <= 127) && !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] is >= 16 and <= 31) && !(b[0] == 192 && (b[1] == 168 || b[1] == 0 || b[1] == 2)) && !(b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51)) && !(b[0] == 203 && b[1] == 0 && b[2] == 113);
    }
    public static SocketsHttpHandler Handler() => new()
    {
        AllowAutoRedirect = false, UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(10), PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = async (context, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            if (addresses.Length == 0 || addresses.Any(x => !PublicAddress(x))) throw new HttpRequestException("S3: внутренние и служебные адреса запрещены");
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct); return new NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); throw; }
        }
    };
}
public sealed class S3Storage(IHttpClientFactory clients, IDataProtectionProvider protection) : IS3Storage
{
    private readonly IDataProtector protector = protection.CreateProtector("Lumiere.S3.Credentials.v1");
    public async Task<string> SignedUrl(StorageConnection c, string key, CancellationToken ct)
    {
        var endpoint = new Uri(c.Endpoint);
        var addresses = await Dns.GetHostAddressesAsync(c.PathStyle ? endpoint.Host : c.Bucket + "." + endpoint.Host, ct);
        if (addresses.Length == 0 || addresses.Any(x => !S3Signing.PublicAddress(x))) throw new ArgumentException("S3: внутренние адреса запрещены");
        var credentials = JsonSerializer.Deserialize<S3Credentials>(protector.Unprotect(c.ProtectedCredentials))!;
        return S3Signing.Presign(endpoint, c.Bucket, key, credentials, c.Region, DateTimeOffset.UtcNow, c.PathStyle);
    }
    public async Task<S3Page> List(StorageConnection c, string prefix, string? token, CancellationToken ct)
    {
        var query = new Dictionary<string, string> { ["list-type"] = "2", ["max-keys"] = "100", ["prefix"] = prefix };
        if (!string.IsNullOrEmpty(token)) query["continuation-token"] = token;
        using var response = await Send(c, "", query, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var xml = XmlReader.Create(stream, new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 });
        var document = await XDocument.LoadAsync(xml, LoadOptions.None, ct);
        var ns = document.Root!.Name.Namespace;
        var objects = document.Root.Elements(ns + "Contents").Select(x => new S3Object(x.Element(ns + "Key")!.Value, long.Parse(x.Element(ns + "Size")!.Value, CultureInfo.InvariantCulture))).ToList();
        return new(objects, document.Root.Element(ns + "NextContinuationToken")?.Value);
    }
    public Task<HttpResponseMessage> Download(StorageConnection c, string key, CancellationToken ct) => Send(c, key, [], ct);
    private async Task<HttpResponseMessage> Send(StorageConnection c, string key, Dictionary<string, string> query, CancellationToken ct)
    {
        if (key.Split('/').Any(x => x is "." or "..") || key.Any(char.IsControl)) throw new ArgumentException("Ключ S3 содержит неподдерживаемые сегменты");
        var endpoint = new Uri(c.Endpoint);
        var authority = c.PathStyle ? endpoint.Authority : c.Bucket + "." + endpoint.Authority;
        var path = (c.PathStyle ? "/" + Uri.EscapeDataString(c.Bucket) : "/") + (key.Length > 0 ? (c.PathStyle ? "/" : "") + string.Join('/', key.Split('/').Select(Uri.EscapeDataString)) : "");
        var qs = string.Join('&', query.Select(x => new KeyValuePair<string, string>(Uri.EscapeDataString(x.Key), Uri.EscapeDataString(x.Value))).OrderBy(x => x.Key, StringComparer.Ordinal).ThenBy(x => x.Value, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}"));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint.Scheme + "://" + authority + path + (qs.Length > 0 ? "?" + qs : "")));
        var credentials = JsonSerializer.Deserialize<S3Credentials>(protector.Unprotect(c.ProtectedCredentials)) ?? throw new InvalidOperationException("Ключи S3 повреждены");
        S3Signing.Sign(request, credentials, c.Region, DateTimeOffset.UtcNow);
        var response = await clients.CreateClient("s3").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) { var code = (int)response.StatusCode; response.Dispose(); throw new ArgumentException($"S3 вернул HTTP {code}. Проверьте endpoint, регион и права ключа."); }
        return response;
    }
}
