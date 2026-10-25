using System.ComponentModel.DataAnnotations;
namespace Broadcast.Api.Domain;
public sealed class StorageConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChannelId { get; set; }
    [MaxLength(300)] public string Endpoint { get; set; } = "";
    [MaxLength(63)] public string Bucket { get; set; } = "";
    [MaxLength(50)] public string Region { get; set; } = "us-east-1";
    public bool PathStyle { get; set; } = true;
    public string ProtectedCredentials { get; set; } = "";
}
public sealed class StorageImport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChannelId { get; set; }
    public string ObjectKey { get; set; } = "";
    public string ConnectionSnapshot { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(20)] public string Status { get; set; } = "Queued";
    public long BytesReceived { get; set; }
    public long? TotalBytes { get; set; }
    public Guid? SourceId { get; set; }
    public string? Error { get; set; }
}
public sealed class OperatorAccount
{
    public int Id { get; set; } = 1;
    public string PasswordHash { get; set; } = "";
    public long SecurityVersion { get; set; } = 1;
}
