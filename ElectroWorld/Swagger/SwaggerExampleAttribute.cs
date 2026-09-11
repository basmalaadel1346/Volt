namespace ElectroWorld.Swagger;

/// <summary>
/// بيحدد Example حقيقي (JSON) لحالة Status Code معينة في Swagger، مأخوذ حرفيًا من نفس
/// الرسائل اللي بيرجعها الكود فعليًا (مش رسائل مُختلَقة). التوثيق فقط - مالوش أي تأثير
/// على استجابة الـ API الفعلية وقت التشغيل.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class SwaggerExampleAttribute : Attribute
{
    public int StatusCode { get; }
    public string Json { get; }

    public SwaggerExampleAttribute(int statusCode, string json)
    {
        StatusCode = statusCode;
        Json = json;
    }
}
