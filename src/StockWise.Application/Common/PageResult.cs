namespace StockWise.Application.Common;

using Microsoft.EntityFrameworkCore;

public class PageResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public long TotalCount { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class QueryableExtensions
{
    public const int MaxPageSize = 200;

    public static async Task<PageResult<T>> ToPageResultAsync<T>(this IQueryable<T> query, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        // Page sizes are bounded: at least 5 rows per page, at most MaxPageSize.
        pageSize = Math.Clamp(pageSize <= 0 ? 25 : pageSize, 5, MaxPageSize);
        var total = await query.LongCountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PageResult<T> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }
}
