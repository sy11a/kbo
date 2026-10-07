namespace Kbo.Bronze;

/// <summary>
/// Source of the 10 random bytes that fill the random half of a ULID. Kept
/// behind an interface so the production path can pin it to the OS CSPRNG
/// (see <see cref="CryptographicUlidEntropy"/>) and still let tests supply a
/// deterministic stream — ULID randomness only needs uniqueness, not secrecy.
/// </summary>
internal interface IUlidEntropy
{
    public void Fill(Span<byte> buffer);
}
