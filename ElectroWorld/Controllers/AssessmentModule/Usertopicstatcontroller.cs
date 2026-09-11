using AssessmentBL.DTOs.UserTopicStat;
using AssessmentBL.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Users;

namespace ElectroWorld.Api.Controllers;

[ApiController]
[Route("api/user-topic-stats")]
[Authorize]
public class UserTopicStatController : ControllerBase
{
    private readonly IUserTopicStatService _userTopicStatService;

    public UserTopicStatController(IUserTopicStatService userTopicStatService)
    {
        _userTopicStatService = userTopicStatService;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserTopicStatResponseDto>>> GetMine(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var stats = await _userTopicStatService.GetByUserIdAsync(userId, ct);
        return Ok(stats);
    }

    [HttpGet("{topicId:int}/{difficulty}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserTopicStatResponseDto>> GetByTopicAndDifficulty(int topicId, string difficulty, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var stat = await _userTopicStatService.GetByTopicAndDifficultyAsync(userId, topicId, difficulty, ct);
        return stat is null ? NotFound() : Ok(stat);
    }

    // No recalculate endpoint by design. UpdateAfterQuizAttemptAsync accumulates
    // (+=) into the stats row and is not idempotent, so exposing it would let any
    // user inflate their own counters without bound by re-posting it. It stays an
    // internal step of SubmitAsync, inside that method's transaction.
}