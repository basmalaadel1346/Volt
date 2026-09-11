using System.Net;
using System.Text.Json;
using Shared.Common.Api;
using Shared.Common.Exceptions;
namespace ElectroWorld.Middleware
{
    public class ExceptionMiddleware
    {
        // Web defaults → camelCase, matching every other response in the API.
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;

        public ExceptionMiddleware(
            RequestDelegate next,
            ILogger<ExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unhandled exception occurred while processing the request.");

                await HandleExceptionAsync(context, ex);
            }
        }

        private static async Task HandleExceptionAsync(
    HttpContext context,
    Exception exception)
        {
            context.Response.ContentType = "application/json; charset=utf-8";

            var statusCode = exception switch
            {
                KeyNotFoundException => (int)HttpStatusCode.NotFound,
                ConflictException => (int)HttpStatusCode.Conflict,
                ArgumentException => (int)HttpStatusCode.BadRequest,
                InvalidOperationException => (int)HttpStatusCode.BadRequest,
                UnauthorizedAccessException => (int)HttpStatusCode.Forbidden,
                _ => (int)HttpStatusCode.InternalServerError
            };

            context.Response.StatusCode = statusCode;

            var message = statusCode == (int)HttpStatusCode.InternalServerError
                ? "حدث خطأ داخلي في الخادم"
                : exception.Message;

            // The shared envelope, so every failure in the API has one shape.
            // ⚠ BREAKING CHANGE for existing Assessment clients, which previously
            // received { statusCode, message }. Coordinate with the Flutter team.
            // To revert, restore the anonymous { statusCode, message } object.
            var response = ApiResponse<object>.Fail(message);

            await context.Response.WriteAsync(
                JsonSerializer.Serialize(response, JsonOptions));
        }
    }
}



