namespace HyperCache.Workflows;

/// <summary>
/// Represents the result of a cached computation performed by
/// <see cref="Pipeline.CachedAsync(string, System.ReadOnlyMemory{byte}, System.Func{System.Threading.Tasks.Task{string}}, int?, System.Threading.CancellationToken)"/>.
/// </summary>
/// <typeparam name="T">The type of the computed or cached value.</typeparam>
public readonly struct CachedResult<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CachedResult{T}"/> struct.
    /// </summary>
    /// <param name="value">The computed or cached value.</param>
    /// <param name="wasHit"><see langword="true"/> when the value came from the cache; otherwise <see langword="false"/>.</param>
    public CachedResult(T value, bool wasHit)
    {
        Value = value;
        WasHit = wasHit;
    }

    /// <summary>
    /// Gets the computed or cached value.
    /// </summary>
    public T Value { get; }

    /// <summary>
    /// Gets a value indicating whether the value was served from the cache.
    /// </summary>
    public bool WasHit { get; }
}
