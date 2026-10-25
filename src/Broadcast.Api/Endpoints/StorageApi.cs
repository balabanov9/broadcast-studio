using System.Text.Json;
using System.Text.RegularExpressions;
using Broadcast.Api.Data;
using Broadcast.Api.Domain;
using Broadcast.Api.Infrastructure;
using Broadcast.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Broadcast.Api.Endpoints;
public sealed record StorageInput(string Endpoint, string Bucket, string Region, string AccessKey, string SecretKey, string? SessionToken, bool PathStyle = true);
public sealed record ImportInput(string Key);
public sealed record DirectInput(string Key, double DurationSeconds);
public static partial class StorageApi
{
    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$")] private static partial Regex BucketPattern();
    [GeneratedRegex("^[a-z0-9-]{1,50}$")] private static partial Regex RegionPattern();
    public static void MapStorage(this WebApplication app)
    {
        app.MapGet("/api/storage/providers", () => Results.Ok(StoragePresets.All)).RequireAuthorization();
        var api = app.MapGroup("/api/channels/{id:guid}/storage").RequireAuthorization();
        api.MapGet("", async (Guid id, BroadcastDb db) =>
        {
            if (!await db.Channels.AnyAsync(x => x.Id == id)) throw new KeyNotFoundException("Канал не найден");
            var c = await db.StorageConnections.SingleOrDefaultAsync(x => x.ChannelId == id);
            return Results.Ok(c is null ? null : new { c.Endpoint, c.Bucket, c.Region, c.PathStyle, configured = true });
        });
        api.MapPut("", async (Guid id, StorageInput input, IDataProtectionProvider protection, Control control, CancellationToken ct) =>
        {
            if (!Uri.TryCreate(input.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https" || endpoint.UserInfo.Length > 0 || endpoint.AbsolutePath != "/" || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0) throw new ArgumentException("S3 endpoint: только HTTPS-адрес без пути, логина и параметров");
            if (System.Net.IPAddress.TryParse(endpoint.Host, out var address) && !S3Signing.PublicAddress(address)) throw new ArgumentException("Внутренние адреса S3 запрещены");
            if (!BucketPattern().IsMatch(input.Bucket) || input.Bucket.Contains("..") || !RegionPattern().IsMatch(input.Region)) throw new ArgumentException("Некорректный bucket или регион S3");
            await control.Run(id, async (db, c) =>
            {
                var saved = await db.StorageConnections.SingleOrDefaultAsync(x => x.ChannelId == id, ct);
                if (saved is null) { saved = new() { ChannelId = id }; db.Add(saved); }
                if (input.SecretKey.Length > 0)
                {
                    if (input.AccessKey.Length is < 1 or > 200 || input.SecretKey.Length > 500 || (input.SessionToken?.Length ?? 0) > 5000 || (input.AccessKey + input.SecretKey + input.SessionToken).Any(char.IsControl)) throw new ArgumentException("Некорректные ключи S3");
                    saved.ProtectedCredentials = protection.CreateProtector("Lumiere.S3.Credentials.v1").Protect(JsonSerializer.Serialize(new S3Credentials(input.AccessKey, input.SecretKey, input.SessionToken)));
                }
                if (saved.ProtectedCredentials.Length == 0) throw new ArgumentException("Для первого подключения укажите Access Key и Secret Key");
                saved.Endpoint = endpoint.GetLeftPart(UriPartial.Authority); saved.Bucket = input.Bucket; saved.Region = input.Region; saved.PathStyle = input.PathStyle;
            }, ct);
            return Results.Ok();
        });
        api.MapGet("/objects", async (Guid id, string? prefix, string? token, BroadcastDb db, IS3Storage storage, CancellationToken ct) =>
        {
            if ((prefix?.Length ?? 0) > 1024 || (token?.Length ?? 0) > 4000) throw new ArgumentException("Префикс или токен слишком длинный");
            var c = await db.StorageConnections.SingleOrDefaultAsync(x => x.ChannelId == id, ct) ?? throw new ConflictException("Сначала подключите S3");
            return Results.Ok(await storage.List(c, prefix ?? "", token, ct));
        });
        api.MapPost("/direct", async (Guid id, DirectInput input, IS3Storage storage, Control control, CancellationToken ct) =>
        {
            if (input.Key.Length is < 1 or > 1024 || input.Key.Any(char.IsControl) || input.Key.Split('/').Any(x => x is "." or "..") || Path.GetExtension(input.Key).ToLowerInvariant() is not (".mp4" or ".mov" or ".mkv" or ".webm")) throw new ArgumentException("Выберите видео MP4, MOV, MKV или WebM");
            if (!double.IsFinite(input.DurationSeconds) || input.DurationSeconds is < 1 or > 86400) throw new ArgumentException("Укажите длительность видео: 1–86400 секунд");
            await control.Run(id, async (db, c) =>
            {
                var connection = await db.StorageConnections.SingleOrDefaultAsync(x => x.ChannelId == id, ct) ?? throw new ConflictException("Сначала подключите S3");
                // Проверяем существование объекта без сохранения тела ответа на диск.
                using var response = await storage.Download(connection, input.Key, ct);
                var display = Path.GetFileName(input.Key);
                db.Sources.Add(new() { ChannelId = id, Name = display[..Math.Min(display.Length, 100)], Kind = SourceKind.S3, Locator = input.Key, DurationSeconds = input.DurationSeconds });
                db.Events.Add(new() { ChannelId = id, Message = "Добавлен прямой источник S3 без загрузки на диск" });
            }, ct);
            return Results.Ok();
        });
        api.MapPost("/imports", async (Guid id, ImportInput input, Control control, CancellationToken ct) =>
        {
            if (input.Key.Length is < 1 or > 1024 || input.Key.Any(char.IsControl) || input.Key.Split('/').Any(x => x is "." or "..") || Path.GetExtension(input.Key).ToLowerInvariant() is not (".mp4" or ".mov" or ".mkv" or ".webm")) throw new ArgumentException("Укажите ключ видео MP4, MOV, MKV или WebM");
            var job = new StorageImport { ChannelId = id, ObjectKey = input.Key };
            await control.Run(id, async (db, c) =>
            {
                if (await db.Imports.CountAsync(x => x.ChannelId == id && (x.Status == "Queued" || x.Status == "Downloading"), ct) >= 20) throw new ConflictException("В очереди уже 20 заданий");
                var connection = await db.StorageConnections.SingleOrDefaultAsync(x => x.ChannelId == id, ct) ?? throw new ConflictException("Сначала подключите S3");
                job.ConnectionSnapshot = JsonSerializer.Serialize(connection); db.Imports.Add(job);
            }, ct);
            return Results.Accepted($"/api/channels/{id}/storage/imports", new { job.Id, job.Status });
        });
        api.MapGet("/imports", async (Guid id, BroadcastDb db) =>
        {
            var jobs = await db.Imports.Where(x => x.ChannelId == id).ToListAsync();
            return Results.Ok(jobs.OrderByDescending(x => x.CreatedAt).Take(30).Select(x => new { x.Id, x.ObjectKey, x.Status, x.BytesReceived, x.TotalBytes, x.SourceId, x.Error, x.CreatedAt }));
        });
    }
}
