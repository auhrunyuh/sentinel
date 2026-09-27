namespace OrderService.Domain;

public sealed record Page<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalPages);

public static class Paging
{
    public static int TotalPages(int count, int pageSize) => (count + pageSize - 1) / pageSize;

    public static Page<T> Paginate<T>(IReadOnlyList<T> source, int pageNumber, int pageSize)
    {
        if (pageNumber < 1) throw new ArgumentOutOfRangeException(nameof(pageNumber));
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize));
        var items = source.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
        return new(items, pageNumber, pageSize, TotalPages(source.Count, pageSize));
    }
}
