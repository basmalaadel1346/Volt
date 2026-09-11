using ContentBL.DTOs;
using ContentBL.Interfaces;
using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.Api;

namespace ElectroWorld.Controllers.Content;

[ApiController]
[Route("api/content/content-types")]
[Authorize]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public class ContentTypesController : ControllerBase
{
    private readonly IContentTypeService _service;
    public ContentTypesController(IContentTypeService service) => _service = service;

    // ملحوظة: GetAllAsync مفيهاش أي مسار فشل (Lookup Table ثابتة) - مفيش Error موثّق هنا عمدًا.
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<ContentTypeResponse>>), StatusCodes.Status200OK)]
    [SwaggerExample(200, """{"success":true,"message":"تمت العملية بنجاح","data":[{"id":1,"name":"Text"},{"id":2,"name":"Image"},{"id":3,"name":"TextAndImage"}]}""")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await _service.GetAllAsync(ct);
        return Ok(ApiResponse<List<ContentTypeResponse>>.Ok(result.Value!));
    }
}
