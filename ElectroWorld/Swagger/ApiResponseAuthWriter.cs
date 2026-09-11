using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Shared.Common.Api;

namespace ElectroWorld.Swagger;

/// <summary>
/// Makes 401 and 403 actually return the ApiResponse envelope.
///
/// WHY THIS IS REQUIRED, not optional: by default ASP.NET Core's JWT middleware
/// rejects the request before any controller runs and returns an EMPTY body with
/// only a WWW-Authenticate header. Documenting a JSON example for 401 without
/// this hook would publish a contract the API does not honour — a Flutter client
/// calling jsonDecode() on the empty body would throw.
///
/// Wired via AddJwtBearer(o => o.Events = ApiResponseAuthWriter.Events).
/// </summary>
public static class ApiResponseAuthWriter
{
    // Web defaults → camelCase, matching every other response in the API.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public const string UnauthorizedMessage = "غير مصرح لك بالوصول";
    public const string ForbiddenMessage = "ليس لديك صلاحية";

    public static JwtBearerEvents Events => new()
    {
        OnChallenge = async context =>
        {
            // Suppress the default empty 401 so we own the body.
            context.HandleResponse();

            if (context.Response.HasStarted)
                return;

            await WriteAsync(context.Response, StatusCodes.Status401Unauthorized, UnauthorizedMessage);
        },

        OnForbidden = async context =>
        {
            if (context.Response.HasStarted)
                return;

            await WriteAsync(context.Response, StatusCodes.Status403Forbidden, ForbiddenMessage);
        }
    };

    private static Task WriteAsync(HttpResponse response, int statusCode, string message)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";

        return response.WriteAsync(
            JsonSerializer.Serialize(ApiResponse<object>.Fail(message), JsonOptions));
    }
}
