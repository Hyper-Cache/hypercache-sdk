using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HyperCache.Workflows;

/// <summary>
/// Records and caches a chain-aware HyperCache workflow.
/// </summary>
/// <remarks>
/// A <see cref="Pipeline"/> wraps a <see cref="Session"/> so that recorded steps automatically thread
/// the previous fingerprint and attach the run identifier. Call
/// <see cref="RecordAsync(string, System.ReadOnlyMemory{byte}, System.Threading.CancellationToken)"/> to record a
/// chain/audit step,
/// <see cref="CachedAsync(string, System.ReadOnlyMemory{byte}, System.Func{System.Threading.Tasks.Task{string}}, int?, System.Threading.CancellationToken)"/>
/// to memoize a computation, and <see cref="End"/> to obtain a <see cref="PipelineReport"/>.
/// </remarks>
public sealed class Pipeline : IDisposable
#if NET8_0_OR_GREATER
    , IAsyncDisposable
#endif
{
    private readonly Session _session;
    private readonly string? _run;
    private readonly List<PipelineStep> _steps = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Pipeline"/> class.
    /// </summary>
    /// <param name="client">The client used to issue requests. The pipeline does not own or dispose it.</param>
    /// <param name="run">An optional run identifier attached to put operations.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    public Pipeline(Client client, string? run = null)
    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        _session = new Session(client);
        _run = string.IsNullOrWhiteSpace(run) ? null : run;
    }

    /// <summary>
    /// Records a fingerprint step for chain and audit purposes.
    /// </summary>
    /// <param name="label">The step label.</param>
    /// <param name="data">The bytes to fingerprint.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The fingerprint result.</returns>
    /// <exception cref="ArgumentException"><paramref name="label"/> is <see langword="null"/>, empty, or whitespace.</exception>
    public async Task<FingerprintResult> RecordAsync(
        string label,
        ReadOnlyMemory<byte> data,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateLabel(label);

        var stopwatch = Stopwatch.StartNew();
        FingerprintResult result = await _session.FingerprintAsync(data, null, ct).ConfigureAwait(false);
        stopwatch.Stop();

        _steps.Add(new PipelineStep
        {
            Label = label,
            FingerprintHex = result.RecordHex,
            WasHit = false,
            OpsUsed = result.OpsUsed,
            OpsCap = result.OpsCap,
            OpsRemaining = result.OpsRemaining,
            ElapsedSeconds = stopwatch.Elapsed.TotalSeconds,
        });

        return result;
    }

    /// <summary>
    /// Records a fingerprint step for the supplied UTF-8 string.
    /// </summary>
    /// <param name="label">The step label.</param>
    /// <param name="data">The string to fingerprint.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The fingerprint result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    public Task<FingerprintResult> RecordAsync(
        string label,
        string data,
        CancellationToken ct = default)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return RecordAsync(label, new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(data)), ct);
    }

    /// <summary>
    /// Returns a cached value for the supplied input, computing and storing it on a cache miss.
    /// </summary>
    /// <param name="label">The step label, also stored as the cache entry label on a miss.</param>
    /// <param name="inputBytes">The input bytes used to look up (and on a miss, store) the value.</param>
    /// <param name="computeFn">The function invoked to compute the value on a cache miss.</param>
    /// <param name="ttl">An optional time-to-live in seconds applied when storing a computed value.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The cached or computed value together with a hit indicator.</returns>
    /// <exception cref="ArgumentException"><paramref name="label"/> is <see langword="null"/>, empty, or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="computeFn"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="computeFn"/> returns <see langword="null"/>.</exception>
    public async Task<CachedResult<string>> CachedAsync(
        string label,
        ReadOnlyMemory<byte> inputBytes,
        Func<Task<string>> computeFn,
        int? ttl = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateLabel(label);

        if (computeFn is null)
        {
            throw new ArgumentNullException(nameof(computeFn));
        }

        var stopwatch = Stopwatch.StartNew();

        CacheLookupResult lookup = await _session.CacheLookupAsync(inputBytes, null, ct).ConfigureAwait(false);

        if (lookup.Hit && lookup.Value is not null)
        {
            string cached = Encoding.UTF8.GetString(lookup.Value);
            stopwatch.Stop();

            _steps.Add(new PipelineStep
            {
                Label = label,
                FingerprintHex = lookup.FingerprintHex,
                WasHit = true,
                SizeBytes = lookup.Value.Length,
                OpsUsed = lookup.OpsUsed,
                OpsCap = lookup.OpsCap,
                OpsRemaining = lookup.OpsRemaining,
                ElapsedSeconds = stopwatch.Elapsed.TotalSeconds,
            });

            return new CachedResult<string>(cached, true);
        }

        string computed = await computeFn().ConfigureAwait(false);
        if (computed is null)
        {
            throw new InvalidOperationException("The compute function must not return null.");
        }

        byte[] valueBytes = Encoding.UTF8.GetBytes(computed);

        var putOptions = new CachePutOptions
        {
            Ttl = ttl,
            Label = label,
            Run = _run,
        };

        CachePutResult put = await _session
            .CachePutAsync(lookup.FingerprintHex, new ReadOnlyMemory<byte>(valueBytes), putOptions, ct)
            .ConfigureAwait(false);

        stopwatch.Stop();

        _steps.Add(new PipelineStep
        {
            Label = label,
            FingerprintHex = lookup.FingerprintHex,
            WasHit = false,
            SizeBytes = put.SizeBytes,
            OpsUsed = put.OpsUsed,
            OpsCap = put.OpsCap,
            OpsRemaining = put.OpsRemaining,
            ElapsedSeconds = stopwatch.Elapsed.TotalSeconds,
        });

        return new CachedResult<string>(computed, false);
    }

    /// <summary>
    /// Returns a cached value for the supplied UTF-8 input string, computing and storing it on a miss.
    /// </summary>
    /// <param name="label">The step label, also stored as the cache entry label on a miss.</param>
    /// <param name="input">The input string used to look up (and on a miss, store) the value.</param>
    /// <param name="computeFn">The function invoked to compute the value on a cache miss.</param>
    /// <param name="ttl">An optional time-to-live in seconds applied when storing a computed value.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The cached or computed value together with a hit indicator.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    public Task<CachedResult<string>> CachedAsync(
        string label,
        string input,
        Func<Task<string>> computeFn,
        int? ttl = null,
        CancellationToken ct = default)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        return CachedAsync(label, new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(input)), computeFn, ttl, ct);
    }

    /// <summary>
    /// Completes the pipeline and produces a summary report.
    /// </summary>
    /// <returns>The pipeline report.</returns>
    public PipelineReport End()
    {
        ThrowIfDisposed();

        int hits = 0;
        int misses = 0;
        double total = 0;
        foreach (PipelineStep step in _steps)
        {
            if (step.WasHit)
            {
                hits++;
            }
            else
            {
                misses++;
            }

            total += step.ElapsedSeconds;
        }

        string? chain = _session.Prev is { Length: > 0 }
            ? Internal.HexConvert.ToHex(_session.Prev)
            : null;

        return new PipelineReport
        {
            NSteps = _steps.Count,
            NHits = hits,
            NMisses = misses,
            TotalSeconds = total,
            Chain = chain,
            Steps = _steps.ToArray(),
        };
    }

    /// <summary>
    /// Completes the pipeline and produces a summary report.
    /// </summary>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The pipeline report.</returns>
    public Task<PipelineReport> EndAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(End());
    }

    /// <summary>
    /// Disposes pipeline-owned resources. The supplied <see cref="Client"/> is not disposed.
    /// </summary>
    public void Dispose()
    {
        // The pipeline does not own the externally supplied client, so nothing is disposed here.
        // Marking disposed prevents further use after Dispose.
        _disposed = true;
    }

#if NET8_0_OR_GREATER
    /// <summary>
    /// Asynchronously disposes pipeline-owned resources. The supplied <see cref="Client"/> is not disposed.
    /// </summary>
    /// <returns>A completed value task.</returns>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }
#endif

    private static void ValidateLabel(string label)
    {
        if (label is null)
        {
            throw new ArgumentNullException(nameof(label));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Label must not be empty or whitespace.", nameof(label));
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Pipeline));
        }
    }
}
