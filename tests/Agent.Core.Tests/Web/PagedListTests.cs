extern alias AgentWeb;

using AgentWeb::Agent.Web.Services;

namespace Agent.Core.Tests.Web;

/// <summary>
/// The shared paging helper behind every console grid (UI pass, task 4a): one page of a loaded list, 1-based, a page
/// beyond the end clamped to the last, and an empty list shown as one empty page.
/// </summary>
public class PagedListTests
{
    private static IReadOnlyList<int> Rows(int count) => [.. Enumerable.Range(1, count)];

    [Fact]
    public void ASeedSizedListPagesByTen()
    {
        var rows = Rows(42);

        var first = PagedList<int>.Of(rows, page: 1, pageSize: 10);
        var last = PagedList<int>.Of(rows, page: 5, pageSize: 10);

        Assert.Equal(5, first.PageCount);
        Assert.Equal(42, first.Total);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], first.Rows);
        Assert.Equal((1, 1, 10), (first.Page, first.First, first.Last));
        Assert.Equal([41, 42], last.Rows);
        Assert.Equal((5, 41, 42), (last.Page, last.First, last.Last));
    }

    [Fact]
    public void APageBeyondTheEndClampsToTheLast()
    {
        var paged = PagedList<int>.Of(Rows(42), page: 9, pageSize: 10);

        Assert.Equal(5, paged.Page);
        Assert.Equal([41, 42], paged.Rows);
        Assert.Equal((41, 42), (paged.First, paged.Last));
    }

    [Fact]
    public void AnEmptyListIsOneEmptyPage()
    {
        var paged = PagedList<int>.Of([], page: 3, pageSize: 10);

        Assert.Empty(paged.Rows);
        Assert.Equal((1, 1, 0, 0, 0), (paged.Page, paged.PageCount, paged.Total, paged.First, paged.Last));
    }

    [Fact]
    public void AShrunkListAfterADeleteKeepsAValidPage()
    {
        // Page 3 of 21 rows holds only row 21; deleting it leaves 20 rows, two pages, and page 3 becomes page 2.
        Assert.Equal([21], PagedList<int>.Of(Rows(21), page: 3, pageSize: 10).Rows);

        var paged = PagedList<int>.Of(Rows(20), page: 3, pageSize: 10);

        Assert.Equal((2, 2, 11, 20), (paged.Page, paged.PageCount, paged.First, paged.Last));
        Assert.Equal(10, paged.Rows.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void APageSizeBelowOneIsRefused(int pageSize)
        => Assert.Throws<ArgumentOutOfRangeException>(() => PagedList<int>.Of(Rows(3), page: 1, pageSize));

    [Fact]
    public void APageBelowOneIsTheFirst()
        => Assert.Equal(1, PagedList<int>.Of(Rows(30), page: 0, pageSize: 10).Page);
}
