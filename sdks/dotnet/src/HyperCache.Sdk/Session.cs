using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HyperCache.Internal;

namespace HyperCache;

/// <summary>
/// Chain-aware wrapper around <see cref="Client"/> that tracks previous fingerprints and run scope.
/// </summary>
/// <remarks>
/// A <see cref="Session"/> automatically threads the previous fingerprint (<see cref="Prev"/>) into
/// fingerprint and lookup operations and attaches the current run (<see cref="Run"/>) to put and list
/// operations. Callers may still override either value explicitly per call.
/// </remarks>
public sealed class Session
{
    private readonly Client _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="Session"/> class.
    /// </summary>
    /// <param name="client">The underlying client used to issue requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    public Session(Client client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Gets the previous fingerprint bytes used to chain subsequent fingerprint and lookup operations.
    /// </summary>
    public byte[]? Prev { get; private set; }

    /// <summary>
    /// Gets the current run identifier attached to put and list operations.
    /// </summary>
    public string? Run { get; private set; }

    /// <summary>
    /// Resets the session chain state by clearing <see cref="Prev"/>. The current <see cref="Run"/> is preserved.
    /// </summary>
    public void Reset()
    {
        Prev = null;
    }

    /// <summary>
    /// Generates a fingerprint, threading the session's previous fingerprint, and updates the chain.
    /// </summary>
    /// <param name="data">The bytes to fingerprint.</param>
    /// <param name="options">Optional fingerprint options. The session's previous fingerprint is used when none is supplied.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The fingerprint result.</returns>
    public async Task<FingerprintResult> FingerprintAsync(
        ReadOnlyMemory<byte> data,
        FingerprintOptions? options = null,
        CancellationToken ct = default)
    {
        FingerprintOptions effective = ApplyPrev(options);

        FingerprintResult result = await _client
            .FingerprintAsync(data, effective, ct)
            .ConfigureAwait(false);

        if (result.Record is { Length: > 0 })
        {
            Prev = result.Record;
        }

        return result;
    }

    /// <summary>
    /// Generates a fingerprint for the supplied bytes, threading the session's previous fingerprint.
    /// </summary>
    /// <param name="data">The bytes to fingerprint.</param>
    /// <param name="options">Optional fingerprint options.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The fingerprint result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    public Task<FingerprintResult> FingerprintAsync(
        byte[] data,
        FingerprintOptions? options = null,
        CancellationToken ct = default)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return FingerprintAsync(new ReadOnlyMemory<byte>(data), options, ct);
    }

    /// <summary>
    /// Generates a fingerprint for the supplied UTF-8 string, threading the session's previous fingerprint.
    /// </summary>
    /// <param name="data">The string to fingerprint.</param>
    /// <param name="options">Optional fingerprint options.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The fingerprint result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    public Task<FingerprintResult> FingerprintAsync(
        string data,
        FingerprintOptions? options = null,
        CancellationToken ct = default)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return FingerprintAsync(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(data)), options, ct);
    }

    /// <summary>
    /// Looks up a value, threading the session's previous fingerprint, and updates the chain on success.
    /// </summary>
    /// <param name="data">The bytes to look up.</param>
    /// <param name="options">Optional fingerprint options. The session's previous fingerprint is used when none is supplied.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The lookup result.</returns>
    public async Task<CacheLookupResult> CacheLookupAsync(
        ReadOnlyMemory<byte> data,
        FingerprintOptions? options = null,
        CancellationToken ct = default)
    {
        FingerprintOptions effective = ApplyPrev(options);

        CacheLookupResult result = await _client
            .CacheLookupAsync(data, effective, ct)
            .ConfigureAwait(false);

        if (!string.IsNullOrEmpty(result.FingerprintHex))
        {
            Prev = HexConvert.FromHex(result.FingerprintHex);
        }

        return result;
    }

    /// <summary>
    /// Looks up a value for the supplied bytes, threading the session's previous fingerprint.
    /// </summary>
    /// <param name="data">The bytes to look up.</param>
    /// <param name="options">Optional fingerprint options.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The lookup result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    public Task<CacheLookupResult> CacheLookupAsync(
        byte[] data,
        FingerprintOptions? options = null,
        CancellationToken ct = default)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return CacheLookupAsync(new ReadOnlyMemory<byte>(data), options, ct);
    }

    /// <summary>
    /// Looks up a value for the supplied UTF-8 string, threading the session's previous fingerprint.
    /// </summary>
    /// <param name="data">The string to look up.</param>
    /// <param name="options">Optional fingerprint options.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The lookup result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    public Task<CacheLookupResult> CacheLookupAsync(
        string data,
        FingerprintOptions? options = null,
        CancellationToken ct = default)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return CacheLookupAsync(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(data)), options, ct);
    }

    /// <summary>
    /// Stores bytes under the supplied fingerprint, attaching the session's current run when none is supplied.
    /// </summary>
    /// <param name="fingerprint">The hexadecimal fingerprint to store under.</param>
    /// <param name="data">The bytes to store.</param>
    /// <param name="options">Optional put options. The session's current run is attached when none is supplied.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The put result.</returns>
    public Task<CachePutResult> CachePutAsync(
        string fingerprint,
        ReadOnlyMemory<byte> data,
        CachePutOptions? options = null,
        CancellationToken ct = default)
    {
        return _client.CachePutAsync(fingerprint, data, ApplyRun(options), ct);
    }

    /// <summary>
    /// Stores bytes under the supplied fingerprint, attaching the session's current run when none is supplied.
    /// </summary>
    /// <param name="fingerprint">The hexadecimal fingerprint to store under.</param>
    /// <param name="data">The bytes to store.</param>
    /// <param name="options">Optional put options.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The put result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    public Task<CachePutResult> CachePutAsync(
        string fingerprint,
        byte[] data,
        CachePutOptions? options = null,
        CancellationToken ct = default)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return CachePutAsync(fingerprint, new ReadOnlyMemory<byte>(data), options, ct);
    }

    /// <summary>
    /// Stores a UTF-8 string under the supplied fingerprint, attaching the session's current run when none is supplied.
    /// </summary>
    /// <param name="fingerprint">The hexadecimal fingerprint to store under.</param>
    /// <param name="data">The string to store.</param>
    /// <param name="options">Optional put options.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The put result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    public Task<CachePutResult> CachePutAsync(
        string fingerprint,
        string data,
        CachePutOptions? options = null,
        CancellationToken ct = default)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return CachePutAsync(fingerprint, new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(data)), options, ct);
    }

    /// <summary>
    /// Lists cache entries, attaching the session's current run when none is supplied.
    /// </summary>
    /// <param name="options">Optional list filters. The session's current run is attached when none is supplied.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The grouped list response.</returns>
    public Task<CacheListResponse> CacheListAsync(
        CacheListOptions? options = null,
        CancellationToken ct = default)
    {
        return _client.CacheListAsync(ApplyListRun(options), ct);
    }

    /// <summary>
    /// Executes an operation with the specified run identifier and restores the prior run afterward.
    /// </summary>
    /// <typeparam name="T">The result type produced by the callback.</typeparam>
    /// <param name="run">The run identifier to apply for the duration of the callback.</param>
    /// <param name="action">The callback to invoke with this session.</param>
    /// <returns>The result produced by the callback.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> or <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="run"/> is empty or whitespace.</exception>
    public async Task<T> WithRunAsync<T>(
        string run,
        Func<Session, Task<T>> action)
    {
        ValidateRun(run);

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

    /// <summary>
    /// Executes an operation with the specified run identifier and restores the prior run afterward.
    /// </summary>
    /// <param name="run">The run identifier to apply for the duration of the callback.</param>
    /// <param name="action">The callback to invoke with this session.</param>
    /// <returns>A task that completes when the callback completes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> or <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="run"/> is empty or whitespace.</exception>
    public async Task WithRunAsync(
        string run,
        Func<Session, Task> action)
    {
        ValidateRun(run);

        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        string? previousRun = Run;
        Run = run;

        try
        {
            await action(this).ConfigureAwait(false);
        }
        finally
        {
            Run = previousRun;
        }
    }

    private static void ValidateRun(string run)
    {
        if (run is null)
        {
            throw new ArgumentNullException(nameof(run));
        }

        if (string.IsNullOrWhiteSpace(run))
        {
            throw new ArgumentException("Run must not be empty or whitespace.", nameof(run));
        }
    }

    private FingerprintOptions ApplyPrev(FingerprintOptions? options)
    {
        // Caller-supplied Prev/PrevHex always wins; otherwise thread the session chain.
        if (options is null)
        {
            return Prev is null
                ? new FingerprintOptions()
                : new FingerprintOptions { Prev = new ReadOnlyMemory<byte>(Prev) };
        }

        bool callerHasPrev = options.PrevHex is { Length: > 0 } || options.Prev.HasValue;
        if (callerHasPrev || Prev is null)
        {
            return options;
        }

        return new FingerprintOptions
        {
            Layers = options.Layers,
            NTok = options.NTok,
            Prev = new ReadOnlyMemory<byte>(Prev),
            PrevHex = options.PrevHex,
        };
    }

    private CachePutOptions? ApplyRun(CachePutOptions? options)
    {
        if (Run is null)
        {
            return options;
        }

        if (options is null)
        {
            return new CachePutOptions { Run = Run };
        }

        // Respect an explicitly supplied run; otherwise attach the session run.
        if (options.Run is not null)
        {
            return options;
        }

        return new CachePutOptions
        {
            Ttl = options.Ttl,
            Label = options.Label,
            Run = Run,
        };
    }

    private CacheListOptions? ApplyListRun(CacheListOptions? options)
    {
        if (Run is null)
        {
            return options;
        }

        if (options is null)
        {
            return new CacheListOptions { Run = Run };
        }

        if (options.Run is not null)
        {
            return options;
        }

        return new CacheListOptions
        {
            Bucket = options.Bucket,
            Part = options.Part,
            Limit = options.Limit,
            Run = Run,
            LabelPrefix = options.LabelPrefix,
            Cursor = options.Cursor,
        };
    }
}
