namespace Souq.Application.Features.Auth;

// ما يُعيده التسجيل والدخول: التوكن + وقت انتهائه + بيانات المستخدم الأساسية
// (كي تبني الواجهة حالتها دون طلب إضافي). لا نُعيد PasswordHash أبداً.
public record AuthResponse(string Token, DateTime ExpiresAt, UserInfo User);

public record UserInfo(int Id, string FullName, string Email, string Role);
