using Xunit;

namespace HyperCache.Tests;

/// <summary>
/// Groups tests that mutate process environment variables so they run serially and do not
/// interfere with one another (or with tests that observe ambient configuration).
/// </summary>
[CollectionDefinition(EnvironmentVariableTests.CollectionName, DisableParallelization = true)]
public sealed class EnvironmentVariableGroup
{
    // Marker class for the xUnit collection definition; intentionally has no members.
}
