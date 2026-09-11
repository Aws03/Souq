using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Souq.Application.Common.Interfaces;

namespace Souq.Infrastructure.Security;

// ============================================================================
// تشفير أسرار المتاجر (المرحلة 11، ADR-0031): AES-256-GCM بمفتاح رئيسي من الإعداد (Secrets:Keys:{id} = base64 لـ 32
// بايت، Secrets:ActiveKeyId للتشفير الجديد). الصيغة "v1.{id}.{base64(nonce|tag|cipher)}": معرّف المفتاح يسمح بالتدوير
// (القديم يبقى للفكّ حتى تُعاد كتابة الأسرار). الغرض بيانات مصاحبة (AAD): النصّ لا يُفكّ إلا لصاحبه.
// لماذا لا Data Protection: حلقة مفاتيحها تُحفظ افتراضياً على قرص الحاوية فتضيع بإعادة تشغيلها — وتضيع معها كل الأسرار.
// هنا المفتاح سرّ بيئة صريح كمفتاح JWT، وفقده أو غيابه خطأ ظاهر لا فقد صامت.
// ============================================================================
public sealed class SecretsSettings
{
    public Dictionary<string, string> Keys { get; set; } = new();
    public string? ActiveKeyId { get; set; }

    // مفتاح بقيمة فارغة كأنه غير مضبوط: Docker Compose يمرّر المتغيّر فارغاً حين لا يُضبط في .env.
    public IEnumerable<KeyValuePair<string, string>> ConfiguredKeys => Keys.Where(k => !string.IsNullOrWhiteSpace(k.Value));
}

public sealed class SecretsSettingsValidator : IValidateOptions<SecretsSettings>
{
    public ValidateOptionsResult Validate(string? name, SecretsSettings settings)
    {
        var keys = settings.ConfiguredKeys.ToList();
        foreach (var (id, value) in keys)
        {
            if (string.IsNullOrWhiteSpace(id) || !id.All(char.IsAsciiLetterOrDigit))
                return ValidateOptionsResult.Fail($"Secrets:Keys: معرّف المفتاح '{id}' حروف وأرقام لاتينية فقط.");
            if (AesGcmSecretProtector.Decode(value) is null)
                return ValidateOptionsResult.Fail($"Secrets:Keys:{id} يجب أن يكون base64 لـ 32 بايت بالضبط.");
        }
        if (settings.ActiveKeyId is { Length: > 0 } active && keys.All(k => k.Key != active))
            return ValidateOptionsResult.Fail($"Secrets:ActiveKeyId '{active}' غير موجود في Secrets:Keys.");
        if (keys.Count > 0 && string.IsNullOrWhiteSpace(settings.ActiveKeyId))
            return ValidateOptionsResult.Fail("Secrets:ActiveKeyId مطلوب حين تُضبط Secrets:Keys.");
        return ValidateOptionsResult.Success;
    }
}

public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const string Version = "v1";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IReadOnlyDictionary<string, byte[]> _keys;
    private readonly string? _activeKeyId;

    public AesGcmSecretProtector(IOptions<SecretsSettings> options)
    {
        var settings = options.Value;
        _keys = settings.ConfiguredKeys.ToDictionary(k => k.Key, k => Decode(k.Value)!);
        _activeKeyId = string.IsNullOrWhiteSpace(settings.ActiveKeyId) ? null : settings.ActiveKeyId;
    }

    public bool IsConfigured => _activeKeyId is not null;

    public string Protect(string plaintext, string purpose)
    {
        if (_activeKeyId is null)
            throw new SecretUnavailableException("لا مفتاح تشفير أسرار مضبوط (Secrets:ActiveKeyId)");

        var plain = Encoding.UTF8.GetBytes(plaintext);
        var blob = new byte[NonceSize + TagSize + plain.Length];
        var nonce = blob.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using (var aes = new AesGcm(_keys[_activeKeyId], TagSize))
            aes.Encrypt(nonce, plain, blob.AsSpan(NonceSize + TagSize), blob.AsSpan(NonceSize, TagSize), Encoding.UTF8.GetBytes(purpose));
        return $"{Version}.{_activeKeyId}.{Convert.ToBase64String(blob)}";
    }

    public string Unprotect(string protectedText, string purpose)
    {
        var parts = protectedText.Split('.', 3);
        if (parts.Length != 3 || parts[0] != Version)
            throw new SecretUnavailableException("صيغة سرّ مشفّر غير معروفة");
        if (!_keys.TryGetValue(parts[1], out var key))
            throw new SecretUnavailableException($"مفتاح التشفير '{parts[1]}' لم يعد مضبوطاً (Secrets:Keys)");

        byte[] blob;
        try { blob = Convert.FromBase64String(parts[2]); }
        catch (FormatException ex) { throw new SecretUnavailableException("سرّ مشفّر تالف", ex); }
        if (blob.Length < NonceSize + TagSize)
            throw new SecretUnavailableException("سرّ مشفّر تالف");

        var plain = new byte[blob.Length - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(blob.AsSpan(0, NonceSize), blob.AsSpan(NonceSize + TagSize), blob.AsSpan(NonceSize, TagSize), plain,
                Encoding.UTF8.GetBytes(purpose));
        }
        catch (CryptographicException ex)
        {
            throw new SecretUnavailableException("تعذّر فكّ السرّ: مفتاح أو غرض لا يطابق", ex);
        }
        return Encoding.UTF8.GetString(plain);
    }

    internal static byte[]? Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var bytes = Convert.FromBase64String(value.Trim());
            return bytes.Length == 32 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
