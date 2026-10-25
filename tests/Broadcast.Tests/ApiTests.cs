using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Broadcast.Api.Domain;
using Broadcast.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Broadcast.Api.Infrastructure;
using Broadcast.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Broadcast.Tests;
public sealed class TestAmcp : IAmcpClient
{
    public List<string> Commands { get; } = [];
    public bool Fail { get; set; }
    public Task<string> Send(string command, CancellationToken ct = default, bool allowMissing = false)
    {
        if (Fail) throw new IOException("Движок недоступен");
        Commands.Add(command); return Task.FromResult("202 OK");
    }
}
public sealed class TestGeography : IGeography
{
    public bool Available { get; set; } = true;
    public bool SupportsRegions => true;
    public GeoLocation Location { get; set; } = new("NL", "NL-NH");
    public GeoLocation Locate(IPAddress? ip) => Location;
}
public sealed class TestStorage : IS3Storage
{
    public byte[] Video { get; set; } = [0, 1, 2];
    public Task<string> SignedUrl(StorageConnection c, string key, CancellationToken ct) => Task.FromResult("https://s3.example.test/" + c.Bucket + "/" + key + "?X-Amz-Signature=test-signature");
    public Task<S3Page> List(StorageConnection c, string prefix, string? token, CancellationToken ct) => Task.FromResult(new S3Page([new("video.mp4", Video.Length)], null));
    public Task<HttpResponseMessage> Download(StorageConnection c, string key, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Video) });
}
public sealed class HlsHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.AbsolutePath, Encoding.UTF8, "application/vnd.apple.mpegurl") });
}
public sealed class StudioFactory : WebApplicationFactory<Program>
{
    private readonly string database = Path.Combine(Path.GetTempPath(), $"broadcast-{Guid.NewGuid():N}.db");
    public TestStorage Storage { get; } = new();
    public TestAmcp Amcp { get; } = new();
    public TestGeography Geo { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite", ["ConnectionStrings:Database"] = "Data Source=" + database,
            ["Operator:Password"] = "test-password-with-enough-characters", ["Media:PublishSecret"] = "test-publish-secret-with-24-characters",
            ["Media:HookSecret"] = "test-hook-secret-with-24-characters", ["PublicOrigin"] = "http://localhost:8088", ["Worker:Disabled"] = "true",
            ["Media:Directory"] = Path.Combine(Path.GetTempPath(), "broadcast-test-media"), ["Restriction:DefaultImage"] = Path.GetFullPath("../../../../../graphics/region-unavailable.png")
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IS3Storage>(); services.AddSingleton<IS3Storage>(Storage);
            services.RemoveAll<IAmcpClient>(); services.AddSingleton<IAmcpClient>(Amcp);
            services.RemoveAll<IGeography>(); services.AddSingleton<IGeography>(Geo);
            services.AddHttpClient("hls").ConfigurePrimaryHttpMessageHandler(() => new HlsHandler());
        });
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) { File.Delete(database); File.Delete(database + "-wal"); File.Delete(database + "-shm"); } }
    public async Task<HttpClient> Operator()
    {
        var client = CreateClient(new() { BaseAddress = new Uri("http://localhost:8088"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:8088");
        (await client.PostAsJsonAsync("/api/login", new { password = "test-password-with-enough-characters" })).EnsureSuccessStatusCode();
        return client;
    }
}
public sealed class ApiTests
{
    private static async Task<JsonElement> Json(HttpResponseMessage response) { response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    private static async Task<JsonElement> Create(HttpClient client, string slug = "test-tv") => await Json(await client.PostAsJsonAsync("/api/channels", new { name = "Тест & канал", slug }));
    [Fact]
    public async Task AuthenticationAndOriginAreRequired()
    {
        using var factory = new StudioFactory(); using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/channels")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.PostAsJsonAsync("/api/login", new { password = "test-password-with-enough-characters" })).StatusCode);
        using var client = await factory.Operator();
        client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", "https://evil.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/channels", new { name = "Тест", slug = "test" })).StatusCode);
    }
    [Fact]
    public async Task PreviewTakeVersionAndFailedCommandPreserveState()
    {
        using var factory = new StudioFactory(); using var client = await factory.Operator();
        var created = await Create(client); var id = created.GetProperty("id").GetGuid();
        var detail = await Json(await client.GetAsync($"/api/channels/{id}"));
        var source = detail.GetProperty("sources")[0].GetProperty("id").GetGuid();
        var revision = detail.GetProperty("channel").GetProperty("revision").GetInt64();
        (await client.PostAsJsonAsync($"/api/channels/{id}/preview", new { sourceId = source, revision })).EnsureSuccessStatusCode();
        Assert.Contains(factory.Amcp.Commands, c => c.StartsWith("PLAY 2-10"));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/channels/{id}/take", new { transition = "CUT", frames = 0, revision })).StatusCode);
        detail = await Json(await client.GetAsync($"/api/channels/{id}")); revision = detail.GetProperty("channel").GetProperty("revision").GetInt64();
        factory.Amcp.Fail = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync($"/api/channels/{id}/take", new { transition = "MIX", frames = 25, revision })).StatusCode);
        detail = await Json(await client.GetAsync($"/api/channels/{id}"));
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("channel").GetProperty("programId").ValueKind);
        factory.Amcp.Fail = false;
        (await client.PostAsJsonAsync($"/api/channels/{id}/take", new { transition = "MIX", frames = 25, revision })).EnsureSuccessStatusCode();
        detail = await Json(await client.GetAsync($"/api/channels/{id}")); Assert.Equal(source, detail.GetProperty("channel").GetProperty("programId").GetGuid());
    }
    [Fact]
    public async Task ScheduleRejectsOverlapAndForeignSourcesAndEscapesEpg()
    {
        using var factory = new StudioFactory(); using var client = await factory.Operator();
        var first = await Create(client); var second = await Create(client, "other-tv");
        var id = first.GetProperty("id").GetGuid(); var other = second.GetProperty("id").GetGuid();
        var detail = await Json(await client.GetAsync($"/api/channels/{id}")); var sourceId = detail.GetProperty("sources")[0].GetProperty("id").GetGuid();
        var start = DateTimeOffset.UtcNow.AddHours(1); var end = start.AddMinutes(30);
        var block = new { title = "Новости & <мир>", sourceId, startsAt = start, endsAt = end };
        (await client.PostAsJsonAsync($"/api/channels/{id}/schedule", block)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/channels/{id}/schedule", block)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/channels/{other}/schedule", block)).StatusCode);
        var xml = await client.GetStringAsync("/iptv/epg.xml"); Assert.Contains("Новости &amp; &lt;мир&gt;", xml); Assert.Contains("+0000", xml);
        var m3u = await client.GetStringAsync("/iptv/playlist.m3u"); Assert.Contains("/hls/program/test-tv/index.m3u8", m3u);
    }
    [Fact]
    public async Task RegionGateCoversManifestAndSegmentsAndNeverTrustsCountryHeader()
    {
        using var factory = new StudioFactory(); using var client = await factory.Operator(); var c = await Create(client); var id = c.GetProperty("id").GetGuid();
        (await client.PutAsJsonAsync($"/api/channels/{id}/restriction", new { enabled = true, rules = new[] { "RU" }, blockUnknown = true, revision = 0 })).EnsureSuccessStatusCode();
        factory.Geo.Location = new("RU", null); client.DefaultRequestHeaders.Add("X-Country", "NL");
        foreach (var file in new[] { "index.m3u8", "stream.m3u8", "segment123.ts" })
        { var response = await client.GetAsync($"/hls/program/test-tv/{file}"); Assert.Contains("/slate/test-tv/" + file, await response.Content.ReadAsStringAsync()); Assert.Contains("no-store", response.Headers.CacheControl!.ToString()); }
        factory.Geo.Location = new("NL", null); Assert.Contains("/program/test-tv/", await client.GetStringAsync("/hls/program/test-tv/index.m3u8"));
        factory.Geo.Location = new(null, null); Assert.Contains("/slate/test-tv/", await client.GetStringAsync("/hls/program/test-tv/segment123.ts"));
        using var anonymous = factory.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/hls/preview/test-tv/index.m3u8")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/hls/slate/test-tv/index.m3u8")).StatusCode);
    }
    [Fact]
    public async Task MissingGeoDatabaseCannotEnableRestrictions()
    {
        using var factory = new StudioFactory(); using var client = await factory.Operator(); var c = await Create(client); factory.Geo.Available = false;
        var response = await client.PutAsJsonAsync($"/api/channels/{c.GetProperty("id").GetGuid()}/restriction", new { enabled = true, rules = new[] { "RU" }, blockUnknown = true, revision = 0 });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
    [Fact]
    public async Task PublicRtmpReadAndForgedHookAreRejected()
    {
        using var factory = new StudioFactory(); using var client = factory.CreateClient();
        var body = new { action = "read", path = "program/test-tv", user = "any", password = "" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/internal/media-auth?secret=wrong", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/internal/media-auth?secret=test-hook-secret-with-24-characters", body)).StatusCode);
    }
    [Fact]
    public async Task PasswordChangeRevokesOtherSessionsAndOldPassword()
    {
        using var factory = new StudioFactory(); using var first = await factory.Operator(); using var second = await factory.Operator();
        (await first.PostAsJsonAsync("/api/password", new { currentPassword = "test-password-with-enough-characters", newPassword = "new-secure-password-1234" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/channels")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.PostAsJsonAsync("/api/login", new { password = "test-password-with-enough-characters" })).StatusCode);
        (await first.PostAsJsonAsync("/api/login", new { password = "new-secure-password-1234" })).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task S3DirectKeepsCredentialsPrivateAndPlaysSignedUrl()
    {
        using var factory = new StudioFactory(); using var client = await factory.Operator(); var c = await Create(client); var id = c.GetProperty("id").GetGuid();
        var config = new { endpoint = "https://s3.example.test", bucket = "broadcast", region = "us-east-1", accessKey = "example-access", secretKey = "example-secret-private", sessionToken = (string?)null };
        (await client.PutAsJsonAsync($"/api/channels/{id}/storage", config)).EnsureSuccessStatusCode();
        var saved = await client.GetStringAsync($"/api/channels/{id}/storage"); Assert.DoesNotContain(config.secretKey, saved); Assert.DoesNotContain(config.accessKey, saved);
        await using (var db = await factory.Services.GetRequiredService<IDbContextFactory<BroadcastDb>>().CreateDbContextAsync())
        { var stored = await db.StorageConnections.SingleAsync(); Assert.DoesNotContain(config.secretKey, stored.ProtectedCredentials); }
        (await client.PostAsJsonAsync($"/api/channels/{id}/storage/direct", new { key = "video.mp4", durationSeconds = 30 })).EnsureSuccessStatusCode();
        var detail = await Json(await client.GetAsync($"/api/channels/{id}")); var source = detail.GetProperty("sources").EnumerateArray().Single(x => x.GetProperty("kind").GetInt32() == 3);
        var revision = detail.GetProperty("channel").GetProperty("revision").GetInt64();
        (await client.PostAsJsonAsync($"/api/channels/{id}/preview", new { sourceId = source.GetProperty("id").GetGuid(), revision })).EnsureSuccessStatusCode();
        Assert.Contains(factory.Amcp.Commands, x => x.Contains("https://s3.example.test/broadcast/video.mp4?X-Amz-Signature=test-signature"));
        Assert.Empty((await Json(await client.GetAsync($"/api/channels/{id}/storage/imports"))).EnumerateArray());
    }
    [Fact]
    public async Task UploadAndS3ImportValidateActualVideo()
    {
        using var factory = new StudioFactory(); using var client = await factory.Operator(); var c = await Create(client); var id = c.GetProperty("id").GetGuid();
        var file = Path.Combine(Path.GetTempPath(), $"video-{Guid.NewGuid():N}.mp4");
        using (var p = new System.Diagnostics.Process { StartInfo = new("ffmpeg") { UseShellExecute = false, RedirectStandardError = true } })
        {
            foreach (var arg in new[] { "-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=320x180:r=25", "-t", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p", file }) p.StartInfo.ArgumentList.Add(arg);
            p.Start(); var error = p.StandardError.ReadToEndAsync(); await p.WaitForExitAsync(); await error; Assert.Equal(0, p.ExitCode);
        }
        try
        {
            factory.Storage.Video = await File.ReadAllBytesAsync(file);
            using var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(factory.Storage.Video), "file", "upload.mp4");
            (await client.PostAsync($"/api/channels/{id}/media", form)).EnsureSuccessStatusCode();
            (await client.PutAsJsonAsync($"/api/channels/{id}/storage", new { endpoint = "https://s3.example.test", bucket = "broadcast", region = "us-east-1", accessKey = "test", secretKey = "test-secret" })).EnsureSuccessStatusCode();
            var job = await Json(await client.PostAsJsonAsync($"/api/channels/{id}/storage/imports", new { key = "import.mp4" }));
            var services = factory.Services;
            var worker = new S3ImportWorker(services.GetRequiredService<IDbContextFactory<BroadcastDb>>(), factory.Storage, services.GetRequiredService<Control>(), services.GetRequiredService<IConfiguration>(), NullLogger<S3ImportWorker>.Instance);
            await worker.Import(job.GetProperty("id").GetGuid(), CancellationToken.None);
            var jobs = await Json(await client.GetAsync($"/api/channels/{id}/storage/imports")); Assert.Equal("Ready", jobs[0].GetProperty("status").GetString());
            var detail = await Json(await client.GetAsync($"/api/channels/{id}")); Assert.Equal(2, detail.GetProperty("sources").EnumerateArray().Count(x => x.GetProperty("kind").GetInt32() == 1));
            using var bad = new MultipartFormDataContent(); bad.Add(new ByteArrayContent([0, 1, 2]), "file", "broken.mp4");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/channels/{id}/media", bad)).StatusCode);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task PngLogoPreservesAlphaAndCannotCrossChannelBoundary()
    {
        using var factory = new StudioFactory(); using var client = await factory.Operator();
        var channel = await Create(client); var other = await Create(client, "logo-other");
        var id = channel.GetProperty("id").GetGuid(); var otherId = other.GetProperty("id").GetGuid();
        using var upload = new MultipartFormDataContent();
        upload.Add(new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAAD0lEQVR4nGP4z8AARAwMAAz8Af9c/RSVAAAAAElFTkSuQmCC")), "file", "логотип.png");
        var asset = await Json(await client.PostAsync($"/api/channels/{id}/graphics/assets", upload));
        var imageId = asset.GetProperty("imageId").GetString();
        var image = await client.GetByteArrayAsync($"/api/channels/{id}/graphics/assets/{imageId}");
        Assert.Equal(6, image[25]); // PNG RGBA, прозрачность не сведена к цветному фону.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/channels/{otherId}/graphics/assets/{imageId}")).StatusCode);
        var scene = new { elements = new[] { new { id = "logo", kind = "image", imageId, text = "", x = 1500, y = 80, width = 220, height = 110, fontSize = 44, color = "#ffffff", background = "#000000" } }, assetBaseUri = "file:///etc/" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/channels/{otherId}/graphics", new { scene, revision = 0 })).StatusCode);
        (await client.PutAsJsonAsync($"/api/channels/{id}/graphics", new { scene, revision = 0 })).EnsureSuccessStatusCode();
        var detail = await Json(await client.GetAsync($"/api/channels/{id}"));
        var saved = detail.GetProperty("channel").GetProperty("graphicsJson").GetString()!;
        Assert.Contains($"file:///media/logos/{id:N}/", saved); Assert.DoesNotContain("file:///etc", saved);
        (await client.PostAsJsonAsync($"/api/channels/{id}/graphics", new { enabled = true, revision = 1 })).EnsureSuccessStatusCode();
        Assert.Contains(factory.Amcp.Commands, x => x.StartsWith("CG ") && x.Contains(imageId!) && x.Contains($"/media/logos/{id:N}/"));
        using var anonymous = factory.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/channels/{id}/graphics/assets/{imageId}")).StatusCode);
    }
    [Fact]
    public async Task BrokenLogoIsRejected()
    {
        using var factory = new StudioFactory(); using var client = await factory.Operator(); var channel = await Create(client);
        var id = channel.GetProperty("id").GetGuid();
        using var upload = new MultipartFormDataContent(); upload.Add(new ByteArrayContent([0, 1, 2]), "file", "broken.png");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/channels/{id}/graphics/assets", upload)).StatusCode);
    }

}
public sealed class TimelineTests
{
    [Theory]
    [InlineData(0, 0, 0)] [InlineData(9, 0, 9)] [InlineData(10, 1, 0)] [InlineData(29, 1, 19)] [InlineData(30, 0, 0)] [InlineData(75, 1, 5)]
    public void PlaylistSelectsBoundariesAndLoops(double elapsed, int position, double offset)
    {
        var result = Timeline.Select([new() { SourceId = Guid.NewGuid(), Position = 0, DurationSeconds = 10 }, new() { SourceId = Guid.NewGuid(), Position = 1, DurationSeconds = 20 }], elapsed);
        Assert.NotNull(result); Assert.Equal(position, result.Position); Assert.Equal(offset, result.Offset);
    }
    [Fact] public void EmptyPlaylistReturnsNoSelection() => Assert.Null(Timeline.Select([], 10));
    [Fact] public void AmcpRejectsNewlineInjection() => Assert.Throws<ArgumentException>(() => Amcp.Quote("file\r\nCLEAR 1"));
    [Fact] public void AmcpEscapesQuotesAndSlashes() => Assert.Equal("\"a\\\"b\\\\c\"", Amcp.Quote("a\"b\\c"));
    [Fact] public void UnknownSubdivisionCanFailClosed()
    {
        var c = new Channel { RegionRestrictionEnabled = true, BlockedCountriesJson = "[\"RU-MOW\"]", BlockUnknownCountry = true };
        Assert.True(RegionalPolicy.Blocked(c, new("RU", null)));
        Assert.False(RegionalPolicy.Blocked(c, new("RU", "RU-SPE")));
    }
}

public sealed class StorageSigningTests
{
    [Fact]
    public void SignatureMatchesPublishedAwsVector()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://examplebucket.s3.amazonaws.com/?max-keys=2&prefix=J");
        S3Signing.Sign(request, new("AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", null), "us-east-1", new DateTimeOffset(2013, 5, 24, 0, 0, 0, TimeSpan.Zero));
        Assert.Contains("34b48302e7b5fa45bde8084f4b7868a86f0a534bc59db6670ed5711ef69dc6f7", request.Headers.GetValues("Authorization").Single());
    }
    [Theory]
    [InlineData("127.0.0.1")] [InlineData("169.254.169.254")] [InlineData("192.168.1.1")] [InlineData("::1")] [InlineData("::ffff:10.0.0.1")] [InlineData("fd00::1")]
    public void PrivateStorageAddressesAreRejected(string ip) => Assert.False(S3Signing.PublicAddress(IPAddress.Parse(ip)));
    [Fact]
    public void PresignedUrlContainsSignatureAndEscapedKey()
    {
        var url = S3Signing.Presign(new Uri("https://s3.example.test"), "broadcast", "folder/мой ролик.mp4", new("access", "private-secret", null), "us-east-1", DateTimeOffset.UnixEpoch);
        Assert.Contains("%20", url); Assert.Contains("X-Amz-Signature=", url); Assert.DoesNotContain("private-secret", url); Assert.Contains("X-Amz-Expires=604800", url);
    }
}
public sealed class RussianStorageTests
{
    [Fact]
    public void RussianPresetsUseExplicitRegionsAndSecureEndpoints()
    {
        Assert.Contains(StoragePresets.All, x => x.Id == "yandex" && x.Region == "ru-central1");
        Assert.Contains(StoragePresets.All, x => x.Id == "vk" && x.Region == "ru-msk");
        Assert.Contains(StoragePresets.All, x => x.Id == "selectel-ru1" && !x.PathStyle);
        Assert.All(StoragePresets.All, x => Assert.StartsWith("https://", x.Endpoint));
    }
    [Fact]
    public void VirtualHostedUrlKeepsBucketOutOfPath()
    {
        var url = S3Signing.Presign(new Uri("https://s3.ru-1.storage.selcloud.ru"), "broadcast", "Эфиры/Ролик 1.mp4", new("access", "secret", null), "ru-1", DateTimeOffset.UnixEpoch, false);
        Assert.StartsWith("https://broadcast.s3.ru-1.storage.selcloud.ru/", url);
        Assert.DoesNotContain("/broadcast/", url); Assert.Contains("%20", url); Assert.Contains("X-Amz-Signature=", url);
    }
    [Fact]
    public void LiveSourceUsesMediaMtxQueryAuthentication()
    {
        var source = new Source { Kind = SourceKind.Live, ChannelId = Guid.NewGuid() };
        var command = Amcp.Play(1, source, secret: "test-secret");
        Assert.Contains("?user=engine&pass=test-secret", command);
        Assert.DoesNotContain("engine:test-secret@", command);
    }
}
