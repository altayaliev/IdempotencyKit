using System.Security.Cryptography;
using System.Text;

namespace IdempotencyKit;

/// <summary>Default fingerprint: SHA-256 over "{method}\n{path}\n{body}".</summary>
public sealed class Sha256IdempotencyFingerprintProvider : IIdempotencyFingerprintProvider
{
    private static readonly byte[] Newline = "\n"u8.ToArray();

    public string Compute(string method, string path, ReadOnlySpan<byte> body)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(method));
        hash.AppendData(Newline);
        hash.AppendData(Encoding.UTF8.GetBytes(path));
        hash.AppendData(Newline);
        hash.AppendData(body);
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
