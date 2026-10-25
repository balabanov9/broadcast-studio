using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Broadcast.Api.Domain;

namespace Broadcast.Api.Infrastructure;
public interface IAmcpClient
{
    Task<string> Send(string command, CancellationToken ct = default, bool allowMissing = false);
}
public sealed class AmcpClient(IConfiguration config) : IAmcpClient
{
    public async Task<string> Send(string command, CancellationToken ct = default, bool allowMissing = false)
    {
        if (command.Contains('\r') || command.Contains('\n')) throw new ArgumentException("Недопустимая команда AMCP");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var client = new TcpClient();
        await client.ConnectAsync(config["Caspar:Host"] ?? "casparcg", config.GetValue("Caspar:Port", 5250), timeout.Token);
        await using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(command + "\r\n"), timeout.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var line = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("CasparCG закрыл соединение");
        if (!line.StartsWith('2') && !(allowMissing && line.StartsWith("404"))) throw new IOException($"CasparCG: {line}");
        if (line.StartsWith("201")) return await reader.ReadLineAsync(timeout.Token) ?? "";
        if (line.StartsWith("200"))
        {
            var body = new StringBuilder();
            while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 } part) body.AppendLine(part);
            return body.ToString();
        }
        return line;
    }
}
public static class Amcp
{
    public static string Quote(string value)
    {
        if (value.Any(char.IsControl)) throw new ArgumentException("Управляющие символы запрещены");
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
    public static string Producer(Source source, string? secret = null, string? directUrl = null) => source.Kind switch
    {
        SourceKind.S3 => Quote(directUrl ?? throw new InvalidOperationException("Не сформирован URL S3")),
        SourceKind.Color => source.Locator,
        SourceKind.Clip => Quote(source.Locator),
        SourceKind.Live => Quote($"rtmp://mediamtx:1935/ingest/{source.ChannelId:N}?user=engine&pass={Uri.EscapeDataString(secret ?? throw new InvalidOperationException("Не задан пароль чтения MediaMTX"))}"),
        _ => throw new ArgumentException("Неизвестный тип источника")
    };
    public static string Play(int slot, Source source, string transition = "CUT", int frames = 0, double seek = 0, string? secret = null, string? directUrl = null)
        => $"PLAY {slot}-10 {Producer(source, secret, directUrl)} {((source.Kind is SourceKind.Clip or SourceKind.S3) ? "LOOP " : "")}{transition} {frames}" +
           (source.Kind is SourceKind.Clip or SourceKind.S3 && seek > 0 ? $" SEEK {(long)(seek * 25)}" : "");
    public static string Graphics(int slot, string json) => $"CG {slot}-20 ADD 1 \"lumiere\" 1 {Quote(json)}";
    public static string Scene(GraphicScene scene) => JsonSerializer.Serialize(scene, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
