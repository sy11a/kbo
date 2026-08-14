using Kbo.Schemas;

namespace Kbo.Tests;

/// <summary>
/// Proves the CI gate can actually reject: every fixture under fixtures/broken/
/// is deliberately invalid and MUST fail validation (plan step 1.2 acceptance).
/// </summary>
public class BrokenFixtureTests
{
    private static readonly EventValidator Validator = new();

    private static string BrokenDirectory => Path.Combine(AppContext.BaseDirectory, "fixtures", "broken");

    public static TheoryData<string, int, string> BrokenEvents()
    {
        TheoryData<string, int, string> data = new();
        foreach (string file in Directory.EnumerateFiles(BrokenDirectory, "*.ndjson").Order())
        {
            string[] lines = File.ReadAllLines(file);
            for (int lineNumber = 1; lineNumber <= lines.Length; lineNumber++)
            {
                if (!string.IsNullOrWhiteSpace(lines[lineNumber - 1]))
                {
                    data.Add(Path.GetFileName(file), lineNumber, lines[lineNumber - 1]);
                }
            }
        }
        return data;
    }

    [Fact]
    public void Broken_fixtures_exist()
    {
        Assert.NotEmpty(BrokenEvents());
    }

    [Theory]
    [MemberData(nameof(BrokenEvents))]
    public void Every_broken_fixture_fails_validation(string file, int lineNumber, string eventJson)
    {
        EventValidationResult result = Validator.Validate(eventJson);

        Assert.False(result.IsValid, $"{file}:{lineNumber} validated but is deliberately broken — the gate is not working.");
        Assert.NotEmpty(result.Errors);
    }
}
