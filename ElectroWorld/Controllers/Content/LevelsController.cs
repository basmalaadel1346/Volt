using ContentBL.DTOs;
using ContentBL.Interfaces;
using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.Api;

namespace ElectroWorld.Controllers.Content;

[ApiController]
[Route("api/content/levels")]
[Authorize]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public class LevelsController : ControllerBase
{
    private readonly ILevelService _service;
    public LevelsController(ILevelService service) => _service = service;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<LevelResponse>>), StatusCodes.Status200OK)]
    [SwaggerExample(200, """{"success":true,"message":"تمت العملية بنجاح","data":[{"id":1,"title":"مقدمة في الكهرباء","description":"مفاهيم أساسية","order":1},{"id":2,"title":"الدوائر البسيطة","description":null,"order":2}]}""")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await _service.GetAllAsync(ct);
        return Ok(ApiResponse<List<LevelResponse>>.Ok(result.Value!));
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<LevelResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LevelResponse>), StatusCodes.Status404NotFound)]
    [SwaggerExample(200, """{"success":true,"message":"تمت العملية بنجاح","data":{"id":1,"title":"مقدمة في الكهرباء","description":"مفاهيم أساسية","order":1}}""")]
    [SwaggerExample(404, """{"success":false,"message":"المستوى غير موجود","data":null}""")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<LevelResponse>.Ok(result.Value!))
            : NotFound(ApiResponse<LevelResponse>.Fail(result.Error!));
    }

    /// <summary>هيديله ترتيب (Order) تلقائي = آخر ترتيب موجود + 1. متبعتيش Order في الـ Body.</summary>
    // ملحوظة: CreateAsync في الـ Service مفيهاش أي مسار فشل حاليًا (بتنجح دايمًا) - مفيش 400 موثّق هنا عمدًا.
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<LevelResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [SwaggerExample(200, """{"success":true,"message":"تم إنشاء المستوى بنجاح","data":{"id":3,"title":"المستشعرات","description":null,"order":3}}""")]
    public async Task<IActionResult> Create([FromBody] CreateLevelRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, ct);
        return Ok(ApiResponse<LevelResponse>.Ok(result.Value!, "تم إنشاء المستوى بنجاح"));
    }

    /// <summary>بتعدّل بيانات المستوى بس - الترتيب (Order) مايتغيرش هنا، استخدمي swap-order.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<LevelResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LevelResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [SwaggerExample(200, """{"success":true,"message":"تم تعديل المستوى بنجاح","data":{"id":1,"title":"مقدمة في الكهرباء - محدّث","description":"وصف جديد","order":1}}""")]
    [SwaggerExample(400, """{"success":false,"message":"المستوى غير موجود","data":null}""")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateLevelRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(id, request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<LevelResponse>.Ok(result.Value!, "تم تعديل المستوى بنجاح"))
            : BadRequest(ApiResponse<LevelResponse>.Fail(result.Error!));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [SwaggerExample(200, """{"success":true,"message":"تم مسح المستوى بنجاح"}""")]
    [SwaggerExample(400, """{"success":false,"message":"مينفعش تمسحي المستوى ده لأن فيه دروس مرتبطة بيه، امسحي الدروس الأول"}""")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var result = await _service.DeleteAsync(id, ct);
        return result.IsSuccess
            ? Ok(ApiResponse.Ok("تم مسح المستوى بنجاح"))
            : BadRequest(ApiResponse.Fail(result.Error!));
    }

    /// <summary>بتبدّل ترتيب مستويين في بعض (المستوى الأول ياخد ترتيب التاني والعكس).</summary>
    [HttpPost("swap-order")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [SwaggerExample(200, """{"success":true,"message":"تم تبديل الترتيب بنجاح"}""")]
    [SwaggerExample(400, """{"success":false,"message":"واحد من المستويين غير موجود"}""")]
    public async Task<IActionResult> SwapOrder([FromBody] SwapLevelsOrderRequest request, CancellationToken ct)
    {
        var result = await _service.SwapOrderAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse.Ok("تم تبديل الترتيب بنجاح"))
            : BadRequest(ApiResponse.Fail(result.Error!));
    }
}
