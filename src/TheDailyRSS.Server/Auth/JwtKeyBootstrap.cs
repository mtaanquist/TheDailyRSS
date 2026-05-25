using System.Security.Cryptography;

namespace TheDailyRSS.Server.Auth;

/// <summary>
/// Ensures a stable JWT signing key exists for self-hosters who don't set one.
/// The key is generated once and persisted next to the app data so tokens survive restarts.
/// </summary>
public static class JwtKeyBootstrap
{
    public static string LoadOrCreate(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        var path = Path.Combine(dataDir, "jwt-signing.key");
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length >= 32) return existing;
        }

        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        File.WriteAllText(path, key);
        return key;
    }
}
