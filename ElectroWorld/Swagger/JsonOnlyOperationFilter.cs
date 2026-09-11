using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ElectroWorld.Swagger;

/// <summary>
/// Forces application/json as the only documented media type. This is what
/// removes the stray text/plain and text/json entries Swashbuckle emits by
/// default for object results.
///
/// Register this LAST, so it also normalizes anything the other filters added.
///
/// multipart/form-data is deliberately preserved on request bodies — the image
/// upload endpoint genuinely consumes it, and stripping it would document an
/// endpoint that cannot be called.
/// </summary>
public class JsonOnlyOperationFilter : IOperationFilter
{
    private const string Json = "application/json";
    private const string Multipart = "multipart/form-data";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        foreach (var response in operation.Responses.Values)
            KeepOnly(response.Content, Json);

        if (operation.RequestBody?.Content is { Count: > 0 } requestContent
            && !requestContent.ContainsKey(Multipart))
        {
            KeepOnly(requestContent, Json);
        }
    }

    private static void KeepOnly(IDictionary<string, OpenApiMediaType>? content, string mediaType)
    {
        if (content is null || content.Count == 0)
            return;

        // Carry the schema/example across if the survivor is being introduced.
        if (!content.TryGetValue(mediaType, out var keep))
        {
            keep = content.Values.First();
            content[mediaType] = keep;
        }

        foreach (var key in content.Keys.Where(k => k != mediaType).ToList())
            content.Remove(key);
    }
}
