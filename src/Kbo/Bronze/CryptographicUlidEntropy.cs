using System.Security.Cryptography;

namespace Kbo.Bronze;

/// <summary>
/// ULID entropy backed by the OS cryptographically secure random number
/// generator — the right primitive for ids that must not collide, even if they
/// don't need to be unguessable. Single shared instance; <c>Fill</c> delegates
/// straight to <c>RandomNumberGenerator.Fill</c>.
/// </summary>
internal sealed class CryptographicUlidEntropy : IUlidEntropy
{
    public static CryptographicUlidEntropy Instance { get; } = new();

    public void Fill(Span<byte> buffer) => RandomNumberGenerator.Fill(buffer);
}
