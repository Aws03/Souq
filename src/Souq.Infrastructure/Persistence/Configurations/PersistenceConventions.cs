using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Souq.Infrastructure.Persistence.Configurations;

// قواعد تخزين موحّدة تتكرّر في عدّة إعدادات — نفس المفهوم فمكان واحد (DatabaseDesign.md).
internal static class PersistenceConventions
{
    // decimal(19,4): يسع خانات كل عملات ISO-4217 (الدينار 3) بلا تقريب صامت (ADR-0014).
    public const string MoneyColumnType = "decimal(19,4)";

    // عمود rowversion كخاصية ظلّ (Shadow) — الكيان في Domain لا يعرف شيئاً عن التزامن
    // التقني؛ EF يضيف "WHERE RowVersion = @original" لكل UPDATE/DELETE (ADR-0013).
    public const string RowVersion = "RowVersion";

    public static void HasRowVersion<T>(this EntityTypeBuilder<T> builder) where T : class
        => builder.Property<byte[]>(RowVersion).IsRowVersion();
}
