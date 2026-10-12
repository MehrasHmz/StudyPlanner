using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using StudyPlanner.Application.Authentication;
using StudyPlanner.Domain.Entities;

namespace StudyPlanner.Infrastructure.Authentication;

public class PasswordHashService : IPasswordHashService
{
    private const string ModernPrefix = "Identity:";
    private readonly IPasswordHasher<User> _hasher;

    public PasswordHashService(IPasswordHasher<User> hasher) => _hasher = hasher;

    public string HashPassword(User user, string password) => ModernPrefix + _hasher.HashPassword(user, password);

    public PasswordVerificationOutcome VerifyPassword(User user, string password)
    {
        var storedHash = user.PasswordHash;
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash) || storedHash.Length > 500)
            return PasswordVerificationOutcome.Failed;

        if (storedHash.StartsWith(ModernPrefix, StringComparison.Ordinal))
        {
            var payload = storedHash[ModernPrefix.Length..];
            if (!TryDecodeCanonical(payload, out var bytes) || !IsSupportedIdentityPayload(bytes))
                return PasswordVerificationOutcome.Failed;

            return _hasher.VerifyHashedPassword(user, payload, password) switch
            {
                PasswordVerificationResult.Success => PasswordVerificationOutcome.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordVerificationOutcome.SuccessRehashNeeded,
                _ => PasswordVerificationOutcome.Failed
            };
        }

        return VerifyLegacyPassword(storedHash, password);
    }

    private static bool IsSupportedIdentityPayload(byte[] bytes)
    {
        if (bytes.Length == 0) return false;
        if (bytes[0] == 0) return bytes.Length == 49;
        if (bytes[0] != 1 || bytes.Length < 45) return false;

        var prf = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1, 4));
        var iterations = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5, 4));
        var saltLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(9, 4));
        // Bound malformed metadata before Identity allocates buffers or runs the work factor.
        return prf <= 2 && iterations is > 0 and <= 1_000_000 &&
            saltLength >= 16 && saltLength <= bytes.Length - 13 - 16;
    }

    private static bool TryDecodeCanonical(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(value);
            return Convert.ToBase64String(bytes) == value;
        }
        catch (FormatException)
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }

    private static PasswordVerificationOutcome VerifyLegacyPassword(string storedHash, string password)
    {
        // Verification only: historical Base64(SHA256(UTF8(password + fixed salt))).
        // Modern-prefixed values never reach this compatibility branch.
        if (storedHash.Length != 44 || !TryDecodeCanonical(storedHash, out var expected) || expected.Length != 32)
            return PasswordVerificationOutcome.Failed;

        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(password + "StudyPlannerSalt2024"));
        return CryptographicOperations.FixedTimeEquals(actual, expected)
            ? PasswordVerificationOutcome.SuccessRehashNeeded
            : PasswordVerificationOutcome.Failed;
    }
}
