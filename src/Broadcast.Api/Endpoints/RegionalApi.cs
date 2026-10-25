using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Broadcast.Api.Data;
using Broadcast.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Broadcast.Api.Endpoints;
public sealed record RegionInput(bool Enabled, string[] Rules, bool BlockUnknown, long Revision);
public static partial class RegionalApi
{
    [GeneratedRegex("^[A-Z]{2}(-[A-Z0-9]{1,3})?$")] private static partial Regex RulePattern();
    [GeneratedRegex("^[a-zA-Z0-9_.-]{1,160}$")] private static partial Regex MediaFile();
    public static void MapRegional(this WebApplication app)
    {
        var api = app.MapGroup("/api/channels/{id:guid}/restriction").RequireAuthorization();
        api.MapGet("", async (Guid id, BroadcastDb db, IGeography geo, Slates slates) =>
        {
            var c = await db.Channels.FindAsync(id) ?? throw new KeyNotFoundException("Канал не найден");
            return Results.Ok(new { enabled = c.RegionRestrictionEnabled, rules = JsonSerializer.Deserialize<string[]>(c.BlockedCountriesJson), blockUnknown = c.BlockUnknownCountry, customImage = c.RestrictionImage is not null, geoAvailable = geo.Available, regionsAvailable = geo.SupportsRegions, slateRunning = slates.Running(c.Id) });
        });
        api.MapPut("", async (Guid id, RegionInput input, IGeography geo, Control control, CancellationToken ct) =>
        {
            if (input.Rules.Length > 250) throw new ArgumentException("Допускается до 250 правил");
            var rules = input.Rules.Select(x => x.Trim().ToUpperInvariant()).Distinct().ToArray();
            var countries = CultureInfo.GetCultures(CultureTypes.SpecificCultures).Select(x => new RegionInfo(x.Name).TwoLetterISORegionName).ToHashSet();
            if (rules.Any(x => !RulePattern().IsMatch(x) || !countries.Contains(x[..2]))) throw new ArgumentException("Используйте коды ISO 3166: RU, NL или код региона из GeoIP City");
            if (input.Enabled && !geo.Available) throw new ConflictException("Сначала подключите базу GeoIP и перезапустите API");
            if (input.Enabled && rules.Length == 0) throw new ArgumentException("Укажите хотя бы один запрещённый регион");
            if (rules.Any(x => x.Contains('-')) && !geo.SupportsRegions) throw new ConflictException("Для областей и регионов требуется база GeoIP City");
            await control.Run(id, (db, c) =>
            {
                Control.Check(c, input.Revision);
                c.RegionRestrictionEnabled = input.Enabled; c.BlockedCountriesJson = JsonSerializer.Serialize(rules); c.BlockUnknownCountry = input.BlockUnknown;
                db.Events.Add(new() { ChannelId = id, Message = input.Enabled ? "Региональные ограничения IPTV включены" : "Региональные ограничения IPTV выключены" });
                return Task.CompletedTask;
            }, ct);
            return Results.Ok();
        });
        api.MapGet("/image", async (Guid id, BroadcastDb db, Slates slates) =>
        {
            var c = await db.Channels.FindAsync(id) ?? throw new KeyNotFoundException("Канал не найден");
            return Results.File(slates.Image(c), "image/png");
        });
        api.MapPost("/image", async (Guid id, IFormFile file, Control control, IConfiguration cfg, CancellationToken ct) =>
        {
            if (file.Length is <= 0 or > 8_000_000 || Path.GetExtension(file.FileName).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg")) throw new ArgumentException("Загрузите PNG или JPEG до 8 МБ");
            var directory = cfg["Media:Directory"] ?? "/media";
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".upload");
            var name = Guid.NewGuid().ToString("N") + ".png";
            var output = Path.Combine(directory, name);
            try
            {
                await using (var stream = File.Create(temporary)) await file.CopyToAsync(stream, ct);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using var p = new Process { StartInfo = new("ffmpeg") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true } };
                foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-max_alloc", "134217728", "-protocol_whitelist", "file,pipe", "-i", temporary, "-frames:v", "1", "-vf", "scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2", "-update", "1", output }) p.StartInfo.ArgumentList.Add(arg);
                p.Start();
                try { var err = p.StandardError.ReadToEndAsync(timeout.Token); var stdout = p.StandardOutput.ReadToEndAsync(timeout.Token); await p.WaitForExitAsync(timeout.Token); await err; await stdout; if (p.ExitCode != 0 || !File.Exists(output)) throw new ArgumentException("Не удалось обработать изображение"); }
                finally { if (!p.HasExited) p.Kill(true); }
                await control.Run(id, (db, c) => { c.RestrictionImage = name; return Task.CompletedTask; }, ct);
                return Results.Ok();
            }
            catch { File.Delete(output); throw; }
            finally { File.Delete(temporary); }
        }).DisableAntiforgery();
        api.MapDelete("/image", async (Guid id, Control control, CancellationToken ct) =>
        { await control.Run(id, (db, c) => { c.RestrictionImage = null; return Task.CompletedTask; }, ct); return Results.NoContent(); });
        app.MapGet("/hls/{kind}/{slug}/{file}", Proxy);
    }
    private static async Task Proxy(string kind, string slug, string file, HttpContext ctx, BroadcastDb db, IGeography geo, IHttpClientFactory clients)
    {
        if (kind is not ("program" or "preview") || !MediaFile().IsMatch(file) || file.Contains("..")) { ctx.Response.StatusCode = 404; return; }
        var c = await db.Channels.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug, ctx.RequestAborted);
        if (c is null) { ctx.Response.StatusCode = 404; return; }
        if (kind == "preview" && ctx.User.Identity?.IsAuthenticated != true) { ctx.Response.StatusCode = 401; return; }
        var restricted = kind == "program" && c.RegionRestrictionEnabled && (!geo.Available || RegionalPolicy.Blocked(c, geo.Locate(ctx.Connection.RemoteIpAddress)));
        var path = $"{(restricted ? "slate" : kind)}/{c.Slug}/{file}";
        var query = ctx.Request.Query.Where(x => x.Key is "_HLS_msn" or "_HLS_part" or "_HLS_skip").Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value.ToString())}");
        using var request = new HttpRequestMessage(HttpMethod.Get, path + "?" + string.Join('&', query));
        var client = clients.CreateClient("hls");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
        // Каждый манифест и каждый сегмент проходят одну проверку. Прямого публичного входа в MediaMTX нет.
        ctx.Response.Headers.CacheControl = "private, no-store";
        ctx.Response.Headers.XContentTypeOptions = "nosniff";
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound && restricted) { ctx.Response.StatusCode = 503; ctx.Response.Headers.RetryAfter = "3"; return; }
        ctx.Response.StatusCode = (int)response.StatusCode;
        ctx.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        if (response.Headers.Location is { } location)
        {
            if (location.IsAbsoluteUri || location.ToString().StartsWith('/')) { ctx.Response.StatusCode = 502; return; }
            ctx.Response.Headers.Location = location.ToString();
        }
        await response.Content.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
    }
}
