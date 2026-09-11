using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Shared.Common.Api;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ElectroWorld.Swagger;

/// <summary>
/// Makes every documented failure use the shared ApiResponse envelope with a
/// concrete JSON example, and evicts the default ProblemDetails schema that
/// [ApiController] otherwise assigns to bare [ProducesResponseType(401)] style
/// declarations.
///
/// Three behaviours, in order of precedence:
///   1. An action that declares its own concrete type keeps it.
///   2. An action with a [SwaggerExample] keeps that example.
///   3. Everything else gets the envelope schema plus the default example below.
///
/// 500 is added to every operation. 401 only where authorization applies and 403
/// only where a role is required, so an anonymous endpoint is never documented as
/// returning them. 400/404/409 are completed when already declared but never
/// invented — an endpoint that cannot 409 should not claim it can.
/// </summary>
public class DefaultApiResponsesOperationFilter : IOperationFilter
{
    private const string Json = "application/json";

    // Schemas [ApiController] injects by convention, which we replace.
    private static readonly string[] ProblemDetailsSchemas =
        ["ProblemDetails", "ValidationProblemDetails", "HttpValidationProblemDetails"];

    private static readonly (string Code, string Description, string Example)[] CompletableErrors =
    [
        ("400", "طلب غير صالح", ApiResponseExamples.BadRequest),
        ("404", "العنصر المطلوب غير موجود", ApiResponseExamples.NotFound),
        ("409", "تعارض مع عملية أخرى", ApiResponseExamples.Conflict),
        ("422", "تحقق غير ناجح", ApiResponseExamples.BadRequest)
    ];

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var envelope = context.SchemaGenerator.GenerateSchema(
            typeof(ApiResponse<object>), context.SchemaRepository);

        var authorize = context.MethodInfo
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Concat(context.MethodInfo.DeclaringType?
                        .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                        .Cast<AuthorizeAttribute>()
                    ?? [])
            .ToList();

        var allowsAnonymous = context.MethodInfo
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).Length > 0;

        var requiresAuth = authorize.Count > 0 && !allowsAnonymous;
        var requiresRole = authorize.Any(a => !string.IsNullOrWhiteSpace(a.Roles));

        // Always documented: the API can always fail internally.
        Ensure(operation, "500", "خطأ داخلي في الخادم", envelope, ApiResponseExamples.ServerError, add: true);

        Ensure(operation, "401", "التوكن مفقود أو غير صالح", envelope,
            ApiResponseExamples.Unauthorized, add: requiresAuth);

        Ensure(operation, "403", "الصلاحية غير كافية", envelope,
            ApiResponseExamples.Forbidden, add: requiresRole);

        // Complete only — never invent these.
        foreach (var (code, description, example) in CompletableErrors)
            Ensure(operation, code, description, envelope, example, add: false);
    }

    private static void Ensure(
        OpenApiOperation operation,
        string statusCode,
        string description,
        OpenApiSchema envelope,
        string exampleJson,
        bool add)
    {
        if (!operation.Responses.TryGetValue(statusCode, out var response))
        {
            if (!add)
                return;

            operation.Responses[statusCode] = new OpenApiResponse
            {
                Description = description,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    [Json] = new() { Schema = envelope, Example = SwaggerJson.Parse(exampleJson) }
                }
            };

            return;
        }

        if (string.IsNullOrWhiteSpace(response.Description))
            response.Description = description;

        response.Content ??= new Dictionary<string, OpenApiMediaType>();

        if (!response.Content.TryGetValue(Json, out var json))
        {
            json = new OpenApiMediaType();
            response.Content[Json] = json;
        }

        // This is the line that fixes the reported symptom: a conventional
        // ProblemDetails schema is replaced by the envelope. A schema the action
        // declared deliberately is left untouched.
        if (json.Schema is null || IsProblemDetails(json.Schema))
            json.Schema = envelope;

        // A per-action [SwaggerExample] has already run? No — this filter runs
        // first, so ResponseExamplesOperationFilter will overwrite this default
        // where an action supplies its own. This is the fallback.
        json.Example ??= SwaggerJson.Parse(exampleJson);
    }

    private static bool IsProblemDetails(OpenApiSchema schema) =>
        schema.Reference?.Id is { } id && ProblemDetailsSchemas.Contains(id);
}
