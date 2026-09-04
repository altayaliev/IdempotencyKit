namespace Idempo;

/// <summary>
/// Computes a stable fingerprint for a request so the store can tell a legitimate replay
/// (same key, same request) apart from a reused key on a different request.
/// </summary>
public interface IIdempotencyFingerprintProvider
{
    string Compute(string method, string path, ReadOnlySpan<byte> body);
}
