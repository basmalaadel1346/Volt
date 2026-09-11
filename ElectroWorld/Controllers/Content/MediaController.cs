using ContentBL.DTOs;
using ContentBL.Interfaces;
using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.Api;

namespace ElectroWorld.Controllers.Content;

[ApiController]
[Route("api/content/media")]
[Authorize(Roles = "Admin")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class MediaController : ControllerBase
{
    private readonly IImageStorageService _imageStorageService;
    public MediaController(IImageStorageService imageStorageService) => _imageStorageService = imageStorageService;

    [HttpPost("images")]
    [RequestSizeLimit(6 * 1024 * 1024)] // 6MB (شوية زيادة عن الـ 5MB بتوع الـ Service، احتياطًا)
    [ProducesResponseType(typeof(ApiResponse<ImageUploadResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ImageUploadResponse>), StatusCodes.Status400BadRequest)]
    [SwaggerExample(200, """{"success":true,"message":"تم رفع الصورة بنجاح","data":{"url":"/uploads/lessons/8f1c2b3a-6b4d-4e2a-9c1f-3d7e5a2b1c0d.png"}}""")]
    [SwaggerExample(400, """{"success":false,"message":"امتداد الصورة غير مسموح - المسموح بس jpg, jpeg, png, webp","data":null}""")]
    public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken ct)
    {
        try
        {
            var url = await _imageStorageService.SaveImageAsync(file, ct);
            return Ok(ApiResponse<ImageUploadResponse>.Ok(new ImageUploadResponse(url), "تم رفع الصورة بنجاح"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ImageUploadResponse>.Fail(ex.Message));
        }
    }
}
