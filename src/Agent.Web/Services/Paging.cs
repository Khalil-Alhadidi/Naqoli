namespace Agent.Web.Services;

/// <summary>
/// One page of a list a console grid has already loaded (UI pass, task 4a). <see cref="Page"/> is 1-based;
/// <see cref="First"/> and <see cref="Last"/> are the 1-based positions of the page's first and last rows in the whole
/// list, both 0 when the list is empty.
/// </summary>
public sealed record PagedList<T>(IReadOnlyList<T> Rows, int Page, int PageCount, int Total, int First, int Last)
{
    /// <summary>
    /// The page <paramref name="page"/> of <paramref name="rows"/>, <paramref name="pageSize"/> rows to a page. A page
    /// beyond the end is the last page (so a list that shrank after a delete keeps a valid page), a page below 1 is the
    /// first, and an empty list is one empty page.
    /// </summary>
    public static PagedList<T> Of(IReadOnlyList<T> rows, int page, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var total = rows.Count;
        if (total == 0)
            return new PagedList<T>([], 1, 1, 0, 0, 0);

        var pageCount = (total + pageSize - 1) / pageSize;
        var current = Math.Clamp(page, 1, pageCount);
        var skip = (current - 1) * pageSize;
        var take = Math.Min(pageSize, total - skip);
        return new PagedList<T>([.. rows.Skip(skip).Take(take)], current, pageCount, total, skip + 1, skip + take);
    }
}
