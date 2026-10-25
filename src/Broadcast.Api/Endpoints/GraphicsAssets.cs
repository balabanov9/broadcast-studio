using System.Diagnostics;
using System.Text.Json;
using Broadcast.Api.Data;
using Broadcast.Api.Services;
using Microsoft.EntityFrameworkCore;
namespace Broadcast.Api.Endpoints;
public static class GraphicsAssets
{
    public static void MapGraphicsAssets(this WebApplication app)
    {
        var api = app.MapGroup("/api/channels/{id:guid}/graphics/assets").RequireAuthorization();
        api.MapPost("", async (Guid id, IFormFile file, BroadcastDb db, IConfiguration cfg, CancellationToken ct) =>
        {
            if (!await db.Channels.AnyAsync(x => x.Id == id, ct)) throw new KeyNotFoundException("Канал не найден");
            if (file.Length is <= 0 or > 5_000_000 || Path.GetExtension(file.FileName).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".webp")) throw new ArgumentException("Выберите PNG, JPEG или WebP до 5 МБ. Для прозрачного фона используйте PNG.");
            var directory = Path.Combine(cfg["Media:Directory"] ?? "/media", "logos", id.ToString("N"));
            Directory.CreateDirectory(directory);
            var imageId = Guid.NewGuid().ToString("N");
            var temporary = Path.Combine(directory, imageId + ".upload");
            var output = Path.Combine(directory, imageId + ".png");
            try
            {
                await using (var stream = File.Create(temporary)) await file.CopyToAsync(stream, ct);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using var p = new Process { StartInfo = new("ffmpeg") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true } };
                foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-max_alloc", "134217728", "-protocol_whitelist", "file,pipe", "-i", temporary, "-frames:v", "1", "-vf", "scale=1024:1024:force_original_aspect_ratio=decrease,format=rgba", "-update", "1", output }) p.StartInfo.ArgumentList.Add(arg);
                p.Start();
                try
                {
                    var stderr = p.StandardError.ReadToEndAsync(timeout.Token); var stdout = p.StandardOutput.ReadToEndAsync(timeout.Token);
                    await p.WaitForExitAsync(timeout.Token); await stderr; await stdout;
                    if (p.ExitCode != 0 || !File.Exists(output)) throw new ArgumentException("Не удалось открыть картинку. Попробуйте другой PNG.");
                }
                finally { if (!p.HasExited) p.Kill(true); }
                // Размер PNG читается из IHDR: декодирование и нормализация уже выполнены FFmpeg.
                var header = new byte[24];
                await using (var stream = File.OpenRead(output)) await stream.ReadExactlyAsync(header, ct);
                var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
                var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
                return Results.Ok(new { imageId, width, height });
            }
            catch { File.Delete(output); throw; }
            finally { File.Delete(temporary); }
        }).DisableAntiforgery();
        api.MapGet("/{imageId}", async (Guid id, string imageId, BroadcastDb db, IConfiguration cfg) =>
        {
            if (!Guid.TryParseExact(imageId, "N", out _) || !await db.Channels.AnyAsync(x => x.Id == id)) return Results.NotFound();
            var path = Path.Combine(cfg["Media:Directory"] ?? "/media", "logos", id.ToString("N"), imageId + ".png");
            return File.Exists(path) ? Results.File(path, "image/png") : Results.NotFound();
        });
    }
}
