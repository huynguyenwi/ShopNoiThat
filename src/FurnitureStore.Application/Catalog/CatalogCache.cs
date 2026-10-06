using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace FurnitureStore.Application.Catalog;

/// <summary>
/// Short-lived cache for data that is read on every page (category menu, filter options, home sections).
/// Admin changes call <see cref="Invalidate"/> so customers see updates immediately.
/// </summary>
public sealed class CatalogCache(IMemoryCache cache)
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);
    private CancellationTokenSource _reset = new();
    private readonly object _lock = new();

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory)
    {
        if (cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        var value = await factory();
        CancellationToken token;
        lock (_lock)
        {
            token = _reset.Token;
        }

        cache.Set(key, value, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = DefaultLifetime,
            ExpirationTokens = { new CancellationChangeToken(token) }
        });
        return value;
    }

    /// <summary>Drops every cached catalog entry.</summary>
    public void Invalidate()
    {
        CancellationTokenSource old;
        lock (_lock)
        {
            old = _reset;
            _reset = new CancellationTokenSource();
        }

        old.Cancel();
        old.Dispose();
    }
}
