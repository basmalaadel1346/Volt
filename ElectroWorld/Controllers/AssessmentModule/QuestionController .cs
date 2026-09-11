using AssessmentBL.DTOs.Question;
using AssessmentBL.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElectroWorld.Api.Controllers;

[ApiController]
[Route("api/questions")]
[Authorize(Roles = "Admin")]
public class QuestionController : ControllerBase
{
    private readonly IQuestionServiceForAdmin _questionService;

    public QuestionController(IQuestionServiceForAdmin questionService)
    {
        _questionService = questionService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminQuestionResponseDto>>> GetByQuizId([FromQuery] int quizId, CancellationToken ct)
    {
        var questions = await _questionService.GetByQuizIdAsync(quizId, ct);
        return Ok(questions);
    }

    [HttpGet("{questionId:int}")]
    public async Task<ActionResult<AdminQuestionResponseDto>> GetById(int questionId, CancellationToken ct)
    {
        var question = await _questionService.GetByQuestionIdAsync(questionId, ct);
        return Ok(question);
    }

    [HttpPost]
    public async Task<ActionResult<AdminQuestionResponseDto>> Create([FromBody] CreateQuestionDto request, CancellationToken ct)
    {
        var question = await _questionService.CreateQuestionAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { questionId = question.Id }, question);
    }

    [HttpPut("{questionId:int}")]
    public async Task<ActionResult<AdminQuestionResponseDto>> Update(int questionId, [FromBody] UpdateQuestionDto request, CancellationToken ct)
    {
        var question = await _questionService.UpdateQuestionAsync(questionId, request, ct);
        return Ok(question);
    }

    [HttpPatch("{questionId:int}/active")]
    public async Task<IActionResult> SetActive(int questionId, [FromQuery] bool isActive, CancellationToken ct)
    {
        await _questionService.SetActiveAsync(questionId, isActive, ct);
        return NoContent();
    }
}