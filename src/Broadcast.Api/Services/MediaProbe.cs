using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
namespace Broadcast.Api.Services;
public static class MediaProbe
{
    public static async Task<double> Duration(string path, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var process = new Process { StartInfo = new("ffprobe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false } };
        foreach (var arg in new[] { "-v", "error", "-protocol_whitelist", "file,pipe", "-show_entries", "format=duration:stream=codec_type", "-of", "json", path }) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token); var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token); var output = await stdout; await stderr;
            if (process.ExitCode != 0) throw new ArgumentException("FFprobe отклонил видеофайл");
            using var metadata = JsonDocument.Parse(output);
            if (!metadata.RootElement.GetProperty("streams").EnumerateArray().Any(x => x.GetProperty("codec_type").GetString() == "video")) throw new ArgumentException("В файле нет видеодорожки");
            var duration = double.Parse(metadata.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
            if (!double.IsFinite(duration) || duration is <= 0 or > 86400) throw new ArgumentException("Длительность видео должна быть не более 24 часов");
            return duration;
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }
}
