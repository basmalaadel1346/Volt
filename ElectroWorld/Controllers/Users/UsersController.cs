using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.Api;
using Shared.Users;
using UsersBL.DTOs;
using UsersBL.Interfaces;

namespace ElectroWorld.Controllers.Users;

[ApiController]
[Route("api/users")]
[Authorize] // كل الـ Endpoints هنا لازم Access Token صالح
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    public UsersController(IUserService userService) => _userService = userService;

    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<UserProfileResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserProfileResponse>), StatusCodes.Status404NotFound)]
    [SwaggerExample(200, """{"success":true,"message":"تمت العملية بنجاح","data":{"id":"3fa85f64-5717-4562-b3fc-2c963f66afa6","email":"ahmed@example.com","fullName":"Ahmed Ali","role":"Parent","authProvider":"Email","age":35,"isActive":true,"convertedFromGuestAt":null,"createdAt":"2026-08-01T10:00:00Z"}}""")]
    [SwaggerExample(404, """{"success":false,"message":"المستخدم غير موجود","data":null}""")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var userId = User.GetUserId(); // من التوكن، مش من الفلاتر
        var result = await _userService.GetProfileAsync(userId, ct);

        return result.IsSuccess
            ? Ok(ApiResponse<UserProfileResponse>.Ok(result.Value!))
            : NotFound(ApiResponse<UserProfileResponse>.Fail(result.Error!));
    }

    [HttpPut("me")]
    [ProducesResponseType(typeof(ApiResponse<UserProfileResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserProfileResponse>), StatusCodes.Status400BadRequest)]
    [SwaggerExample(200, """{"success":true,"message":"تم تحديث البيانات بنجاح","data":{"id":"3fa85f64-5717-4562-b3fc-2c963f66afa6","email":"ahmed@example.com","fullName":"Ahmed Ali","role":"Parent","authProvider":"Email","age":36,"isActive":true,"convertedFromGuestAt":null,"createdAt":"2026-08-01T10:00:00Z"}}""")]
    [SwaggerExample(400, """{"success":false,"message":"المستخدم غير موجود","data":null}""")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await _userService.UpdateProfileAsync(userId, request, ct);

        return result.IsSuccess
            ? Ok(ApiResponse<UserProfileResponse>.Ok(result.Value!, "تم تحديث البيانات بنجاح"))
            : BadRequest(ApiResponse<UserProfileResponse>.Fail(result.Error!));
    }
}
