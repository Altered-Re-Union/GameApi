using System.Security.Cryptography;
using System.Text;

namespace GameApi.Security;

/// <summary>
/// Fixed-time comparison against a configured API key, shared by every
/// endpoint protected with the same secret -- vendored from
/// altered-bga-api's AlteredBgaApi.Security.ApiKeyAuth as-is.
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
