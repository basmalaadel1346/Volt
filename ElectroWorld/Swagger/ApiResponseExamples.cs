namespace ElectroWorld.Swagger;

/// <summary>
/// The canonical JSON examples shown in Swagger for the shared ApiResponse
/// envelope. Declared once here so a message is never copy-pasted across
/// dozens of actions and cannot drift between them.
///
/// Every string below matches what the API actually returns at runtime — see
/// ApiResponseProblemWriter (401/403) and ExceptionMiddleware (400/404/409/500).
/// If you change a message in one place, change it in the other.
/// </summary>
public static class ApiResponseExamples
{
    public const string BadRequest =
        """{"success":false,"message":"عنصر المحتوى غير موجود","data":null}""";

    public const string Unauthorized =
        """{"success":false,"message":"غير مصرح لك بالوصول","data":null}""";

    public const string Forbidden =
        """{"success":false,"message":"ليس لديك صلاحية","data":null}""";

    public const string NotFound =
        """{"success":false,"message":"العنصر المطلوب غير موجود","data":null}""";

    public const string Conflict =
        """{"success":false,"message":"تم تنفيذ العملية بالفعل","data":null}""";

    public const string ServerError =
        """{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}""";
}
