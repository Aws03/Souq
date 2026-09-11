using NSubstitute;
using Souq.Domain.Interfaces;

namespace Souq.Application.Tests.TestDoubles;

// وحدة عمل وهمية تنفّذ جسم المعاملة فوراً (بلا قاعدة): حالات الاستخدام التي تلفّ حفظاتها بمعاملة تُختبر كما تعمل،
// والاستثناء من داخلها يصعد كما في الحقيقة (والتراجع مسؤولية القاعدة — تُثبته اختبارات التكامل).
public static class TestUnitOfWork
{
    public static IUnitOfWork Create()
    {
        var uow = Substitute.For<IUnitOfWork>();
        uow.InTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
           .Returns(call => call.Arg<Func<Task>>()());
        return uow;
    }
}
