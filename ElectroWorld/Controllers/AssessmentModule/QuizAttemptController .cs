using AssessmentBL.DTOs.QuizAttempt;
using AssessmentBL.Interfaces;
using ElectroWorld.Swagger;
using Shared.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Shared.Users;

namespace ElectroWorld.Api.Controllers;

[ApiController]
[Route("api/quiz-attempts")]
[Authorize]
public class QuizAttemptController : ControllerBase
{
    private readonly IQuizAttemptService _quizAttemptService;

    public QuizAttemptController(IQuizAttemptService quizAttemptService)
    {
        _quizAttemptService = quizAttemptService;
    }

    /// <summary>
    /// Resolves the content language for this request: an explicit ?language=
    /// wins, otherwise Accept-Language, otherwise the service default. An
    /// unsupported value is normalized rather than rejected — a bad language
    /// header must never fail a quiz submission.
    /// </summary>
    private string? ResolveLanguage(string? language)
    {
        if (!string.IsNullOrWhiteSpace(language))
            return language;

        return Request.Headers.TryGetValue("Accept-Language", out StringValues header)
            ? header.ToString()
            : null;
    }

    // First attempt: only quizId is supplied.
    // Retry attempt: previousAttemptId is also supplied (service resolves
    // which questions to serve and attaches the latest hints).
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<QuizAttemptResponseDto>> Start(
        [FromQuery] int quizId,
        [FromQuery] long? previousAttemptId,
        [FromQuery] string? language,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var attempt = await _quizAttemptService.StartAsync(
            quizId, userId, previousAttemptId, ResolveLanguage(language), ct);
        return CreatedAtAction(nameof(GetById), new { attemptId = attempt.AttemptId }, attempt);
    }

    /// <summary>Submits the whole attempt and returns the score plus AI hints.</summary>
    /// <remarks>
    /// Fully decorated reference action: every status code declares a concrete
    /// type, and every failure carries a hardcoded JSON example. 401/403/500 would
    /// be added automatically by DefaultApiResponsesOperationFilter; they are
    /// declared here to override the generic message with a specific one.
    /// </remarks>
    [HttpPost("{attemptId:long}/submit")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(QuizAttemptResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status500InternalServerError)]
    [SwaggerExample(200, @"{
      ""attemptId"": 42,
      ""totalQuestions"": 4,
      ""autoGradedQuestions"": 3,
      ""pendingEssayQuestions"": 1,
      ""correctAnswers"": 2,
      ""wrongAnswers"": 1,
      ""scorePercentage"": 66.67,
      ""language"": ""ar"",
      ""languageFallbackApplied"": false,
      ""retryQuestions"": [
        {
          ""questionId"": 102,
          ""questionText"": ""ما وحدة قياس المقاومة الكهربية؟"",
          ""questionType"": ""MultipleChoice"",
          ""imageUrl"": null,
          ""difficulty"": ""Medium"",
          ""displayOrder"": 2,
          ""points"": 2,
          ""currentHint"": ""افتكر إن الوحدة اسمها على اسم العالم الألماني."",
          ""options"": [
            { ""optionId"": 1004, ""optionText"": ""الأوم"", ""imageUrl"": null, ""displayOrder"": 1 },
            { ""optionId"": 1005, ""optionText"": ""الفولت"", ""imageUrl"": null, ""displayOrder"": 2 }
          ]
        }
      ]
    }")]
    [SwaggerExample(400, ApiResponseExamples.BadRequest)]
    [SwaggerExample(401, ApiResponseExamples.Unauthorized)]
    [SwaggerExample(403, ApiResponseExamples.Forbidden)]
    [SwaggerExample(404, @"{""success"":false,""message"":""المحاولة رقم 42 غير موجودة"",""data"":null}")]
    [SwaggerExample(409, @"{""success"":false,""message"":""المحاولة رقم 42 تم تسليمها بالفعل"",""data"":null}")]
    [SwaggerExample(500, ApiResponseExamples.ServerError)]
    public async Task<ActionResult<QuizAttemptResultDto>> Submit(
        long attemptId,
        [FromBody] SubmitQuizAttemptDto dto,
        [FromQuery] string? language,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await _quizAttemptService.SubmitAsync(
            attemptId, userId, dto, ResolveLanguage(language), ct);
        return Ok(result);
    }

    [HttpGet("{attemptId:long}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuizAttemptResponseDto>> GetById(
        long attemptId,
        [FromQuery] string? language,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var attempt = await _quizAttemptService.GetByIdAsync(
            attemptId, userId, ResolveLanguage(language), ct);
        return Ok(attempt);
    }
}