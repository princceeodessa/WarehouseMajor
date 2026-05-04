using System.Security.Cryptography;

namespace WarehouseAutomatisaion.Desktop.Data;

internal static class DesktopPasswordHasher
{
    public const int CurrentIterations = 210_000;

    private const int SaltSize = 32;
    private const int HashSize = 32;

    public static DesktopPasswordHash Create(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            CurrentIterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return new DesktopPasswordHash(
            Convert.ToBase64String(hash),
            Convert.ToBase64String(salt),
            CurrentIterations);
    }

    public static bool Verify(string password, string hashBase64, string saltBase64, int iterations)
    {
        if (string.IsNullOrEmpty(password)
            || string.IsNullOrWhiteSpace(hashBase64)
            || string.IsNullOrWhiteSpace(saltBase64)
            || iterations <= 0)
        {
            return false;
        }

        try
        {
            var expectedHash = Convert.FromBase64String(hashBase64);
            var salt = Convert.FromBase64String(saltBase64);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed record DesktopPasswordHash(
    string HashBase64,
    string SaltBase64,
    int Iterations);
