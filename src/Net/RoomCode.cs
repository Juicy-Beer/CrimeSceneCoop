using System.Security.Cryptography;
using System.Text;

namespace CrimeSceneCoop;

internal static class RoomCode
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Generate(int length = 6)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return new string(chars);
    }

    public static string Normalize(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw.Trim().ToUpperInvariant())
        {
            if (c is '0' or 'O') { sb.Append('O'); continue; } // treated as invalid later
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        }
        return sb.ToString()
            .Replace('0', 'O')
            .Replace('1', 'I');
    }

    public static bool IsSteamCode(string code)
    {
        code = Normalize(code);
        if (code.Length is < 5 or > 8) return false;
        foreach (var c in code)
            if (Alphabet.IndexOf(c) < 0) return false;
        return true;
    }

    public static bool TryParseDirect(string raw, out string host, out int port)
    {
        host = "";
        port = CoopConfig.Current.DirectPort;
        raw = raw.Trim();
        if (raw.StartsWith("DIRECT:", StringComparison.OrdinalIgnoreCase))
            raw = raw[7..];
        var parts = raw.Split(':');
        if (parts.Length == 1)
        {
            host = parts[0];
            return !string.IsNullOrWhiteSpace(host);
        }
        if (parts.Length == 2 && int.TryParse(parts[1], out port))
        {
            host = parts[0];
            return !string.IsNullOrWhiteSpace(host);
        }
        return false;
    }
}
