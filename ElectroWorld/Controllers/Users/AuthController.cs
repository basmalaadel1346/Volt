using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.Api;
using UsersBL.DTOs;
using UsersBL.Interfaces;

namespace ElectroWorld.Controllers.Users;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    public AuthController(IAuthService authService) => _authService = authService;

    // ملحوظة: RegisterGuestAsync في الـ Service مفيهاش أي مسار فشل حاليًا (بتنجح دايمًا)،
    // فمفيش 400 موثّق هنا عمدًا - توثيق حالة مش ممكن تحصل فعليًا هيكون تضليل.
    [HttpPost("guest")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [SwaggerExample(200, """{"success":true,"message":"تم إنشاء حساب Guest بنجاح","data":{"userId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","fullName":"Ahmed","role":"Child","authProvider":"Guest","age":null,"accessToken":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...","refreshToken":"cXVpY2tSZWZyZXNoVG9rZW5WYWx1ZUV4YW1wbGU=","accessTokenExpiresAt":"2026-09-10T12:15:00Z"}}""")]
    public async Task<IActionResult> RegisterGuest([FromBody] RegisterGuestRequest request, CancellationToken ct)
    {
        var result = await _authService.RegisterGuestAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<AuthResponse>.Ok(result.Value!, "تم إنشاء حساب Guest بنجاح"))
            : BadRequest(ApiResponse<AuthResponse>.Fail(result.Error!));
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status400BadRequest)]
    [SwaggerExample(200, """{"success":true,"message":"تم التسجيل بنجاح","data":{"userId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","fullName":"Ahmed Ali","role":"Parent","authProvider":"Email","age":35,"accessToken":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...","refreshToken":"cXVpY2tSZWZyZXNoVG9rZW5WYWx1ZUV4YW1wbGU=","accessTokenExpiresAt":"2026-09-10T12:15:00Z"}}""")]
    [SwaggerExample(400, """{"success":false,"message":"البريد الإلكتروني مستخدم بالفعل","data":null}""")]
    public async Task<IActionResult> Register([FromBody] RegisterEmailRequest request, CancellationToken ct)
    {
        var result = await _authService.RegisterWithEmailAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<AuthResponse>.Ok(result.Value!, "تم التسجيل بنجاح"))
            : BadRequest(ApiResponse<AuthResponse>.Fail(result.Error!));
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status401Unauthorized)]
    [SwaggerExample(200, """{"success":true,"message":"تم تسجيل الدخول بنجاح","data":{"userId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","fullName":"Ahmed Ali","role":"Parent","authProvider":"Email","age":35,"accessToken":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...","refreshToken":"cXVpY2tSZWZyZXNoVG9rZW5WYWx1ZUV4YW1wbGU=","accessTokenExpiresAt":"2026-09-10T12:15:00Z"}}""")]
    [SwaggerExample(401, """{"success":false,"message":"بيانات الدخول غير صحيحة","data":null}""")]
    public async Task<IActionResult> Login([FromBody] LoginEmailRequest request, CancellationToken ct)
    {
        var result = await _authService.LoginWithEmailAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<AuthResponse>.Ok(result.Value!, "تم تسجيل الدخول بنجاح"))
            : Unauthorized(ApiResponse<AuthResponse>.Fail(result.Error!));
    }

    [HttpPost("google")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status400BadRequest)]
    [SwaggerExample(200, """{"success":true,"message":"تم الدخول عن طريق Google بنجاح","data":{"userId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","fullName":"Ahmed Ali","role":"Parent","authProvider":"Google","age":null,"accessToken":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...","refreshToken":"cXVpY2tSZWZyZXNoVG9rZW5WYWx1ZUV4YW1wbGU=","accessTokenExpiresAt":"2026-09-10T12:15:00Z"}}""")]
    [SwaggerExample(400, """{"success":false,"message":"Google Token غير صالح","data":null}""")]
    public async Task<IActionResult> GoogleAuth([FromBody] GoogleAuthRequest request, CancellationToken ct)
    {
        var result = await _authService.LoginOrRegisterWithGoogleAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<AuthResponse>.Ok(result.Value!, "تم الدخول عن طريق Google بنجاح"))
            : BadRequest(ApiResponse<AuthResponse>.Fail(result.Error!));
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status401Unauthorized)]
    [SwaggerExample(200, """{"success":true,"message":"تم تجديد التوكن بنجاح","data":{"userId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","fullName":"Ahmed Ali","role":"Parent","authProvider":"Email","age":35,"accessToken":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...","refreshToken":"cXVpY2tSZWZyZXNoVG9rZW5WYWx1ZUV4YW1wbGU=","accessTokenExpiresAt":"2026-09-10T12:15:00Z"}}""")]
    [SwaggerExample(401, """{"success":false,"message":"Refresh Token غير صالح أو منتهي","data":null}""")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await _authService.RefreshTokenAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<AuthResponse>.Ok(result.Value!, "تم تجديد التوكن بنجاح"))
            : Unauthorized(ApiResponse<AuthResponse>.Fail(result.Error!));
    }

    // ملحوظة: الـ 401 هنا لو الـ Token نفسه (Access Token في الـ Header) غير صالح/منتهي -
    // ده بيرجع من الـ [Authorize] Middleware قبل ما يوصل لكودنا، فمفيش Body ليه (موثّق بالـ Status Code لوحده تحت).
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [SwaggerExample(200, """{"success":true,"message":"تم تسجيل الخروج بنجاح"}""")]
    [SwaggerExample(400, """{"success":false,"message":"Token غير موجود"}""")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await _authService.LogoutAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse.Ok("تم تسجيل الخروج بنجاح"))
            : BadRequest(ApiResponse.Fail(result.Error!));
    }

    // ملحوظة: بترجع 200 دايمًا حتى لو الإيميل مش مسجل خالص (User Enumeration Protection) -
    // مفيش مسار فشل، فمفيش 400 موثّق هنا عمدًا.
    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [SwaggerExample(200, """{"success":true,"message":"لو الإيميل مسجل، هيوصلك كود إعادة التعيين"}""")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        await _authService.ForgotPasswordAsync(request, ct);
        return Ok(ApiResponse.Ok("لو الإيميل مسجل، هيوصلك كود إعادة التعيين"));
    }

    [HttpPost("verify-reset-otp")]
    [ProducesResponseType(typeof(ApiResponse<VerifyResetOtpResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<VerifyResetOtpResponse>), StatusCodes.Status400BadRequest)]
    [SwaggerExample(200, """{"success":true,"message":"الكود صحيح","data":{"resetToken":"xY9k2mP7qzR4tL...","resetTokenExpiresAt":"2026-09-10T12:10:00Z"}}""")]
    [SwaggerExample(400, """{"success":false,"message":"الكود منتهي أو غير صالح، اطلبي كود جديد","data":null}""")]
    public async Task<IActionResult> VerifyResetOtp([FromBody] VerifyResetOtpRequest request, CancellationToken ct)
    {
        var result = await _authService.VerifyResetOtpAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<VerifyResetOtpResponse>.Ok(result.Value!, "الكود صحيح"))
            : BadRequest(ApiResponse<VerifyResetOtpResponse>.Fail(result.Error!));
    }

    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [SwaggerExample(200, """{"success":true,"message":"تم تغيير كلمة المرور بنجاح"}""")]
    [SwaggerExample(400, """{"success":false,"message":"جلسة إعادة التعيين منتهية أو غير صالحة، ابدئي من كود جديد"}""")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var result = await _authService.ResetPasswordAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse.Ok("تم تغيير كلمة المرور بنجاح"))
            : BadRequest(ApiResponse.Fail(result.Error!));
    }
}
