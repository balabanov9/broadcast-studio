using System.Collections.Concurrent;
using Broadcast.Api.Data;
using Broadcast.Api.Domain;
using Broadcast.Api.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Broadcast.Api.Services;
public sealed class AirHub : Hub;
public sealed class ConflictException(string message) : Exception(message);
public sealed class Control(IDbContextFactory<BroadcastDb> factory, IAmcpClient amcp, IHubContext<AirHub> hub, IConfiguration config, IS3Storage storage, ILogger<Control> log)
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> locks = new();
    private readonly ConcurrentDictionary<Guid, bool> connected = new();
    public async Task Run(Guid id, Func<BroadcastDb, Channel, Task> action, CancellationToken ct = default)
    {
        var gate = locks.GetOrAdd(id, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var channel = await db.Channels.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Канал не найден");
            await action(db, channel);
            db.ChangeTracker.DetectChanges();
            if (!db.ChangeTracker.HasChanges()) return;
            channel.Revision++;
            await db.SaveChangesAsync(ct);
            try { await hub.Clients.All.SendAsync("changed", id, ct); }
            catch (Exception e) { log.LogWarning(e, "Не удалось отправить событие клиентам; изменения уже сохранены"); }
        }
        finally { gate.Release(); }
    }
    public static void Check(Channel c, long revision)
    {
        if (c.Revision != revision) throw new ConflictException("Состояние эфира изменилось. Обновите данные и повторите действие.");
    }
    public static async Task<Source> OwnedSource(BroadcastDb db, Channel c, Guid id)
        => await db.Sources.SingleOrDefaultAsync(x => x.Id == id && x.ChannelId == c.Id) ?? throw new ArgumentException("Источник не принадлежит каналу");
    public async Task Ensure(Channel c, CancellationToken ct)
    {
        if (connected.ContainsKey(c.Id)) return;
        for (var n = 0; n < 2; n++)
        {
            var slot = c.Slot * 2 - 1 + n;
            var path = n == 0 ? $"program/{c.Slug}" : $"preview/{c.Slug}";
            await amcp.Send($"REMOVE {slot}-100", ct, true);
            var secret = config["Media:PublishSecret"] ?? throw new InvalidOperationException("Не задан Media:PublishSecret");
            await amcp.Send($"ADD {slot}-100 STREAM {Amcp.Quote($"rtmp://mediamtx:1935/{path}?user=engine&pass={Uri.EscapeDataString(secret)}")} -format flv -codec:v libx264 -preset:v ultrafast -tune:v zerolatency -b:v 3500k -g:v 50 -bf:v 0 -filter:v format=yuv420p -codec:a aac -ar:a 48000", ct);
        }
        connected[c.Id] = true;
    }
    public void Disconnect(Guid id) => connected.TryRemove(id, out _);
    public async Task Preview(BroadcastDb db, Channel c, Guid sourceId, CancellationToken ct)
    {
        var source = await OwnedSource(db, c, sourceId);
        await Ensure(c, ct);
        await amcp.Send(Amcp.Play(c.Slot * 2, source, secret: config["Media:PublishSecret"], directUrl: await Resolve(db, c, source, ct)), ct);
        c.PreviewId = source.Id;
        c.Error = null;
    }
    public async Task Play(BroadcastDb db, Channel c, Source source, string transition, int frames, string reason, CancellationToken ct, double seek = 0)
    {
        await Ensure(c, ct);
        await amcp.Send(Amcp.Play(c.Slot * 2 - 1, source, transition, frames, seek, config["Media:PublishSecret"], await Resolve(db, c, source, ct)), ct);
        c.ProgramId = source.Id;
        c.MediaLeaseRefreshAt = source.Kind == SourceKind.S3 ? DateTimeOffset.UtcNow.AddDays(6) : null;
        c.ProgramSince = DateTimeOffset.UtcNow.AddSeconds(-seek);
        c.Status = reason;
        c.Error = null;
        db.Events.Add(new() { ChannelId = c.Id, Message = $"{reason}: {source.Name}" });
    }
    private async Task<string?> Resolve(BroadcastDb db, Channel c, Source source, CancellationToken ct)
    {
        if (source.Kind != SourceKind.S3) return null;
        var connection = await db.StorageConnections.SingleOrDefaultAsync(x => x.ChannelId == c.Id, ct) ?? throw new ConflictException("Подключение S3 удалено");
        return await storage.SignedUrl(connection, source.Locator, ct);
    }
    public Task Graphics(Channel c, bool on, CancellationToken ct)
        => amcp.Send(on ? Amcp.Graphics(c.Slot * 2 - 1, c.GraphicsJson) : $"CG {c.Slot * 2 - 1}-20 REMOVE 1", ct);
}
