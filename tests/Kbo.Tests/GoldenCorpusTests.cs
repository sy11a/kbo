using Kbo.Schemas;

namespace Kbo.Tests;

public class GoldenCorpusTests
{
    private static readonly EventValidator Validator = new();

    private static string GoldenDirectory => Path.Combine(AppContext.BaseDirectory, "fixtures", "golden");

    public static TheoryData<string, int, string> GoldenEvents()
    {
        TheoryData<string, int, string> data = new();
        foreach (string file in Directory.EnumerateFiles(GoldenDirectory, "*.ndjson").Order())
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

    [Theory]
    [MemberData(nameof(GoldenEvents))]
    public void Every_golden_event_validates(string file, int lineNumber, string eventJson)
    {
        EventValidationResult result = Validator.Validate(eventJson);

        Assert.True(result.IsValid, $"{file}:{lineNumber} failed validation: {string.Join("; ", result.Errors)}");
    }

    [Fact]
    public void Every_schema_version_has_golden_coverage()
    {
        HashSet<string> coveredRefs = GoldenEvents()
            .Select(row => Path.GetFileNameWithoutExtension((string)row[0]))
            .Select(name => name[..name.LastIndexOf('.')] + "/" + name[(name.LastIndexOf('.') + 1)..])
            .ToHashSet();

        foreach (string knownRef in Validator.KnownSchemaRefs)
        {
            Assert.Contains(knownRef, coveredRefs);
        }
    }
}
