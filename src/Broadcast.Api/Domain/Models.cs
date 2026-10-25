using System.ComponentModel.DataAnnotations;

namespace Broadcast.Api.Domain;

public sealed class Channel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(80)] public string Name { get; set; } = "Новый канал";
    [MaxLength(40)] public string Slug { get; set; } = "channel";
    public int Slot { get; set; }
    public long Revision { get; set; }
    public bool Autopilot { get; set; }
    public Guid? PreviewId { get; set; }
    public Guid? ProgramId { get; set; }
    public Guid? FallbackId { get; set; }
    public Guid? ActiveBlockId { get; set; }
    public DateTimeOffset? MediaLeaseRefreshAt { get; set; }
    public DateTimeOffset? ProgramSince { get; set; }
    public bool RegionRestrictionEnabled { get; set; }
    public string BlockedCountriesJson { get; set; } = "[]";
    public bool BlockUnknownCountry { get; set; } = true;
    public string? RestrictionImage { get; set; }
    public bool GraphicsOnAir { get; set; }
    public string GraphicsJson { get; set; } = """{"elements":[]}""";
    public string Status { get; set; } = "Не подключён";
    public string? Error { get; set; }
    public string IngestKey { get; set; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
}
public sealed class Source
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChannelId { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    public SourceKind Kind { get; set; }
    [MaxLength(1024)] public string Locator { get; set; } = "";
    public double DurationSeconds { get; set; }
}
public enum SourceKind { Color, Clip, Live, S3 }
public sealed class Playlist
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChannelId { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    public List<PlaylistItem> Items { get; set; } = [];
}
public sealed class PlaylistItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlaylistId { get; set; }
    public Guid SourceId { get; set; }
    public int Position { get; set; }
    public double DurationSeconds { get; set; }
}
public sealed class ScheduleBlock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChannelId { get; set; }
    [MaxLength(140)] public string Title { get; set; } = "";
    public Guid? SourceId { get; set; }
    public Guid? PlaylistId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
}
public sealed class AirEvent
{
    public long Id { get; set; }
    public Guid ChannelId { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(500)] public string Message { get; set; } = "";
}
public sealed record GraphicElement(string Id, string Kind, string Text, int X, int Y, int Width, int Height, int FontSize, string Color, string Background, string? ImageId = null);
public sealed record GraphicScene(List<GraphicElement> Elements, string? AssetBaseUri = null);
