using System.Collections.Concurrent;
using System.Text.Json;
using Broadcast.Api.Data;
using Broadcast.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Broadcast.Api.Services;
public sealed class AutopilotWorker(IDbContextFactory<BroadcastDb> factory, Control control, IHttpClientFactory http, ILogger<AutopilotWorker> log) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, int> misses = new();
    private readonly ConcurrentDictionary<Guid, string> cursors = new();
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await Tick(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { log.LogError(e, "Ошибка цикла контроля эфира"); }
        }
    }
    private async Task Tick(CancellationToken ct)
    {
        await using var read = await factory.CreateDbContextAsync(ct);
        var channels = await read.Channels.AsNoTracking().ToListAsync(ct);
        HashSet<string>? ready = null;
        try
        {
            var response = await http.CreateClient("mediamtx").GetFromJsonAsync<JsonElement>("v3/paths/list?itemsPerPage=1000", ct);
            ready = response.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("ready").GetBoolean()).Select(x => x.GetProperty("name").GetString()!).ToHashSet();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException) { log.LogWarning("MediaMTX недоступен: {Ошибка}", e.Message); }
        foreach (var item in channels)
        {
            try
            {
                await control.Run(item.Id, async (db, c) =>
                {
                    var now = DateTimeOffset.UtcNow;
                    // SQLite используется только для локальных проверок; сравнение времени выполняется в памяти.
                    var blocks = await db.Schedule.Where(x => x.ChannelId == c.Id).ToListAsync(ct);
                    var block = blocks.SingleOrDefault(x => x.StartsAt <= now && x.EndsAt > now);
                    Source? desired = null;
                    double offset = 0;
                    var cursor = "";
                    if (c.Autopilot)
                    {
                        if (block?.PlaylistId is { } pid)
                        {
                            var entries = await db.PlaylistItems.Where(x => x.PlaylistId == pid).ToListAsync(ct);
                            var elapsed = (now - block.StartsAt).TotalSeconds;
                            var selection = Timeline.Select(entries, elapsed);
                            if (selection is not null)
                            {
                                desired = await Control.OwnedSource(db, c, selection.SourceId);
                                offset = selection.Offset;
                                cursor = $"{block.Id}:{(long)(elapsed / entries.Sum(x => x.DurationSeconds))}:{selection.Position}";
                            }
                        }
                        else if (block?.SourceId is { } sid) { desired = await Control.OwnedSource(db, c, sid); offset = (now - block.StartsAt).TotalSeconds; cursor = block.Id.ToString(); }
                        if (desired is null && c.FallbackId is { } fallback) { desired = await Control.OwnedSource(db, c, fallback); cursor = "fallback"; }
                    }
                    var current = c.ProgramId is { } currentId ? await Control.OwnedSource(db, c, currentId) : null;
                    if (!c.Autopilot && current is not null && ready is not null && !ready.Contains($"program/{c.Slug}"))
                    {
                        desired = current;
                        offset = c.ProgramSince is { } since ? (now - since).TotalSeconds : 0;
                        cursor = "recover-manual";
                    }
                    var observed = desired ?? current;
                    if (observed?.Kind == SourceKind.Live && ready is not null)
                    {
                        var available = ready.Contains($"ingest/{c.Id:N}");
                        misses[c.Id] = available ? 0 : misses.GetValueOrDefault(c.Id) + 1;
                        if (misses[c.Id] >= 3 && c.FallbackId is { } fid)
                        { desired = await Control.OwnedSource(db, c, fid); offset = 0; cursor = "fallback-live"; }
                    }
                    var renewS3 = current?.Kind == SourceKind.S3 && c.MediaLeaseRefreshAt <= now;
                    if (renewS3 && desired is null) { desired = current; offset = c.ProgramSince is { } since ? (now - since).TotalSeconds : 0; cursor = "renew-s3"; }
                    if (desired is null) return;
                    var outputMissing = ready is not null && !ready.Contains($"program/{c.Slug}");
                    if (outputMissing) control.Disconnect(c.Id);
                    if (c.ProgramId != desired.Id || !cursors.TryGetValue(c.Id, out var old) || old != cursor || outputMissing || renewS3 || (c.Autopilot && c.ActiveBlockId != block?.Id))
                    {
                        var seek = desired.Kind is SourceKind.Clip or SourceKind.S3 && desired.DurationSeconds > 0 ? offset % desired.DurationSeconds : 0;
                        await control.Play(db, c, desired, "CUT", 0, cursor.StartsWith("fallback") ? "Резервный источник" : "Автопилот", ct, seek);
                        if (c.GraphicsOnAir) await control.Graphics(c, true, ct);
                        c.ActiveBlockId = block?.Id;
                        cursors[c.Id] = cursor;
                    }
                }, ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                control.Disconnect(item.Id);
                log.LogWarning(e, "Не удалось управлять каналом {Канал}", item.Name);
                // Ошибка сохраняется отдельно: не подтверждаем неисполненное переключение.
                await control.Run(item.Id, (db, c) => { c.Error = "Медиадвижок недоступен или отклонил команду"; return Task.CompletedTask; }, ct);
            }
        }
    }
}
