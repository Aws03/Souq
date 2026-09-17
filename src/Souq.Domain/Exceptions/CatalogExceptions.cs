namespace Souq.Domain.Exceptions;

// بيانات فئة غير صالحة (معرّف رابط، نصوص، ترتيب) — 422 برمز ثابت تترجمه الواجهة.
public sealed class InvalidCategoryException : DomainException
{
    public InvalidCategoryException(string message) : base("InvalidCategory", message) { }
}

// نقل فئة يكسر الشجرة (تحت نفسها، تحت فرعها، أو أعمق من الحدّ) — الرمز نفسه الذي عرفته الواجهة منذ 1A.
public sealed class InvalidCategoryParentException : DomainException
{
    public InvalidCategoryParentException(string message) : base("InvalidParent", message) { }
}

// مرادف بحث غير صالح (لغة، طول، أكثر من كلمة، أو كلمة إلى نفسها) — M3، ADR-0042.
public sealed class InvalidSearchSynonymException : DomainException
{
    public InvalidSearchSynonymException(string message) : base("InvalidSearchSynonym", message) { }
}
