using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace WindowsAiAssistant.App.Audio.EdgeTts;

internal static class EdgeTtsDrm
{
    private const long WinEpoch = 11644473600;
    private const double SecondsToNanoseconds = 1e9;

    private static double _clockSkewSeconds;

    public static string GenerateSecMsGec()
    {
        var ticks = GetUnixTimestamp() + WinEpoch;
        ticks -= ticks % 300;
        ticks *= SecondsToNanoseconds / 100;

        var payload = $"{ticks:F0}{EdgeTtsConstants.TrustedClientToken}";
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    public static string GenerateMuid() => Guid.NewGuid().ToString("N").ToUpperInvariant();

    public static void AdjustClockSkewFromServerDate(string? serverDate)
    {
        if (string.IsNullOrWhiteSpace(serverDate))
        {
            return;
        }

        if (!DateTimeOffset.TryParseExact(
                serverDate,
                "r",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            return;
        }

        _clockSkewSeconds += parsed.UtcDateTime.Subtract(DateTime.UtcNow).TotalSeconds;
    }

    public static void AdjustClockSkewFromResponse(HttpResponseMessage response)
    {
        if (response.Headers.Date is DateTimeOffset serverDate)
        {
            _clockSkewSeconds += serverDate.UtcDateTime.Subtract(DateTime.UtcNow).TotalSeconds;
        }
    }

    private static double GetUnixTimestamp() =>
        DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _clockSkewSeconds;
}
