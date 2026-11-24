using System.Security.Cryptography;
using System.Text;

namespace BatterySwapping.Core;
public static class Passwords
{
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2$210000${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }
    public static bool Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored)) return false;
        if (stored.StartsWith("pbkdf2$"))
        {
            try
            {
                var p = stored.Split('$');
                if (p.Length != 4 || !int.TryParse(p[1], out int iterations) || iterations < 100000 || iterations > 1000000) return false;
                var key = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(p[2]), iterations, HashAlgorithmName.SHA256, 32);
                return CryptographicOperations.FixedTimeEquals(key, Convert.FromBase64String(p[3]));
            }
            catch (FormatException) { return false; }
        }
        if (stored.Length == 64 && stored.All(Uri.IsHexDigit))
            return string.Equals(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))), stored, StringComparison.OrdinalIgnoreCase);
        return password == stored;
    }
}
