namespace AffiVideo.Contracts;

/// <summary>One page of a list. Pages are counted from 1; <c>Total</c> is the size of the whole list.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
