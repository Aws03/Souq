using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Customers;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Tests.Customers;

// ============================================================================
// `CustomerAccountProfiles` — تنفيذ العقد الذي أعلنته Identity وتُنفّذه هذه الوحدة (TD-03/R-15، M9).
//
// بناءُ تجمّع العميل كان يجري داخل `RegisterHandler` — أي أنّ Identity كانت تُنشئ كياناً لا تملكه،
// و`RegisterHandlerTests` هو ما كان يُثبِّت شكلَ ذلك الكيان. صار البناء هنا، فصار إثباته هنا.
// ============================================================================
public class CustomerAccountProfilesTests
{
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();

    [Fact]
    public async Task ملفّ_الحساب_الجديد_يُبنى_بمعرّف_الحساب_واسمه_وبريده_ويُدرَج_بلا_حفظ()
    {
        Customer? added = null;
        _customers.When(c => c.AddAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>()))
                  .Do(call => added = call.Arg<Customer>());

        await new CustomerAccountProfiles(_customers)
            .CreateForAccountAsync(42, "مستخدم جديد", "new@souq.com", CancellationToken.None);

        (added!.UserId, added.FullName, added.Email).Should().Be((42, "مستخدم جديد", "new@souq.com"));
        // بلا حفظ: التسجيل وحدة واحدة، ومعاملة RegisterHandler هي من تُودِع.
        await _customers.Received(1).AddAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task معرّف_الملفّ_يُقرأ_بمعرّف_الحساب_وغيابه_null()
    {
        _customers.FindIdByUserIdAsync(42, Arg.Any<CancellationToken>()).Returns(7);
        _customers.FindIdByUserIdAsync(99, Arg.Any<CancellationToken>()).Returns((int?)null);
        var profiles = new CustomerAccountProfiles(_customers);

        (await profiles.FindIdForAccountAsync(42, CancellationToken.None)).Should().Be(7);
        // حسابٌ بلا ملفّ (موظّف متجر، أو حساب منصّة): null لا صفر ولا استثناء — تدخل المطالبة فارغةً.
        (await profiles.FindIdForAccountAsync(99, CancellationToken.None)).Should().BeNull();
    }
}
