using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HyperCache.Tests;

/// <summary>
/// Tests for the static <see cref="HyperCacheClient"/> convenience surface.
/// </summary>
/// <remarks>
/// These tests intentionally avoid invoking the lazily initialized default client, which would
/// require live configuration and network access. They verify the public method shape instead so
/// the suite stays deterministic and never calls the real API.
/// </remarks>
public sealed class HyperCacheClientStaticTests
{
    [Fact]
    public void ExposesFingerprintAsyncOverloads()
    {
        MethodInfo[] methods = typeof(HyperCacheClient)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(HyperCacheClient.FingerprintAsync))
            .ToArray();

        Assert.Equal(3, methods.Length);
        Assert.All(methods, m => Assert.Equal(typeof(Task<FingerprintResult>), m.ReturnType));
    }

    [Fact]
    public void ExposesCachePutAsyncOverloads()
    {
        MethodInfo[] methods = typeof(HyperCacheClient)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(HyperCacheClient.CachePutAsync))
            .ToArray();

        Assert.Equal(3, methods.Length);
        Assert.All(methods, m => Assert.Equal(typeof(Task<CachePutResult>), m.ReturnType));
    }

    [Fact]
    public void ExposesCacheGetAsync()
    {
        MethodInfo? method = typeof(HyperCacheClient).GetMethod(
            nameof(HyperCacheClient.CacheGetAsync),
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<byte[]>), method!.ReturnType);
    }

    [Fact]
    public void ExposesCacheDeleteAsync()
    {
        MethodInfo? method = typeof(HyperCacheClient).GetMethod(
            nameof(HyperCacheClient.CacheDeleteAsync),
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
    }

    [Fact]
    public void IsStaticClass()
    {
        Type type = typeof(HyperCacheClient);

        Assert.True(type.IsAbstract && type.IsSealed, "HyperCacheClient should be a static class.");
    }
}
