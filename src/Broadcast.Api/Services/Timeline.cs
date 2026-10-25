using Broadcast.Api.Domain;

namespace Broadcast.Api.Services;
public sealed record Selection(Guid SourceId, double Offset, int Position);
public static class Timeline
{
    public static Selection? Select(IReadOnlyList<PlaylistItem> items, double elapsed)
    {
        var ordered = items.OrderBy(x => x.Position).ToArray();
        var total = ordered.Sum(x => x.DurationSeconds);
        if (ordered.Length == 0 || total <= 0 || ordered.Any(x => !double.IsFinite(x.DurationSeconds) || x.DurationSeconds <= 0)) return null;
        var offset = ((elapsed % total) + total) % total;
        foreach (var item in ordered)
        {
            if (offset < item.DurationSeconds) return new(item.SourceId, offset, item.Position);
            offset -= item.DurationSeconds;
        }
        return null;
    }
}
