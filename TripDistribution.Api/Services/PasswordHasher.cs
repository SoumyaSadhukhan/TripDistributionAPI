using System.Security.Cryptography;
using System.Text;

namespace TripDistribution.Api.Services;

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string storedHash);
}

public class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16; // 128-bit
    private const int HashSize = 32; // 256-bit
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            Algorithm,
            HashSize);

        // Format: $pbkdf2$i=100000$saltHex$hashHex
        return $"$pbkdf2$i={Iterations}${Convert.ToHexString(salt).ToLowerInvariant()}${Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    public bool VerifyPassword(string password, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash) || string.IsNullOrWhiteSpace(password))
            return false;

        // Check if PBKDF2 format
        if (storedHash.StartsWith("$pbkdf2$"))
        {
            var parts = storedHash.Split('$');
            if (parts.Length == 5 && parts[2].StartsWith("i=") && int.TryParse(parts[2][2..], out var iterations))
            {
                try
                {
                    byte[] salt = Convert.FromHexString(parts[3]);
                    byte[] expectedHash = Convert.FromHexString(parts[4]);

                    byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                        Encoding.UTF8.GetBytes(password),
                        salt,
                        iterations,
                        Algorithm,
                        HashSize);

                    return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
                }
                catch
                {
                    return false;
                }
            }
        }

        // Backward compatibility: verify legacy unsalted SHA-256
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        var legacyHash = Convert.ToHexString(bytes).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(legacyHash),
            Encoding.UTF8.GetBytes(storedHash.ToLowerInvariant()));
    }
}
