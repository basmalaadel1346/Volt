using Microsoft.OpenApi.Any;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ElectroWorld.Swagger;

internal static class SwaggerJson
{
    /// <summary>
    /// Parses an example into an OpenAPI value. A malformed example is dropped
    /// rather than breaking the whole Swagger document — a bad example must
    /// never take the docs page down.
    /// </summary>
    public static IOpenApiAny? Parse(string json)
    {
        try
        {
            return OpenApiAnyFactory.CreateFromJson(json);
        }
        catch
        {
            return null;
        }
    }
}
