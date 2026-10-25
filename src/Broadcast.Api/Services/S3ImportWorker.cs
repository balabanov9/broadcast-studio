using System.Text.Json;
using Broadcast.Api.Data;
using Broadcast.Api.Domain;
using Broadcast.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Broadcast.Api.Services;
public sealed class S3ImportWorker(IDbContextFactory<BroadcastDb> factory, IS3Storage storage, Control control, IConfiguration cfg, ILogger<S3ImportWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Один импортирующий процесс. Незавершённые задания после перезапуска ставятся в очередь заново.
        await using (var db = await factory.CreateDbContextAsync(ct))
        { foreach (var job in await db.Imports.Where(x => x.Status == "Downloading").ToListAsync(ct)) job.Status = "Queued"; await db.SaveChangesAsync(ct); }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                var jobs = await db.Imports.AsNoTracking().Where(x => x.Status == "Queued").ToListAsync(ct);
                var job = jobs.OrderBy(x => x.CreatedAt).FirstOrDefault();
                if (job is not null) await Import(job.Id, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception e) { log.LogError(e, "Ошибка фонового импорта S3"); }
        }
    }
    public async Task Import(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var job = await db.Imports.SingleAsync(x => x.Id == id, ct);
        var directory = cfg["Media:Directory"] ?? "/media"; Directory.CreateDirectory(directory);
        var name = job.Id.ToString("N") + Path.GetExtension(job.ObjectKey).ToLowerInvariant();
        var path = Path.Combine(directory, name); var partial = path + ".part";
        try
        {
            job.Status = "Downloading"; job.Error = null; job.BytesReceived = 0; await db.SaveChangesAsync(ct);
            var connection = JsonSerializer.Deserialize<StorageConnection>(job.ConnectionSnapshot)!;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(60));
            using var response = await storage.Download(connection, job.ObjectKey, timeout.Token);
            job.TotalBytes = response.Content.Headers.ContentLength;
            if (job.TotalBytes > 2_000_000_000) throw new ArgumentException("Файл S3 превышает 2 ГБ");
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = File.Create(partial))
            {
                var buffer = new byte[128 * 1024]; var lastUpdate = DateTimeOffset.UtcNow;
                int count;
                while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    job.BytesReceived += count;
                    if (job.BytesReceived > 2_000_000_000) throw new ArgumentException("Файл S3 превышает 2 ГБ");
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                    if ((DateTimeOffset.UtcNow - lastUpdate).TotalSeconds >= 1) { await db.SaveChangesAsync(timeout.Token); lastUpdate = DateTimeOffset.UtcNow; }
                }
            }
            if (job.TotalBytes is { } length && job.BytesReceived != length) throw new IOException("S3: файл получен не полностью");
            var duration = await MediaProbe.Duration(partial, timeout.Token);
            File.Move(partial, path, true);
            var sourceId = Guid.NewGuid();
            await control.Run(job.ChannelId, (airDb, c) =>
            {
                var display = Path.GetFileName(job.ObjectKey);
                airDb.Sources.Add(new() { Id = sourceId, ChannelId = c.Id, Name = display[..Math.Min(display.Length, 100)], Kind = SourceKind.Clip, Locator = name, DurationSeconds = duration });
                // Результат и источник фиксируются одной транзакцией EF.
                airDb.Imports.Attach(job); job.SourceId = sourceId; job.Status = "Ready";
                airDb.Entry(job).State = EntityState.Modified;
                airDb.Events.Add(new() { ChannelId = c.Id, Message = "Видео из S3 добавлено в медиатеку" });
                return Task.CompletedTask;
            }, ct);
            db.Entry(job).State = EntityState.Detached;
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            File.Delete(path); job.Status = "Failed"; job.Error = e is ArgumentException ? e.Message : "Импорт не завершён. Проверьте хранилище, доступ, место на диске и формат видео.";
            await db.SaveChangesAsync(ct);
        }
        finally { File.Delete(partial); }
    }
}
