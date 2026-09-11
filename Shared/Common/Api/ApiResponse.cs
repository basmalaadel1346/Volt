namespace Shared.Common.Api;

// شكل موحّد لكل الـ Responses في المشروع (مش بس Users) - عشان الفلاتر
// تتعامل مع نفس الـ Structure أيًا كان الـ Endpoint اللي بتكلمه
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = default!;
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data, string message = "تمت العملية بنجاح") =>
        new() { Success = true, Message = message, Data = data };

    public static ApiResponse<T> Fail(string message) =>
        new() { Success = false, Message = message, Data = default };
}

// نسخة من غير Data، للـ Endpoints اللي مش بترجع بيانات (زي Logout)
public class ApiResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = default!;

    public static ApiResponse Ok(string message = "تمت العملية بنجاح") =>
        new() { Success = true, Message = message };

    public static ApiResponse Fail(string message) =>
        new() { Success = false, Message = message };
}
