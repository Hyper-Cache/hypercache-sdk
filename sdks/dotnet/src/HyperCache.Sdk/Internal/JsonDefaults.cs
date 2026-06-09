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

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        return options;
    }
}
