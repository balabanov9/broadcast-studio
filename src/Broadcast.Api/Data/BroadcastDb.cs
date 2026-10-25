using Broadcast.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Broadcast.Api.Data;
public sealed class BroadcastDb(DbContextOptions<BroadcastDb> options) : DbContext(options)
{
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistItem> PlaylistItems => Set<PlaylistItem>();
    public DbSet<ScheduleBlock> Schedule => Set<ScheduleBlock>();
    public DbSet<AirEvent> Events => Set<AirEvent>();
    public DbSet<StorageConnection> StorageConnections => Set<StorageConnection>();
    public DbSet<StorageImport> Imports => Set<StorageImport>();
    public DbSet<OperatorAccount> Operators => Set<OperatorAccount>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<StorageConnection>().HasIndex(x => x.ChannelId).IsUnique();
        b.Entity<StorageConnection>().HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<StorageImport>().HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<StorageImport>().HasIndex(x => x.Status);
        b.Entity<Channel>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<Channel>().HasIndex(x => x.Slot).IsUnique();
        b.Entity<Channel>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<Source>().HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Playlist>().HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Playlist>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.PlaylistId);
        b.Entity<PlaylistItem>().HasOne<Source>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PlaylistItem>().HasIndex(x => new { x.PlaylistId, x.Position }).IsUnique();
        b.Entity<ScheduleBlock>().HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ScheduleBlock>().HasOne<Source>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ScheduleBlock>().HasOne<Playlist>().WithMany().HasForeignKey(x => x.PlaylistId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ScheduleBlock>().HasIndex(x => new { x.ChannelId, x.StartsAt });
        b.Entity<AirEvent>().HasIndex(x => new { x.ChannelId, x.At });
    }
}
