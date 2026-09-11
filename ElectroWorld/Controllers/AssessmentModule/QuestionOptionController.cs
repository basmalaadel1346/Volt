using AssessmentBL.DTOs.QuestionOption;
using AssessmentBL.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElectroWorld.Api.Controllers;

[ApiController]
[Route("api/question-options")]
[Authorize(Roles = "Admin")]
public class QuestionOptionController : ControllerBase
{
    private readonly IQuestionOptionServiceForAdmin _questionOptionService;

    public QuestionOptionController(IQuestionOptionServiceForAdmin questionOptionService)
    {
        _questionOptionService = questionOptionService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminQuestionOptionResponseDto>>> GetByQuestionId([FromQuery] int questionId, CancellationToken ct)
    {
        var options = await _questionOptionService.GetByQuestionIdAsync(questionId, ct);
        return Ok(options);
    }

    // NOTE: the service exposes no single-option "get by id" method, only
    // GetByQuestionIdAsync. CreatedAtAction therefore points back at the
    // by-question listing endpoint instead of a single-resource URL. See
    // "Potential Issues Found".
    [HttpPost]
    public async Task<ActionResult<AdminQuestionOptionResponseDto>> Create([FromBody] CreateQuestionOptionDto request, CancellationToken ct)
    {
        var option = await _questionOptionService.CreateOptionAsync(request, ct);
        return CreatedAtAction(nameof(GetByQuestionId), new { questionId = request.QuestionId }, option);
    }

    [HttpPut("{optionId:int}")]
    public async Task<ActionResult<AdminQuestionOptionResponseDto>> Update(int optionId, [FromBody] UpdateQuestionOptionDto request, CancellationToken ct)
    {
        var option = await _questionOptionService.UpdateOptionAsync(optionId, request, ct);
        return Ok(option);
    }

    [HttpDelete("{optionId:int}")]
    public async Task<IActionResult> Delete(int optionId, CancellationToken ct)
    {
        await _questionOptionService.DeleteOptionAsync(optionId, ct);
        return NoContent();
    }
}