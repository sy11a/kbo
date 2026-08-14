using System.Globalization;
using System.Text.RegularExpressions;
using Kbo.Bronze;

namespace Kbo.Tests;

public class UlidTests
{
    private static readonly Regex EnvelopeIdPattern = new("^[0-9A-HJKMNP-TV-Z]{26}$");

    [Fact]
    public void NewUlid_MatchesEnvelopeSchemaPattern()
    {
        string ulid = Ulid.New(DateTimeOffset.Parse("2026-08-11T12:00:00Z", CultureInfo.InvariantCulture), new Random(42));

        Assert.Matches(EnvelopeIdPattern, ulid);
    }

    [Fact]
    public void NewUlid_LaterTimestamp_SortsLexicographicallyAfter()
    {
        Random random = new(42);
        string earlier = Ulid.New(DateTimeOffset.Parse("2026-08-11T12:00:00Z", CultureInfo.InvariantCulture), random);
        string later = Ulid.New(DateTimeOffset.Parse("2026-08-11T12:00:01Z", CultureInfo.InvariantCulture), random);

        Assert.True(string.CompareOrdinal(earlier, later) < 0);
    }

    [Fact]
    public void NewUlid_SameInstant_ProducesDistinctIds()
    {
        Random random = new(42);
        DateTimeOffset instant = DateTimeOffset.Parse("2026-08-11T12:00:00Z", CultureInfo.InvariantCulture);

        Assert.NotEqual(Ulid.New(instant, random), Ulid.New(instant, random));
    }

    [Fact]
    public void NewUlid_KnownTimestamp_EncodesTimePrefix()
    {
        string ulid = Ulid.New(DateTimeOffset.FromUnixTimeMilliseconds(0), new Random(42));

        Assert.StartsWith("0000000000", ulid);
    }
}
