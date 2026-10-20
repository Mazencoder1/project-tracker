using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ProjectTracker.Web.Services;

/// <summary>
/// Password storage, compatible with the hashes already in profiles.password_hash:
///   scrypt$N$r$p$salt$key       (base64url; current format)
///   sha512$saltHex$hashHex      (transitional format written by the SQL install script)
///   plain text                  (legacy; accepted once and re-hashed to scrypt on the next login)
/// </summary>
public static class PasswordHasher
{
    private const string ScryptPrefix = "scrypt";
    private const string SqlSha512Prefix = "sha512";
    private const int N = 16_384;
    private const int R = 8;
    private const int P = 1;
    private const int SaltBytes = 16;
    private const int KeyBytes = 64;

    public readonly record struct Verification(bool Valid, bool NeedsRehash);

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Scrypt(Encoding.UTF8.GetBytes(password), salt, N, R, P, KeyBytes);
        return string.Join('$', ScryptPrefix, N, R, P, Base64Url(salt), Base64Url(key));
    }

    public static Verification Verify(string? stored, string password)
    {
        if (string.IsNullOrEmpty(stored)) return new(false, false);

        var parts = stored.Split('$');
        if (parts[0] == ScryptPrefix && parts.Length == 6)
        {
            if (!int.TryParse(parts[1], out var n) || !int.TryParse(parts[2], out var r) || !int.TryParse(parts[3], out var p)
                || n != N || r != R || p != P)
                return new(false, false);
            try
            {
                var salt = FromBase64Url(parts[4]);
                var expected = FromBase64Url(parts[5]);
                var actual = Scrypt(Encoding.UTF8.GetBytes(password), salt, N, R, P, KeyBytes);
                return new(CryptographicOperations.FixedTimeEquals(expected, actual), false);
            }
            catch (FormatException)
            {
                return new(false, false);
            }
        }

        if (parts[0] == SqlSha512Prefix && parts.Length == 3)
        {
            try
            {
                var salt = Convert.FromHexString(parts[1]);
                var expected = Convert.FromHexString(parts[2]);
                if (salt.Length != SaltBytes || expected.Length != KeyBytes) return new(false, false);
                var actual = SHA512.HashData([.. salt, .. Encoding.Unicode.GetBytes(password)]);
                var valid = CryptographicOperations.FixedTimeEquals(expected, actual);
                return new(valid, valid);
            }
            catch (FormatException)
            {
                return new(false, false);
            }
        }

        // Transitional support for a database the SQL install script has not been run on yet.
        var stored256 = SHA256.HashData(Encoding.UTF8.GetBytes(stored));
        var given256 = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        var ok = CryptographicOperations.FixedTimeEquals(stored256, given256);
        return new(ok, ok);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }

    // ── scrypt (RFC 7914) — .NET has no built-in implementation ───────────────
    private static byte[] Scrypt(byte[] password, byte[] salt, int n, int r, int p, int dkLen)
    {
        var blockSize = 128 * r;
        var b = Pbkdf2Sha256SingleIteration(password, salt, p * blockSize);
        var x = new uint[32 * r];
        var v = new uint[32 * r * n];
        var scratch = new uint[32 * r];
        for (var i = 0; i < p; i++)
        {
            var chunk = b.AsSpan(i * blockSize, blockSize);
            for (var k = 0; k < x.Length; k++) x[k] = BinaryPrimitives.ReadUInt32LittleEndian(chunk[(k * 4)..]);
            RoMix(x, v, scratch, n, r);
            for (var k = 0; k < x.Length; k++) BinaryPrimitives.WriteUInt32LittleEndian(chunk[(k * 4)..], x[k]);
        }
        return Pbkdf2Sha256SingleIteration(password, b, dkLen);
    }

    /// <summary>
    /// PBKDF2-HMAC-SHA256 with c = 1, exactly as RFC 7914 §6 prescribes inside scrypt (the password-hardening work is in
    /// the memory-hard RoMix step, N = 16384). Written out so it is not mistaken for a stand-alone low-iteration PBKDF2.
    /// </summary>
    private static byte[] Pbkdf2Sha256SingleIteration(byte[] password, byte[] salt, int length)
    {
        using var hmac = new HMACSHA256(password);
        var output = new byte[length];
        var block = new byte[salt.Length + 4];
        salt.CopyTo(block, 0);
        for (var i = 1; (i - 1) * 32 < length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(block.AsSpan(salt.Length), (uint)i);
            var u1 = hmac.ComputeHash(block);
            Array.Copy(u1, 0, output, (i - 1) * 32, Math.Min(32, length - (i - 1) * 32));
        }
        return output;
    }

    private static void RoMix(uint[] x, uint[] v, uint[] scratch, int n, int r)
    {
        var len = 32 * r;
        for (var i = 0; i < n; i++)
        {
            Array.Copy(x, 0, v, i * len, len);
            BlockMix(x, scratch, r);
        }
        for (var i = 0; i < n; i++)
        {
            var j = (int)(x[(2 * r - 1) * 16] & (uint)(n - 1));
            for (var k = 0; k < len; k++) x[k] ^= v[j * len + k];
            BlockMix(x, scratch, r);
        }
    }

    private static void BlockMix(uint[] b, uint[] y, int r)
    {
        var x = new uint[16];
        Array.Copy(b, (2 * r - 1) * 16, x, 0, 16);
        for (var i = 0; i < 2 * r; i++)
        {
            for (var k = 0; k < 16; k++) x[k] ^= b[i * 16 + k];
            Salsa208(x);
            // Even blocks go to the first half of the output, odd blocks to the second half.
            Array.Copy(x, 0, y, ((i & 1) * r + (i >> 1)) * 16, 16);
        }
        Array.Copy(y, b, 32 * r);
    }

    private static void Salsa208(uint[] b)
    {
        var x = (uint[])b.Clone();
        for (var i = 0; i < 4; i++)
        {
            x[4] ^= Rotl(x[0] + x[12], 7); x[8] ^= Rotl(x[4] + x[0], 9);
            x[12] ^= Rotl(x[8] + x[4], 13); x[0] ^= Rotl(x[12] + x[8], 18);
            x[9] ^= Rotl(x[5] + x[1], 7); x[13] ^= Rotl(x[9] + x[5], 9);
            x[1] ^= Rotl(x[13] + x[9], 13); x[5] ^= Rotl(x[1] + x[13], 18);
            x[14] ^= Rotl(x[10] + x[6], 7); x[2] ^= Rotl(x[14] + x[10], 9);
            x[6] ^= Rotl(x[2] + x[14], 13); x[10] ^= Rotl(x[6] + x[2], 18);
            x[3] ^= Rotl(x[15] + x[11], 7); x[7] ^= Rotl(x[3] + x[15], 9);
            x[11] ^= Rotl(x[7] + x[3], 13); x[15] ^= Rotl(x[11] + x[7], 18);
            x[1] ^= Rotl(x[0] + x[3], 7); x[2] ^= Rotl(x[1] + x[0], 9);
            x[3] ^= Rotl(x[2] + x[1], 13); x[0] ^= Rotl(x[3] + x[2], 18);
            x[6] ^= Rotl(x[5] + x[4], 7); x[7] ^= Rotl(x[6] + x[5], 9);
            x[4] ^= Rotl(x[7] + x[6], 13); x[5] ^= Rotl(x[4] + x[7], 18);
            x[11] ^= Rotl(x[10] + x[9], 7); x[8] ^= Rotl(x[11] + x[10], 9);
            x[9] ^= Rotl(x[8] + x[11], 13); x[10] ^= Rotl(x[9] + x[8], 18);
            x[12] ^= Rotl(x[15] + x[14], 7); x[13] ^= Rotl(x[12] + x[15], 9);
            x[14] ^= Rotl(x[13] + x[12], 13); x[15] ^= Rotl(x[14] + x[13], 18);
        }
        for (var i = 0; i < 16; i++) b[i] += x[i];
    }

    private static uint Rotl(uint a, int b) => (a << b) | (a >> (32 - b));
}
