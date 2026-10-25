using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Broadcast.Api.Data;
using Broadcast.Api.Domain;
using Broadcast.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Broadcast.Api.Endpoints;
public sealed record ChannelInput(string Name, string Slug);
public sealed record SourceInput(string Name, SourceKind Kind, string Locator);
public sealed record PreviewInput(Guid SourceId, long Revision);
public sealed record TakeInput(string Transition, int Frames, long Revision);
public sealed record AutoInput(bool Enabled, Guid FallbackId, long Revision);
public sealed record PlaylistEntry(Guid SourceId, double DurationSeconds);
public sealed record PlaylistInput(string Name, List<PlaylistEntry> Items);
public sealed record BlockInput(string Title, Guid? SourceId, Guid? PlaylistId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public sealed record GraphicsInput(GraphicScene Scene, long Revision);
public sealed record ToggleInput(bool Enabled, long Revision);
public sealed record LoginInput(string Password);
public sealed record PasswordInput(string CurrentPassword, string NewPassword);
public sealed record MediaAuth(string Action, string Path, string User, string Password);
public static partial class Api
{
    [GeneratedRegex("^[a-z][a-z0-9-]{1,39}$")] private static partial Regex SlugRegex();
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")] private static partial Regex ColorRegex();
    public static bool SecretEquals(string a, string b) => CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
    public static void Name(string value, int max = 100)
    { if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(char.IsControl)) throw new ArgumentException("Некорректное название"); }
    public static void MapBroadcast(this WebApplication app)
    {
        app.MapPost("/api/login", async (LoginInput body, HttpContext context, BroadcastDb db, IPasswordHasher<OperatorAccount> hasher) =>
        {
            var account = await db.Operators.SingleAsync();
            if (body.Password.Length > 1024 || hasher.VerifyHashedPassword(account, account.PasswordHash, body.Password) == PasswordVerificationResult.Failed) return Results.Unauthorized();
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Оператор"), new Claim("version", account.SecurityVersion.ToString(CultureInfo.InvariantCulture))], CookieAuthenticationDefaults.AuthenticationScheme)));
            return Results.Ok(new { name = "Оператор" });
        }).RequireRateLimiting("login");
        app.MapPost("/api/password", async (PasswordInput body, HttpContext ctx, BroadcastDb db, IPasswordHasher<OperatorAccount> hasher) =>
        {
            if (body.NewPassword.Length is < 12 or > 128) throw new ArgumentException("Новый пароль: 12–128 символов");
            var account = await db.Operators.SingleAsync();
            if (body.CurrentPassword.Length > 1024 || hasher.VerifyHashedPassword(account, account.PasswordHash, body.CurrentPassword) == PasswordVerificationResult.Failed) return Results.Unauthorized();
            account.PasswordHash = hasher.HashPassword(account, body.NewPassword); account.SecurityVersion++;
            await db.SaveChangesAsync(); await ctx.SignOutAsync();
            return Results.Ok();
        }).RequireAuthorization().RequireRateLimiting("login");
        app.MapPost("/api/logout", async (HttpContext ctx) => { await ctx.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization();
        app.MapGet("/api/session", () => Results.Ok(new { name = "Оператор" })).RequireAuthorization();
        app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/health/ready", async (BroadcastDb db, CancellationToken ct) => await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503));
        app.MapPost("/internal/media-auth", async (MediaAuth body, string? secret, IConfiguration cfg, BroadcastDb db) =>
        {
            if (!SecretEquals(secret ?? "", cfg["Media:HookSecret"]!)) return Results.Unauthorized();
            if (body.Action == "api") return body.User == "engine" && SecretEquals(body.Password, cfg["Media:PublishSecret"]!) ? Results.Ok() : Results.Unauthorized();
            if (body.Action == "read") return body.User == "engine" && SecretEquals(body.Password, cfg["Media:PublishSecret"]!) ? Results.Ok() : Results.Unauthorized();
            if (body.Action != "publish") return Results.Unauthorized();
            if (body.User == "engine" && SecretEquals(body.Password, cfg["Media:PublishSecret"]!) && (body.Path.StartsWith("program/") || body.Path.StartsWith("preview/") || body.Path.StartsWith("slate/"))) return Results.Ok();
            if (body.Path.StartsWith("ingest/") && Guid.TryParseExact(body.Path[7..], "N", out var id))
            {
                var c = await db.Channels.FindAsync(id);
                if (c is not null && SecretEquals(body.Password, c.IngestKey)) return Results.Ok();
            }
            return Results.Unauthorized();
        });
        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/channels", async (BroadcastDb db) => await db.Channels.OrderBy(x => x.Slot).ToListAsync());
        api.MapPost("/channels", async (ChannelInput input, BroadcastDb db) =>
        {
            Name(input.Name, 80);
            if (!SlugRegex().IsMatch(input.Slug)) throw new ArgumentException("Slug: 2–40 латинских букв, цифр и дефисов");
            var slots = await db.Channels.Select(x => x.Slot).ToListAsync();
            var slot = Enumerable.Range(1, 4).Except(slots).FirstOrDefault();
            if (slot == 0) throw new ConflictException("В MVP доступны четыре канала");
            if (await db.Channels.AnyAsync(x => x.Slug == input.Slug)) throw new ConflictException("Slug уже занят");
            var c = new Channel { Name = input.Name.Trim(), Slug = input.Slug, Slot = slot };
            var fallback = new Source { ChannelId = c.Id, Name = "Резерв · чёрное поле", Kind = SourceKind.Color, Locator = "#000000" };
            c.FallbackId = fallback.Id;
            db.AddRange(c, fallback, new Source { ChannelId = c.Id, Name = "OBS · прямой эфир", Kind = SourceKind.Live });
            await db.SaveChangesAsync();
            return Results.Created($"/api/channels/{c.Id}", c);
        });
        api.MapGet("/channels/{id:guid}", async (Guid id, BroadcastDb db) =>
        {
            var c = await db.Channels.FindAsync(id) ?? throw new KeyNotFoundException("Канал не найден");
            var blocks = await db.Schedule.Where(x => x.ChannelId == id).ToListAsync();
            var events = await db.Events.Where(x => x.ChannelId == id).OrderByDescending(x => x.Id).Take(30).ToListAsync();
            return Results.Ok(new { channel = c, sources = await db.Sources.Where(x => x.ChannelId == id).ToListAsync(), playlists = await db.Playlists.Include(x => x.Items).Where(x => x.ChannelId == id).ToListAsync(), schedule = blocks.OrderBy(x => x.StartsAt), events });
        });
        api.MapPost("/channels/{id:guid}/sources", async (Guid id, SourceInput input, Control control, CancellationToken ct) =>
        {
            Name(input.Name);
            if (input.Kind != SourceKind.Color || !ColorRegex().IsMatch(input.Locator)) throw new ArgumentException("Через эту форму создаются цветовые источники. Видео загрузите файлом; OBS уже создан.");
            await control.Run(id, (db, c) => { db.Sources.Add(new() { ChannelId = c.Id, Name = input.Name, Kind = input.Kind, Locator = input.Locator }); return Task.CompletedTask; }, ct);
            return Results.Ok();
        });
        api.MapPost("/channels/{id:guid}/media", Upload).DisableAntiforgery();
        api.MapPost("/channels/{id:guid}/preview", async (Guid id, PreviewInput input, Control control, CancellationToken ct) =>
        {
            await control.Run(id, async (db, c) => { Control.Check(c, input.Revision); await control.Preview(db, c, input.SourceId, ct); }, ct);
            return Results.Ok();
        });
        api.MapPost("/channels/{id:guid}/take", async (Guid id, TakeInput input, Control control, CancellationToken ct) =>
        {
            if (input.Transition is not ("CUT" or "MIX") || input.Frames is < 0 or > 100) throw new ArgumentException("Переход CUT/MIX, длительность 0–100 кадров");
            await control.Run(id, async (db, c) =>
            {
                Control.Check(c, input.Revision);
                var source = await Control.OwnedSource(db, c, c.PreviewId ?? throw new ConflictException("Выберите источник Preview"));
                await control.Play(db, c, source, input.Transition, input.Frames, "Ручное управление", ct);
                c.Autopilot = false;
                c.ActiveBlockId = null;
            }, ct);
            return Results.Ok();
        });
        api.MapPost("/channels/{id:guid}/autopilot", async (Guid id, AutoInput input, Control control, CancellationToken ct) =>
        {
            await control.Run(id, async (db, c) =>
            {
                Control.Check(c, input.Revision);
                var fallback = await Control.OwnedSource(db, c, input.FallbackId);
                if (fallback.Kind == SourceKind.Live) throw new ArgumentException("Резервом должен быть файл или цветовое поле");
                c.FallbackId = fallback.Id;
                c.Autopilot = input.Enabled;
                db.Events.Add(new() { ChannelId = c.Id, Message = input.Enabled ? "Автопилот включён" : "Автопилот отключён" });
            }, ct);
            return Results.Ok();
        });
        api.MapPost("/channels/{id:guid}/playlists", async (Guid id, PlaylistInput input, Control control, CancellationToken ct) =>
        {
            Name(input.Name);
            if (input.Items.Count is < 1 or > 100) throw new ArgumentException("Плейлист: 1–100 позиций");
            await control.Run(id, async (db, c) =>
            {
                var list = new Playlist { ChannelId = id, Name = input.Name };
                for (var i = 0; i < input.Items.Count; i++)
                {
                    var entry = input.Items[i];
                    await Control.OwnedSource(db, c, entry.SourceId);
                    if (!double.IsFinite(entry.DurationSeconds) || entry.DurationSeconds is < 1 or > 86400) throw new ArgumentException("Длительность позиции: 1–86400 секунд");
                    list.Items.Add(new() { SourceId = entry.SourceId, Position = i, DurationSeconds = entry.DurationSeconds });
                }
                db.Playlists.Add(list);
            }, ct);
            return Results.Ok();
        });
        api.MapPost("/channels/{id:guid}/schedule", async (Guid id, BlockInput input, Control control, CancellationToken ct) =>
        {
            Name(input.Title, 140);
            var duration = input.EndsAt - input.StartsAt;
            if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(24)) throw new ArgumentException("Длительность блока: от 1 секунды до 24 часов");
            if (input.SourceId.HasValue == input.PlaylistId.HasValue) throw new ArgumentException("Выберите источник или плейлист");
            await control.Run(id, async (db, c) =>
            {
                if (input.SourceId is { } sid) await Control.OwnedSource(db, c, sid);
                if (input.PlaylistId is { } pid && !await db.Playlists.AnyAsync(x => x.Id == pid && x.ChannelId == id, ct)) throw new ArgumentException("Плейлист не принадлежит каналу");
                var blocks = await db.Schedule.Where(x => x.ChannelId == id).ToListAsync(ct);
                if (blocks.Count >= 1000) throw new ConflictException("Достигнут лимит 1000 блоков; удалите старые записи");
                if (blocks.Any(x => x.StartsAt < input.EndsAt && x.EndsAt > input.StartsAt)) throw new ConflictException("Блок пересекается с существующим расписанием");
                db.Schedule.Add(new() { ChannelId = id, Title = input.Title, SourceId = input.SourceId, PlaylistId = input.PlaylistId, StartsAt = input.StartsAt.ToUniversalTime(), EndsAt = input.EndsAt.ToUniversalTime() });
            }, ct);
            return Results.Ok();
        });
        api.MapDelete("/channels/{id:guid}/schedule/{blockId:guid}", async (Guid id, Guid blockId, Control control, CancellationToken ct) =>
        {
            await control.Run(id, async (db, c) => { var b = await db.Schedule.SingleOrDefaultAsync(x => x.Id == blockId && x.ChannelId == id, ct) ?? throw new KeyNotFoundException("Блок не найден"); db.Remove(b); }, ct);
            return Results.NoContent();
        });
        api.MapPut("/channels/{id:guid}/graphics", async (Guid id, GraphicsInput input, Control control, CancellationToken ct) =>
        {
            ValidateScene(input.Scene);
            await control.Run(id, (db, c) => { Control.Check(c, input.Revision); c.GraphicsJson = Infrastructure.Amcp.Scene(input.Scene); return Task.CompletedTask; }, ct);
            return Results.Ok();
        });
        api.MapPost("/channels/{id:guid}/graphics", async (Guid id, ToggleInput input, Control control, CancellationToken ct) =>
        {
            await control.Run(id, async (db, c) => { Control.Check(c, input.Revision); await control.Ensure(c, ct); await control.Graphics(c, input.Enabled, ct); c.GraphicsOnAir = input.Enabled; }, ct);
            return Results.Ok();
        });
        app.MapGet("/iptv/playlist.m3u", async (BroadcastDb db, IConfiguration cfg) =>
        {
            var origin = cfg["PublicOrigin"]!.TrimEnd('/');
            var text = new StringBuilder($"#EXTM3U x-tvg-url=\"{origin}/iptv/epg.xml\"\n");
            foreach (var c in await db.Channels.OrderBy(x => x.Slot).ToListAsync()) text.Append($"#EXTINF:-1 tvg-id=\"{c.Slug}\" group-title=\"LUMIÈRE\",{c.Name.Replace(",", " ")}\n{origin}/hls/program/{c.Slug}/index.m3u8\n");
            return Results.Text(text.ToString(), "audio/x-mpegurl", Encoding.UTF8);
        });
        app.MapGet("/iptv/epg.xml", async (BroadcastDb db) =>
        {
            var tv = new XElement("tv", new XAttribute("generator-info-name", "LUMIÈRE"));
            var channels = await db.Channels.ToListAsync();
            foreach (var c in channels) tv.Add(new XElement("channel", new XAttribute("id", c.Slug), new XElement("display-name", c.Name)));
            var start = DateTimeOffset.UtcNow.AddDays(-1);
            var end = start.AddDays(8);
            foreach (var b in (await db.Schedule.ToListAsync()).Where(x => x.EndsAt > start && x.StartsAt < end).OrderBy(x => x.StartsAt))
            {
                var c = channels.Single(x => x.Id == b.ChannelId);
                tv.Add(new XElement("programme", new XAttribute("channel", c.Slug), new XAttribute("start", XmlTime(b.StartsAt)), new XAttribute("stop", XmlTime(b.EndsAt)), new XElement("title", new XAttribute("lang", "ru"), b.Title)));
            }
            return Results.Text(new XDocument(new XDeclaration("1.0", "utf-8", null), tv).ToString(), "application/xml", Encoding.UTF8);
        });
    }
    public static string XmlTime(DateTimeOffset time) => time.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + " +0000";
    public static void ValidateScene(GraphicScene scene)
    {
        if (scene.Elements.Count > 20 || scene.Elements.Select(x => x.Id).Distinct().Count() != scene.Elements.Count) throw new ArgumentException("До 20 слоёв с уникальными идентификаторами");
        foreach (var e in scene.Elements)
            if (e.Kind is not ("logo" or "title" or "ticker" or "subtitle") || e.Text.Length > 1000 || e.X < 0 || e.Y < 0 || e.Width < 20 || e.Height < 20 || e.X + e.Width > 1920 || e.Y + e.Height > 1080 || e.FontSize is < 12 or > 160 || !ColorRegex().IsMatch(e.Color) || !ColorRegex().IsMatch(e.Background)) throw new ArgumentException("Некорректные параметры графического слоя");
    }
    private static async Task<IResult> Upload(Guid id, IFormFile file, Control control, IConfiguration cfg, CancellationToken ct)
    {
        if (file.Length is <= 0 or > 2_000_000_000) throw new ArgumentException("Размер видео: до 2 ГБ");
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".mp4" or ".mov" or ".mkv" or ".webm")) throw new ArgumentException("Поддерживаются MP4, MOV, MKV, WebM");
        var directory = cfg["Media:Directory"] ?? "/media";
        Directory.CreateDirectory(directory);
        var name = Guid.NewGuid().ToString("N") + extension;
        var path = Path.Combine(directory, name);
        try
        {
            await using (var output = File.Create(path)) await file.CopyToAsync(output, ct);
            var duration = await MediaProbe.Duration(path, ct);
            await control.Run(id, (db, c) => { db.Sources.Add(new() { ChannelId = c.Id, Name = Path.GetFileName(file.FileName)[..Math.Min(Path.GetFileName(file.FileName).Length, 100)], Kind = SourceKind.Clip, Locator = name, DurationSeconds = duration }); return Task.CompletedTask; }, ct);
            return Results.Ok();
        }
        catch { File.Delete(path); throw; }
    }
}
