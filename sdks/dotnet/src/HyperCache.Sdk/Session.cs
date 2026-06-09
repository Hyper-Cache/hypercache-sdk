using System;
using System.Threading;
using System.Threading.Tasks;

namespace HyperCache;

/// <summary>
/// Chain-aware wrapper around <see cref="Client"/> that tracks previous fingerprints and run scope.
/// </summary>
public sealed class Session
{
    private readonly Client _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="Session"/> class.
    /// </summary>
    public Session(Client client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Gets the previous fingerprint bytes for chain-aware operations.
    /// </summary>
    public byte[]? Prev { get; private set; }

    /// <summary>
    /// Gets the current run identifier.
    /// </summary>
    public string? Run { get; private set; }

    /// <summary>
    /// Resets the session chain state.
    /// </summary>
    public void Reset()
    {
        Prev = null;
    }

    /// <summary>
    /// Generates a fingerprint and updates the session chain.
    /// </summary>
    public Task<FingerprintResult> FingerprintAsync(
        ReadOnlyMemory<byte> data,
        FingerprintOptions? options = null,
        CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Executes an operation with the specified run identifier and restores the prior run afterward.
    /// </summary>
    public async Task<T> WithRunAsync<T>(
        string run,
        Func<Session, Task<T>> action)
    {
        if (run is null)
        {
            throw new ArgumentNullException(nameof(run));
        }

        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        string? previousRun = Run;
        Run = run;

        try
        {
            return await action(this).ConfigureAwait(false);
        }
        finally
        {
            Run = previousRun;
        }
    }
}
