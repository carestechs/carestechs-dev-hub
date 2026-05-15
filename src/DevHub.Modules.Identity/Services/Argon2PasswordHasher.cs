using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace DevHub.Modules.Identity.Services;

/// <summary>
/// Argon2id password hasher. Encoded form:
/// <c>argon2id$v=19$m=&lt;mem&gt;,t=&lt;iters&gt;,p=&lt;lanes&gt;$&lt;salt-b64&gt;$&lt;hash-b64&gt;</c>
/// </summary>
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    private const int MemoryKb = 65_536;     // 64 MiB
    private const int Iterations = 4;
    private const int Lanes = 2;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const string Prefix = "argon2id";
    private const int Version = 19;          // Argon2 v1.3

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Compute(password, salt, MemoryKb, Iterations, Lanes, HashBytes);

        return $"{Prefix}$v={Version}$m={MemoryKb},t={Iterations},p={Lanes}$" +
               $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string encoded)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(encoded))
            return false;

        if (!TryParse(encoded, out var p)) return false;

        var computed = Compute(password, p.Salt, p.Memory, p.Iterations, p.Lanes, p.Hash.Length);
        return CryptographicOperations.FixedTimeEquals(computed, p.Hash);
    }

    private static byte[] Compute(string password, byte[] salt, int memoryKb, int iterations, int lanes, int outputBytes)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKb,
            Iterations = iterations,
            DegreeOfParallelism = lanes,
        };
        return argon.GetBytes(outputBytes);
    }

    private static bool TryParse(string encoded, out (byte[] Salt, byte[] Hash, int Memory, int Iterations, int Lanes) p)
    {
        p = default;
        // argon2id$v=19$m=65536,t=4,p=2$<salt>$<hash>
        var parts = encoded.Split('$');
        if (parts.Length != 5 || parts[0] != Prefix) return false;
        if (!parts[1].StartsWith("v=") || !int.TryParse(parts[1][2..], out var v) || v != Version) return false;

        int memory = 0, iters = 0, lanes = 0;
        foreach (var kv in parts[2].Split(','))
        {
            var eq = kv.IndexOf('=');
            if (eq <= 0) return false;
            var key = kv[..eq];
            if (!int.TryParse(kv[(eq + 1)..], out var n)) return false;
            switch (key)
            {
                case "m": memory = n; break;
                case "t": iters = n; break;
                case "p": lanes = n; break;
                default: return false;
            }
        }
        if (memory <= 0 || iters <= 0 || lanes <= 0) return false;

        byte[] salt, hash;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            hash = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException) { return false; }

        p = (salt, hash, memory, iters, lanes);
        return true;
    }
}
