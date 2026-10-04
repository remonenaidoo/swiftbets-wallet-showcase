using System.Security.Cryptography;
using System.Text;

namespace SwiftBets.Wallet.Domain.Vouchers;

/// <summary>
/// A 16-character voucher code: 15 characters drawn from an alphabet without look-alikes (no 0, O, 1, I or L) and a
/// Luhn mod 31 check character, printed in groups of four. A typo is caught by the check before any lookup.
/// </summary>
public static class VoucherCode
{
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 16;

    /// <summary>The code for <paramref name="seed"/> (32 bytes): the same seed always gives the same code.</summary>
    public static string FromSeed(ReadOnlySpan<byte> seed)
    {
        if (seed.Length < 30)
        {
            throw new ArgumentException("A code needs at least 30 seed bytes.", nameof(seed));
        }

        var body = new char[Length - 1];
        for (var i = 0; i < body.Length; i++)
        {
            // Two bytes per character keeps the modulo bias below one in two thousand.
            body[i] = Alphabet[((seed[i * 2] << 8) | seed[(i * 2) + 1]) % Alphabet.Length];
        }

        return new string(body) + CheckCharacter(body);
    }

    /// <summary>The code in canonical form (upper case, no separators), or null when it is malformed or its check fails.</summary>
    public static string? Normalise(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 40)
        {
            return null;
        }

        var builder = new StringBuilder(Length);
        foreach (var c in input)
        {
            if (c is ' ' or '-')
            {
                continue;
            }

            var upper = char.ToUpperInvariant(c);
            if (!Alphabet.Contains(upper, StringComparison.Ordinal))
            {
                return null;
            }

            builder.Append(upper);
        }

        var code = builder.ToString();
        return code.Length == Length && CheckCharacter(code.AsSpan(0, Length - 1)) == code[^1] ? code : null;
    }

    public static string Format(string code) => string.Join('-', Enumerable.Range(0, code.Length / 4).Select(i => code.Substring(i * 4, 4)));

    /// <summary>What is stored and looked up: the code itself never reaches the database.</summary>
    public static byte[] Hash(string code) => SHA256.HashData(Encoding.ASCII.GetBytes(code));

    private static char CheckCharacter(ReadOnlySpan<char> body)
    {
        var n = Alphabet.Length;
        var factor = 2;
        var sum = 0;
        for (var i = body.Length - 1; i >= 0; i--)
        {
            var addend = factor * Alphabet.IndexOf(body[i], StringComparison.Ordinal);
            factor = factor == 2 ? 1 : 2;
            sum += (addend / n) + (addend % n);
        }

        return Alphabet[(n - (sum % n)) % n];
    }
}
