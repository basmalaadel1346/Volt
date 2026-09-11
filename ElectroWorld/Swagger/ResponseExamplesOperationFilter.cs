using Microsoft.OpenApi.Models;
using Shared.Common.Api;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ElectroWorld.Swagger;

/// <summary>
/// Applies [SwaggerExample] to the response with the matching status code.
///
/// Previously this filter skipped any response whose Content was empty, which
/// is exactly why 401 and 403 showed nothing useful: they are declared as bare
/// [ProducesResponseType(StatusCodes.Status401Unauthorized)] with no body type,
/// so Swashbuckle gives them no Content to attach an example to.
///
/// It now SYNTHESIZES application/json content with the ApiResponse envelope
/// for those responses, so a hardcoded example appears. Documentation only —
/// this changes nothing at runtime.
/// </summary>
public class ResponseExamplesOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var attributes = context.MethodInfo
            .GetCustomAttributes(typeof(SwaggerExampleAttribute), inherit: true)
            .Cast<SwaggerExampleAttribute>();

        OpenApiSchema? envelope = null;

        foreach (var attribute in attributes)
        {
            var key = attribute.StatusCode.ToString();
            if (!operation.Responses.TryGetValue(key, out var response))
                continue;

            var example = SwaggerJson.Parse(attribute.Json);
            if (example is null)
                continue;   // malformed example — ignore it rather than break the doc

            // The fix: a declared-but-bodyless response gets JSON content built
            // for it, instead of being skipped.
            if (response.Content.Count == 0)
            {
                envelope ??= context.SchemaGenerator.GenerateSchema(
                    typeof(ApiResponse<object>), context.SchemaRepository);

                response.Content["application/json"] = new OpenApiMediaType
                {
                    Schema = envelope,
                    Example = example
                };

                continue;
            }

            foreach (var mediaType in response.Content.Values)
                mediaType.Example = example;
        }
    }
}
