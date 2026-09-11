using ContentBL.DTOs;
using ContentBL.Interfaces;
using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.Api;

namespace ElectroWorld.Controllers.Content;

[ApiController]
[Route("api/content")]
[Authorize]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public class LessonContentsController : ControllerBase
{
    private readonly ILessonContentService _service;
    public LessonContentsController(ILessonContentService service) => _service = service;

    /// <summary>هيديله ترتيب (SortOrder) تلقائي = آخر ترتيب في نفس الدرس + 1. متبعتيش SortOrder في الـ Body.</summary>
    [HttpPost("lessons/{lessonId:int}/contents")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<LessonContentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LessonContentResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [SwaggerExample(200, """{"success":true,"message":"تم إضافة المحتوى بنجاح","data":{"id":12,"lessonId":5,"contentTypeId":1,"contentTypeName":"Text","content":"معلومة إضافية عن المقاومة الكهربية","mediaUrl":null,"sortOrder":3}}""")]
    [SwaggerExample(400, """{"success":false,"message":"الدرس غير موجود","data":null}""")]
    public async Task<IActionResult> Create(int lessonId, [FromBody] CreateLessonContentRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(lessonId, request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<LessonContentResponse>.Ok(result.Value!, "تم إضافة المحتوى بنجاح"))
            : BadRequest(ApiResponse<LessonContentResponse>.Fail(result.Error!));
    }

    /// <summary>التعديل بـ contentId لوحده - مش محتاجة تبعتي lessonId (الـ Id فريد أصلاً).
    /// الترتيب مايتغيرش هنا، استخدمي swap-order.</summary>
    [HttpPut("contents/{contentId:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<LessonContentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LessonContentResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [SwaggerExample(200, """{"success":true,"message":"تم تعديل المحتوى بنجاح","data":{"id":12,"lessonId":5,"contentTypeId":2,"contentTypeName":"Image","content":null,"mediaUrl":"/uploads/lessons/8f1c2b3a.png","sortOrder":3}}""")]
    [SwaggerExample(400, """{"success":false,"message":"عنصر المحتوى غير موجود","data":null}""")]
    public async Task<IActionResult> Update(int contentId, [FromBody] UpdateLessonContentRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(contentId, request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<LessonContentResponse>.Ok(result.Value!, "تم تعديل المحتوى بنجاح"))
            : BadRequest(ApiResponse<LessonContentResponse>.Fail(result.Error!));
    }

    /// <summary>المسح بـ contentId لوحده - مش محتاجة تبعتي lessonId.</summary>
    [HttpDelete("contents/{contentId:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [SwaggerExample(200, """{"success":true,"message":"تم مسح المحتوى بنجاح"}""")]
    [SwaggerExample(400, """{"success":false,"message":"عنصر المحتوى غير موجود"}""")]
    public async Task<IActionResult> Delete(int contentId, CancellationToken ct)
    {
        var result = await _service.DeleteAsync(contentId, ct);
        return result.IsSuccess
            ? Ok(ApiResponse.Ok("تم مسح المحتوى بنجاح"))
            : BadRequest(ApiResponse.Fail(result.Error!));
    }

    /// <summary>بتبدّل ترتيب عنصرين في بعض - لازم يكونوا في نفس الدرس.</summary>
    [HttpPost("contents/swap-order")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [SwaggerExample(200, """{"success":true,"message":"تم تبديل الترتيب بنجاح"}""")]
    [SwaggerExample(400, """{"success":false,"message":"واحد من العنصرين غير موجود"}""")]
    public async Task<IActionResult> SwapOrder([FromBody] SwapLessonContentsOrderRequest request, CancellationToken ct)
    {
        var result = await _service.SwapOrderAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse.Ok("تم تبديل الترتيب بنجاح"))
            : BadRequest(ApiResponse.Fail(result.Error!));
    }
}
