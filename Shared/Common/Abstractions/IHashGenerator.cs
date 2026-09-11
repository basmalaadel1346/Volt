namespace Shared.Common.Abstractions;

/// <summary>
/// هاش عادي (SHA-256) بنستخدمه للـ Refresh Tokens والـ OTP، لأننا محتاجين
/// نلاقي الصف بالـ Hash بتاعه في الداتابيز (مش زي الباسورد اللي بنـVerify بيه بس).
/// </summary>
public interface IHashGenerator
{
    string Hash(string value);
}
