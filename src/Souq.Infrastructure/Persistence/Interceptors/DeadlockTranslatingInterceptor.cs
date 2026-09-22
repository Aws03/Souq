using System.Collections;
using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Souq.Application.Common.Exceptions;

namespace Souq.Infrastructure.Persistence.Interceptors;

// ============================================================================
// DeadlockTranslatingInterceptor — الجمود (SQL 1205) سباقٌ يُعاد، أياً كان الأمر الذي وقع عليه.
//
// **العطل (F-33).** `AppDbContext` يترجم الجمود في موضعين: `SaveChangesAsync` و`InTransactionAsync`،
// وكلاهما مسارُ **كتابة**. لكنّ SQL Server يختار ضحيّته بتكلفة التراجع لا بنوع الأمر، فيقع الاختيار
// على `SELECT` كما يقع على `UPDATE`. وحين يقع على قراءة لا يمرّ الاستثناء بأيٍّ من الموضعين: يخرج
// خاماً عبر حدود الطبقة فيصير **500**.
//
// وليس استنتاجاً: سقط `GET /api/basket` على مشغّل GitHub ثمّ محلّياً، والسطر الملتقَط يقول بالحرف
// «An exception occurred while iterating over the results of a query» ثمّ
// «Error Number:1205 … chosen as the deadlock victim». أي أنّ `BasketWriter` — وهو مبنيٌّ أصلاً
// لإعادة المحاولة على هذا السباق بعينه — لم يكن يرى شيئاً يلتقطه.
//
// **ولماذا يُلفّ القارئ ولا يكفي `CommandFailed`.** قِيس ولم يُفترض: `ReaderExecuted` **ينجح** على
// الاستعلام الذي سيُجمَّد، ولا يُستدعى `CommandFailed` إطلاقاً. فالخطأ يصل من الخادم بعد تسليم
// القارئ، أثناء تعداد الصفوف — أي في `ReadAsync` لا في تنفيذ الأمر. فاللفّ هو الموضع الوحيد الذي
// يراه. و`CommandFailed` يبقى للحالة الأخرى: جمودٌ يقع وقت التنفيذ (كتابة بلا قارئ).
//
// **ولماذا الترجمة لا إعادةُ المحاولة هنا.** الضحيّة تُرجَع كاملةً: لا بيانات ناقصة، والمطلوب من
// المتصل إعادة المحاولة — وهو بالضبط معنى `ConcurrencyConflictException` في هذا المستودع، وما
// يلتقطه `BasketWriter` و`InventoryWriter` وأخواتهما فيُعيدون العملية من أوّلها. وإعادةٌ عمياء في
// هذه الطبقة ستُعيد أمراً واحداً داخل معاملةٍ ماتت، أو استعلاماً وسط تعدادٍ نصفُه مقروء.
//
// ومن لا يُعيد المحاولة يصل جوابه 409 لا 500: «أعد المحاولة» بدل «تعطّل الخادم» — وهو الفرق بين
// رسالةٍ صحيحة ومعدّل أخطاءٍ مصطنع يُخفي الأعطال الحقيقية.
// ============================================================================
public sealed class DeadlockTranslatingInterceptor : DbCommandInterceptor
{
    private const int Deadlock = 1205;

    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result) => new Translating(result);

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<DbDataReader>(new Translating(result));

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) =>
        Throw(eventData.Exception);

    public override Task CommandFailedAsync(
        DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Throw(eventData.Exception);
        return Task.CompletedTask;
    }

    // الرمي من المعترِض يستبدل الاستثناء الصاعد. والأصل يُحفظ داخلياً فلا يضيع رقم الخطأ من التشخيص.
    private static void Throw(Exception exception)
    {
        if (IsDeadlock(exception)) throw new ConcurrencyConflictException(exception);
    }

    private static bool IsDeadlock(Exception exception) => exception switch
    {
        SqlException { Number: Deadlock } => true,
        { InnerException: { } inner } => IsDeadlock(inner),
        _ => false,
    };

    // ========================================================================
    // غلافٌ يمرّر كل شيء كما هو، ويترجم الجمود وحده حيث يقع فعلاً: تقدُّم القارئ.
    // ما عداه يُمرَّر بلا لمس — الغلاف ليس مكاناً لسلوكٍ جديد.
    // ========================================================================
    private sealed class Translating(DbDataReader inner) : DbDataReader
    {
        public override bool Read() => Guard(inner.Read);

        public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            try { return await inner.ReadAsync(cancellationToken); }
            catch (Exception e) when (IsDeadlock(e)) { throw new ConcurrencyConflictException(e); }
        }

        public override bool NextResult() => Guard(inner.NextResult);

        public override async Task<bool> NextResultAsync(CancellationToken cancellationToken)
        {
            try { return await inner.NextResultAsync(cancellationToken); }
            catch (Exception e) when (IsDeadlock(e)) { throw new ConcurrencyConflictException(e); }
        }

        private static T Guard<T>(Func<T> read)
        {
            try { return read(); }
            catch (Exception e) when (IsDeadlock(e)) { throw new ConcurrencyConflictException(e); }
        }

        public override object this[int ordinal] => inner[ordinal];
        public override object this[string name] => inner[name];
        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;
        public override int VisibleFieldCount => inner.VisibleFieldCount;

        public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
            inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
            inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override IEnumerator GetEnumerator() => inner.GetEnumerator();
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override T GetFieldValue<T>(int ordinal) => inner.GetFieldValue<T>(ordinal);
        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken) =>
            inner.GetFieldValueAsync<T>(ordinal, cancellationToken);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override Stream GetStream(int ordinal) => inner.GetStream(ordinal);
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override TextReader GetTextReader(int ordinal) => inner.GetTextReader(ordinal);
        public override object GetValue(int ordinal) => inner.GetValue(ordinal);
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) =>
            inner.IsDBNullAsync(ordinal, cancellationToken);
        public override DataTable? GetSchemaTable() => inner.GetSchemaTable();
        public override Task<DataTable?> GetSchemaTableAsync(CancellationToken cancellationToken = default) =>
            inner.GetSchemaTableAsync(cancellationToken);
        public override void Close() => inner.Close();
        public override Task CloseAsync() => inner.CloseAsync();
        public override ValueTask DisposeAsync() => inner.DisposeAsync();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
        }
    }
}
