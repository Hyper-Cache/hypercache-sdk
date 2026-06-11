using System.Text.Json;

namespace HyperCache.Internal;

/// <summary>
/// Centralized <see cref="JsonSerializerOptions"/> for all HyperCache wire serialization.
/// </summary>
/// <remarks>
/// Wire property names use snake_case and are mapped explicitly via
/// <c>[JsonPropertyName]</c> attributes on the model types, so no global naming
/// policy is configured here. Reading is case-insensitive to tolerate minor
/// server variations. A single shared, reflection-based options instance is used
/// for both target frameworks to keep Phase 1 simple; source generation may be
/// layered on later without changing call sites.
/// </remarks>
internal static class JsonDefaults
{
    /// <summary>
    /// Gets the shared serializer options used by the SDK.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>
    /// Gets serializer options that include properties with <see langword="null"/> values
    /// when writing.
    /// </summary>
    /// <remarks>
    /// Used by requests that rely on explicit null-clear semantics (for example,
    /// relabel), where omitting a property and sending <c>null</c> mean different
    /// things to the API.
    /// </remarks>
    public static JsonSerializerOptions IncludeNullsOptions { get; } = CreateIncludeNullsOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        return options;
    }

    private static JsonSerializerOptions CreateIncludeNullsOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        };

        return options;
    }
}
