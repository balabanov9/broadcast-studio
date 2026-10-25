using System.Collections.Concurrent;
using System.Diagnostics;
using Broadcast.Api.Data;
using Broadcast.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Broadcast.Api.Services;
public sealed class Slates(IConfiguration cfg, IDbContextFactory<BroadcastDb> factory, ILogger<Slates> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, (Process Process, string Image)> processes = new();
    public string Image(Channel c) => c.RestrictionImage is null ? cfg["Restriction:DefaultImage"] ?? "/app/graphics/region-unavailable.png" : Path.Combine(cfg["Media:Directory"] ?? "/media", c.RestrictionImage);
    public bool Running(Guid id) => processes.TryGetValue(id, out var value) && !value.Process.HasExited;
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                var channels = await db.Channels.AsNoTracking().Where(x => x.RegionRestrictionEnabled).ToListAsync(ct);
                foreach (var c in channels)
                {
                    try { Ensure(c); } catch (Exception e) { logger.LogError(e, "Не удалось запустить региональную заставку канала {Канал}", c.Name); }
                }
                foreach (var id in processes.Keys.Where(id => channels.All(c => c.Id != id))) Stop(id);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { foreach (var id in processes.Keys) Stop(id); }
    }
    private void Ensure(Channel c)
    {
        var image = Image(c);
        if (processes.TryGetValue(c.Id, out var existing) && !existing.Process.HasExited && existing.Image == image) return;
        Stop(c.Id);
        if (!File.Exists(image)) throw new IOException("Файл заставки не найден");
        var process = new Process { StartInfo = new("ffmpeg") { UseShellExecute = false, RedirectStandardError = true } };
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "warning", "-nostdin", "-re", "-loop", "1", "-framerate", "25", "-i", image, "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo", "-vf", "scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,format=yuv420p", "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency", "-b:v", "1500k", "-g", "50", "-c:a", "aac", "-b:a", "96k", "-f", "flv", $"rtmp://mediamtx:1935/slate/{c.Slug}?user=engine&pass={Uri.EscapeDataString(cfg["Media:PublishSecret"]!)}" }) process.StartInfo.ArgumentList.Add(arg);
        // Не записываем stderr FFmpeg: он может содержать URL с паролем публикации.
        process.ErrorDataReceived += (_, _) => { };
        process.Start(); process.BeginErrorReadLine();
        processes[c.Id] = (process, image);
    }
    private void Stop(Guid id)
    {
        if (!processes.TryRemove(id, out var item)) return;
        try { if (!item.Process.HasExited) item.Process.Kill(true); } finally { item.Process.Dispose(); }
    }
}
