using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace Hesap.Infrastructure;

public static class Passwords
{
    private static readonly PasswordHasher<object> Hasher = new();

    public static string Hash(string password) => Hasher.HashPassword(null!, password);

    public static bool Verify(string hash, string password)
    {
        try
        {
            return Hasher.VerifyHashedPassword(null!, hash, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string Sha256Hex(string input) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    public static string RandomToken(int bytes = 32) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static class PasswordPolicy
{
    /// <summary>En az 8 karakter; harf ve rakam içermeli; en fazla 128 karakter.</summary>
    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            return "Şifre en az 8 karakter olmalıdır.";
        if (password.Length > 128)
            return "Şifre en fazla 128 karakter olabilir.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "Şifre en az bir harf ve bir rakam içermelidir.";
        return null;
    }
}

/// <summary>Türkiye'ye özgü kimlik ve hesap numarası doğrulamaları.</summary>
public static class TrIds
{
    /// <summary>T.C. kimlik no: 11 hane, ilk hane 0 değil, 10. ve 11. haneler kontrol hanesi.</summary>
    public static bool IsValidNationalId(string? v)
    {
        if (string.IsNullOrEmpty(v) || v.Length != 11 || v[0] == '0' || !v.All(char.IsAsciiDigit))
            return false;

        var d = v.Select(c => c - '0').ToArray();
        var tenth = (((d[0] + d[2] + d[4] + d[6] + d[8]) * 7 - (d[1] + d[3] + d[5] + d[7])) % 10 + 10) % 10;
        return d[9] == tenth && d[10] == d.Take(10).Sum() % 10;
    }

    /// <summary>Vergi kimlik no (10 hane) GİB kontrol algoritması.</summary>
    public static bool IsValidTaxNumber(string? v)
    {
        if (string.IsNullOrEmpty(v) || v.Length != 10 || !v.All(char.IsAsciiDigit))
            return false;

        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            var t = (v[i] - '0' + 9 - i) % 10;
            var p = t * (int)Math.Pow(2, 9 - i) % 9;
            if (t != 0 && p == 0) p = 9;
            sum += p;
        }

        return (10 - sum % 10) % 10 == v[9] - '0';
    }

    /// <summary>Vergi no (10) veya şahıs için T.C. kimlik no (11). Boş geçerlidir.</summary>
    public static bool IsValidTaxOrNationalId(string? v) =>
        string.IsNullOrEmpty(v) || (v.Length == 10 ? IsValidTaxNumber(v) : IsValidNationalId(v));

    public static string? NormalizeIban(string? iban) =>
        string.IsNullOrWhiteSpace(iban) ? null : new string(iban.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

    /// <summary>TR IBAN: 26 karakter, ISO 13616 mod-97 kontrolü.</summary>
    public static bool IsValidTrIban(string? iban)
    {
        if (iban == null || iban.Length != 26 || !iban.StartsWith("TR") || !iban[2..].All(char.IsAsciiDigit))
            return false;

        var rearranged = iban[4..] + "2927" + iban[2..4];
        var mod = 0;
        foreach (var c in rearranged)
            mod = (mod * 10 + (c - '0')) % 97;
        return mod == 1;
    }
}

public static class Money
{
    public static readonly CultureInfo Tr = new("tr-TR");

    /// <summary>Kuruşa yuvarlama (yarım kuruş yukarı).</summary>
    public static decimal R2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public static string Format(decimal v) => v.ToString("N2", Tr) + " ₺";
}
