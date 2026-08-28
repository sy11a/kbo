using Kbo.Adapters;

namespace Kbo.Tests;

public class WikilinksTests
{
    [Fact]
    public void CountDistinct_NormalizesAliasesAnchorsAndDedupes()
    {
        string body = "[[Alpha]] [[Alpha|shown differently]] [[Alpha#section]] [[Beta#heading]] ![[Gamma]]";

        Assert.Equal(3, Wikilinks.CountDistinct(body));
    }

    [Fact]
    public void CountDistinct_TrimsTargetWhitespace()
    {
        Assert.Equal(1, Wikilinks.CountDistinct("[[ Alpha ]] [[Alpha]]"));
        Assert.Equal(1, Wikilinks.CountDistinct("[[ Beta | alias ]] [[Beta]]"));
    }

    [Fact]
    public void CountDistinct_IgnoresBareAnchorSelfReferences()
    {
        Assert.Equal(0, Wikilinks.CountDistinct("see [[#next-section]]"));
    }

    [Fact]
    public void CountDistinct_ReturnsZeroWithoutWikilinks()
    {
        Assert.Equal(0, Wikilinks.CountDistinct("plain body with a [markdown](link.md) and no wikilinks"));
        Assert.Equal(0, Wikilinks.CountDistinct(""));
    }

    [Fact]
    public void CountDistinct_KeepsAliasBeforeAnchorStripping()
    {
        Assert.Equal(1, Wikilinks.CountDistinct("[[Alpha#section|alias]] [[Alpha]]"));
    }
}
