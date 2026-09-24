using System.Security.Cryptography;
using System.Text;

namespace GameApi.Security;

/// <summary>
/// Fixed-time comparison against a configured API key -- vendored from
/// altered-bga-api's AlteredBgaApi.Security.ApiKeyAuth as-is. Used only by
/// the admin adjustment endpoint: unlike the read endpoints (AlteredAuth
/// bearer + "bga-game-history" scope), setting a win/loss adjustment is a
/// privileged action gated on its own dedicated secret.
/// </summary>
public static class ApiKeyAuth
{
    public static bool Matches(string? presentedKey, string? configuredKey)
    {
        // Nothing presented can ever match an unconfigured key -- misconfiguration
        // fails closed rather than accepting everything.
        if (string.IsNullOrEmpty(configuredKey) || string.IsNullOrEmpty(presentedKey))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presentedKey), Encoding.UTF8.GetBytes(configuredKey));
    }
}
