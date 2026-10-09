namespace AffiVideo.Application;

/// <summary>One page of a list, counted from 1. Out-of-range values are brought into range rather than refused.</summary>
public readonly record struct PageRequest
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    // Far beyond any real list, and small enough that Skip cannot overflow.
    private const int MaxPage = int.MaxValue / MaxPageSize;

    public PageRequest(int? page, int? pageSize)
    {
        Page = Math.Clamp(page ?? 1, 1, MaxPage);
        PageSize = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
    }

    public int Page { get; }

    public int PageSize { get; }

    public int Skip => (Page - 1) * PageSize;
}

public sealed record Page<T>(IReadOnlyList<T> Items, int Number, int PageSize, int Total);
