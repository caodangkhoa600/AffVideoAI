using AffiVideo.Application;
using AffiVideo.Contracts;

namespace AffiVideo.Api;

internal static class PageResponses
{
    public static PagedResponse<TResponse> ToResponse<T, TResponse>(this Page<T> page, Func<T, TResponse> item) =>
        new(page.Items.Select(item).ToList(), page.Number, page.PageSize, page.Total);
}
