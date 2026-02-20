using System.Text.Json;
using FocusGuard.Core.Configuration;

namespace FocusGuard.Core.Hardening;

public static class HeartbeatHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string HeartbeatFilePath =>
        Path.Combine(AppPaths.DataDirectory, "heartbeat.json");

    public static async Task WriteAsync(HeartbeatData data)
    {
        var dir = Path.GetDirectoryName(HeartbeatFilePath);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(data, JsonOptions);
        var tempPath = HeartbeatFilePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, HeartbeatFilePath, overwrite: true);
    }

    public static async Task<HeartbeatData?> ReadAsync()
    {
        try
        {
            if (!File.Exists(HeartbeatFilePath))
                return null;

            var json = await File.ReadAllTextAsync(HeartbeatFilePath);
            return JsonSerializer.Deserialize<HeartbeatData>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Delete()
    {
        try
        {
            if (File.Exists(HeartbeatFilePath))
                File.Delete(HeartbeatFilePath);
        }
        catch
        {
            // Best-effort delete
        }
    }
}
