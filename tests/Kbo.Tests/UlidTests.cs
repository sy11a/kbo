using System.Globalization;
using System.Text.RegularExpressions;
using Kbo.Bronze;

namespace Kbo.Tests;

public partial class UlidTests
{
    private sealed class SequenceUlidEntropy : IUlidEntropy
    {
        private byte _counter;

        public void Fill(Span<byte> buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = _counter++;
            }
        }
    }

    [Fact]
    public void NewUlid_MatchesEnvelopeSchemaPattern()
    {
        string ulid = Ulid.New(DateTimeOffset.Parse("2026-08-11T12:00:00Z", CultureInfo.InvariantCulture), new SequenceUlidEntropy());

        Assert.Matches(EnvelopeIdPattern, ulid);
    }

    [Fact]
    public void NewUlid_LaterTimestamp_SortsLexicographicallyAfter()
    {
        SequenceUlidEntropy entropy = new();
        string earlier = Ulid.New(DateTimeOffset.Parse("2026-08-11T12:00:00Z", CultureInfo.InvariantCulture), entropy);
        string later = Ulid.New(DateTimeOffset.Parse("2026-08-11T12:00:01Z", CultureInfo.InvariantCulture), entropy);

        Assert.True(string.CompareOrdinal(earlier, later) < 0);
    }

    [Fact]
    public void NewUlid_SameInstant_ProducesDistinctIds()
    {
        SequenceUlidEntropy entropy = new();
        DateTimeOffset instant = DateTimeOffset.Parse("2026-08-11T12:00:00Z", CultureInfo.InvariantCulture);

        Assert.NotEqual(Ulid.New(instant, entropy), Ulid.New(instant, entropy), StringComparer.Ordinal);
    }

    [Fact]
    public void NewUlid_KnownTimestamp_EncodesTimePrefix()
    {
        string ulid = Ulid.New(DateTimeOffset.FromUnixTimeMilliseconds(0), new SequenceUlidEntropy());

        Assert.StartsWith("0000000000", ulid, StringComparison.Ordinal);
    }

    [Fact]
    public void NewUlid_SameEntropySequenceAndTime_ProducesTheSameId()
    {
        DateTimeOffset instant = DateTimeOffset.Parse("2026-08-11T12:00:00Z", CultureInfo.InvariantCulture);

        string first = Ulid.New(instant, new SequenceUlidEntropy());
        string second = Ulid.New(instant, new SequenceUlidEntropy());

        Assert.Equal(first, second, StringComparer.Ordinal);
    }

    [Fact]
    public void NewUlid_WithCryptographicEntropy_MatchesEnvelopeSchemaPattern()
    {
        string ulid = Ulid.New(DateTimeOffset.Parse("2026-08-11T12:00:00Z", CultureInfo.InvariantCulture), CryptographicUlidEntropy.Instance);

        Assert.Equal(26, ulid.Length);
        Assert.Matches(EnvelopeIdPattern, ulid);
    }

    [Fact]
    public void NewUlid_WithCryptographicEntropy_SameInstant_ProducesDistinctIds()
    {
        DateTimeOffset instant = DateTimeOffset.Parse("2026-08-11T12:00:00Z", CultureInfo.InvariantCulture);

        Assert.NotEqual(
            Ulid.New(instant, CryptographicUlidEntropy.Instance),
            Ulid.New(instant, CryptographicUlidEntropy.Instance),
            StringComparer.Ordinal);
    }

    [GeneratedRegex("^[0-9A-HJKMNP-TV-Z]{26}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EnvelopeIdPattern { get; }
}
