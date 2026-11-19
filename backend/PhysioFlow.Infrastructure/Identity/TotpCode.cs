using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PhysioFlow.Infrastructure.Identity;

/// <summary>RFC 6238: SHA-1, six digits, 30-second steps, drift of one step.</summary>
public static class TotpCode
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string NewSecret() => Encode(RandomNumberGenerator.GetBytes(20));

    public static string Generate(string secret, long step, int digits = 6)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(Decode(secret), counter);
        var offset = hash[^1] & 15;
        var value = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff;
        var modulus = digits == 8 ? 100_000_000 : 1_000_000;
        return (value % modulus).ToString(digits == 8 ? "D8" : "D6", CultureInfo.InvariantCulture);
    }

    public static long? Match(string secret, string? code, DateTimeOffset now, long lastUsedStep)
    {
        if (code is null || code.Length != 6 || code.Any(c => c is < '0' or > '9')) return null;
        var current = now.ToUnixTimeSeconds() / 30;
        foreach (var step in new[] { current, current - 1, current + 1 })
            if (step > lastUsedStep && CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(Generate(secret, step)), Encoding.ASCII.GetBytes(code)))
                return step;
        return null;
    }

    private static string Encode(byte[] bytes)
    {
        var result = new StringBuilder();
        var buffer = 0;
        var bits = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5) { bits -= 5; result.Append(Alphabet[(buffer >> bits) & 31]); }
        }
        if (bits > 0) result.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return result.ToString();
    }

    private static byte[] Decode(string text)
    {
        var result = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var c in text)
        {
            var value = Alphabet.IndexOf(c);
            if (value < 0) throw new FormatException("Invalid TOTP secret encoding.");
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8) { bits -= 8; result.Add((byte)(buffer >> bits)); }
        }
        return result.ToArray();
    }
}
