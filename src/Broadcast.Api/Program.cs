using System.Net.Http.Headers;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;
using Broadcast.Api.Data;
using Broadcast.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Broadcast.Api.Endpoints;
using Broadcast.Api.Infrastructure;
using Broadcast.Api.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);
var origin = builder.Configuration["PublicOrigin"] ?? "http://localhost:8088";
if (!Uri.TryCreate(origin, UriKind.Absolute, out var publicUri) || publicUri.AbsolutePath != "/") throw new InvalidOperationException("PublicOrigin должен содержать только схему, хост и порт");
builder.Configuration["PublicOrigin"] = origin.TrimEnd('/');
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 2_010_000_000);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 2_000_000_000);
builder.Services.AddDbContextFactory<BroadcastDb>(options =>
{
    if (builder.Configuration["Database:Provider"] == "Sqlite") options.UseSqlite(builder.Configuration.GetConnectionString("Database") ?? "Data Source=broadcast.db");
    else options.UseNpgsql(builder.Configuration.GetConnectionString("Database"));
});
builder.Services.AddSingleton<IPasswordHasher<OperatorAccount>, PasswordHasher<OperatorAccount>>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "lumiere.operator";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = publicUri.Scheme == "https" ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromHours(12);
    o.Events.OnValidatePrincipal = async context =>
    {
        var factory = context.HttpContext.RequestServices.GetRequiredService<IDbContextFactory<BroadcastDb>>();
        await using var db = await factory.CreateDbContextAsync(context.HttpContext.RequestAborted);
        var account = await db.Operators.AsNoTracking().SingleAsync(context.HttpContext.RequestAborted);
        if (context.Principal?.FindFirst("version")?.Value != account.SecurityVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        { context.RejectPrincipal(); await context.HttpContext.SignOutAsync(); }
    };
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});
var keysPath = builder.Configuration["DataProtection:Directory"];
if (keysPath is not null) builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath));
builder.Services.AddAuthorization();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownProxies.Clear(); o.KnownIPNetworks.Clear();
    o.KnownProxies.Add(IPAddress.Parse(builder.Configuration["Proxy:Address"] ?? "172.29.80.10"));
    o.ForwardLimit = 1;
});
builder.Services.AddSingleton<IGeography, Geography>();
builder.Services.AddSingleton<IS3Storage, S3Storage>();
builder.Services.AddHttpClient("s3", client => client.Timeout = TimeSpan.FromMinutes(60)).ConfigurePrimaryHttpMessageHandler(S3Signing.Handler);
if (!builder.Configuration.GetValue("Worker:Disabled", false)) builder.Services.AddHostedService<S3ImportWorker>();
builder.Services.AddSingleton<Slates>();
if (!builder.Configuration.GetValue("Worker:Disabled", false)) builder.Services.AddHostedService(sp => sp.GetRequiredService<Slates>());
builder.Services.AddHttpClient("hls", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Media:HlsUrl"] ?? "http://mediamtx:8888/");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("engine:" + builder.Configuration["Media:PublishSecret"])));
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddRateLimiter(o => o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new() { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
builder.Services.AddSignalR();
builder.Services.AddSingleton<IAmcpClient, AmcpClient>();
builder.Services.AddSingleton<Control>();
builder.Services.AddHttpClient("mediamtx", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Media:ApiUrl"] ?? "http://mediamtx:9997/");
    client.Timeout = TimeSpan.FromSeconds(3);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("engine:" + builder.Configuration["Media:PublishSecret"])));
});
if (!builder.Configuration.GetValue("Worker:Disabled", false)) builder.Services.AddHostedService<AutopilotWorker>();
var app = builder.Build();
foreach (var key in new[] { "Operator:Password", "Media:PublishSecret", "Media:HookSecret" })
    if (string.IsNullOrWhiteSpace(app.Configuration[key]) || app.Configuration[key]!.Length < 24) throw new InvalidOperationException($"Задайте {key}: минимум 24 символа");

app.Use(async (ctx, next) =>
{
    try
    {
        if (ctx.Request.Path.StartsWithSegments("/api") && ctx.Request.Method is not ("GET" or "HEAD" or "OPTIONS") && ctx.Request.Headers.Origin != origin.TrimEnd('/'))
        { ctx.Response.StatusCode = 403; await ctx.Response.WriteAsJsonAsync(new { error = "Origin запроса не разрешён" }); return; }
        await next();
    }
    catch (Exception e) when (!ctx.Response.HasStarted)
    {
        var status = e switch { ArgumentException => 400, KeyNotFoundException => 404, ConflictException or DbUpdateConcurrencyException => 409, DbUpdateException => 409, IOException or OperationCanceledException => 503, _ => 500 };
        app.Logger.Log(status >= 500 ? LogLevel.Error : LogLevel.Information, e, "Ошибка запроса {Путь}", ctx.Request.Path);
        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new { error = status == 500 ? "Внутренняя ошибка сервера" : status == 503 ? "Медиадвижок недоступен. Переключение не подтверждено." : e is DbUpdateException ? "Конфликт сохранения данных" : e.Message });
    }
});
app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapBroadcast();
app.MapRegional();
app.MapStorage();
app.MapGraphicsAssets();
app.MapHub<AirHub>("/hubs/air").RequireAuthorization();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BroadcastDb>();
    if (db.Database.IsSqlite()) await db.Database.EnsureCreatedAsync();
    else await db.Database.MigrateAsync();
    if (!await db.Operators.AnyAsync())
    {
        var account = new OperatorAccount();
        account.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<OperatorAccount>>().HashPassword(account, app.Configuration["Operator:Password"]!);
        db.Operators.Add(account); await db.SaveChangesAsync();
    }
}
await app.RunAsync();
public partial class Program;
