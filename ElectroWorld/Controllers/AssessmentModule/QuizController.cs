using AssessmentBL.DTOs.Quiz;
using AssessmentBL.DTOs.Quiz.Common;
using AssessmentBL.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElectroWorld.Api.Controllers;

[ApiController]
[Route("api/quizzes")]
[Authorize(Roles = "Admin")]
public class QuizController : ControllerBase
{
    private readonly IQuizService _quizService;

    public QuizController(IQuizService quizService)
    {
        _quizService = quizService;
    }

    [HttpGet("{quizId:int}")]
    public async Task<ActionResult<QuizResponseDto>> GetById(int quizId, CancellationToken ct)
    {
        var quiz = await _quizService.GetByIdAsync(quizId, ct);
        return Ok(quiz);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<QuizResponseDto>>> Get([FromQuery] QuizFilterDto filter, CancellationToken ct)
    {
        var result = await _quizService.GetAsync(filter, ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<QuizResponseDto>> Create([FromBody] CreateQuizDto request, CancellationToken ct)
    {
        var quiz = await _quizService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { quizId = quiz.Id }, quiz);
    }

    [HttpPut("{quizId:int}")]
    public async Task<ActionResult<QuizResponseDto>> Update(int quizId, [FromBody] UpdateQuizDto request, CancellationToken ct)
    {
        var quiz = await _quizService.UpdateAsync(quizId, request, ct);
        return Ok(quiz);
    }

    [HttpPatch("{quizId:int}/active")]
    public async Task<IActionResult> SetActive(int quizId, [FromQuery] bool isActive, CancellationToken ct)
    {
        await _quizService.SetActiveAsync(quizId, isActive, ct);
        return NoContent();
    }
}
