using AwesomeAssertions;
using Souq.Domain.Common;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.Identity;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

// ملف العميل ودفتر عناوينه (المرحلة 7): عنوان افتراضي واحد للشحن وآخر للفوترة ما دام له عنوان، حدّ للدفتر، حظر
// تجاري، ومحو يزيل البيانات الشخصية ويُبقي المعرّف.
public class CustomerProfileTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

    private static Customer NewCustomer() => new(userId: 7, "سارة أحمد", "Sara@Souq.Test");

    private static PostalAddress Address(string city = "عمّان", string line1 = "شارع الجامعة 12") =>
        new("سارة أحمد", "+962 79 000 0000", "jo", city, line1, region: "العاصمة");

    private static CustomerAddress WithId(CustomerAddress address, int id)
    {
        typeof(Souq.Domain.Common.Entity).GetProperty("Id")!.SetValue(address, id);
        return address;
    }

    [Fact]
    public void العنوان_يُطبَّع_ويُتحقَّق_منه()
    {
        var address = new PostalAddress("  سارة  ", " 0790000000 ", " jo ", " عمّان ", " شارع 1 ", " ", null, " 11942 ");

        (address.RecipientName, address.Phone, address.Country, address.City, address.Line1, address.Region, address.PostalCode)
            .Should().Be(("سارة", "0790000000", "JO", "عمّان", "شارع 1", null, "11942"));

        ((Action)(() => new PostalAddress("س", "079", "JO", "عمّان", "شارع"))).Should().Throw<InvalidCustomerDataException>();
        ((Action)(() => new PostalAddress("س", "0790000000", "Jordan", "عمّان", "شارع"))).Should().Throw<InvalidCustomerDataException>();
        ((Action)(() => new PostalAddress("س", "0790000000", "JO", " ", "شارع"))).Should().Throw<InvalidCustomerDataException>();
        ((Action)(() => new PostalAddress("س", "0790000000", "JO", "عمّان", new string('x', 201)))).Should().Throw<InvalidCustomerDataException>();
    }

    [Fact]
    public void لقطة_الشحن_سطر_واحد_مقصوص_لحدّ_الطلب()
    {
        var line = Address().ToSingleLine(500);

        line.Should().Be("سارة أحمد، +962 79 000 0000، شارع الجامعة 12، عمّان، العاصمة، JO");
        Address().ToSingleLine(10).Should().HaveLength(10);
    }

    [Fact]
    public void أول_عنوان_افتراضي_للشحن_والفوترة_والافتراضي_واحد_دائماً()
    {
        var customer = NewCustomer();
        var home = WithId(customer.AddAddress(Address(), "المنزل"), 1);
        var work = WithId(customer.AddAddress(Address(line1: "شارع المكتب"), "العمل", defaultShipping: true), 2);

        (home.IsDefaultShipping, home.IsDefaultBilling).Should().Be((false, true));
        (work.IsDefaultShipping, work.IsDefaultBilling).Should().Be((true, false));
        customer.DefaultShippingAddress.Should().BeSameAs(work);

        customer.SetDefaultBilling(2);
        customer.Addresses.Count(a => a.IsDefaultBilling).Should().Be(1);
        customer.Addresses.Count(a => a.IsDefaultShipping).Should().Be(1);
    }

    [Fact]
    public void حذف_الافتراضي_ينقل_صفته_لأقدم_عنوان_باقٍ()
    {
        var customer = NewCustomer();
        var first = WithId(customer.AddAddress(Address(), null), 1);
        WithId(customer.AddAddress(Address(line1: "ثانٍ"), null, defaultShipping: true, defaultBilling: true), 2);

        customer.RemoveAddress(2);

        (first.IsDefaultShipping, first.IsDefaultBilling).Should().Be((true, true));
        ((Action)(() => customer.RemoveAddress(99))).Should().Throw<InvalidCustomerDataException>();
    }

    [Fact]
    public void الدفتر_محدود_والاسم_المختصر_محدود()
    {
        var customer = NewCustomer();
        for (var i = 0; i < Customer.MaxAddresses; i++) customer.AddAddress(Address(line1: $"شارع {i}"), null);

        ((Action)(() => customer.AddAddress(Address(), null))).Should().Throw<InvalidCustomerDataException>();
        ((Action)(() => NewCustomer().AddAddress(Address(), new string('x', CustomerAddress.LabelMaxLength + 1))))
            .Should().Throw<InvalidCustomerDataException>();
    }

    [Fact]
    public void تحديث_الملف_يتحقّق_من_الهاتف_والبريد_يبقى_بريد_التواصل_مطبَّعاً()
    {
        var customer = NewCustomer();

        customer.UpdateProfile("سارة", " +962790000000 ");

        (customer.FullName, customer.Phone, customer.Email).Should().Be(("سارة", "+962790000000", "sara@souq.test"));
        ((Action)(() => customer.UpdateProfile("سارة", "abc"))).Should().Throw<InvalidCustomerDataException>();
        customer.UpdateProfile("سارة", null);
        customer.Phone.Should().BeNull();
    }

    [Fact]
    public void الحظر_ورفعه()
    {
        var customer = NewCustomer();

        customer.Block(Now);
        (customer.IsBlocked, customer.BlockedAt).Should().Be((true, Now));
        customer.Block(Now.AddDays(1));
        customer.BlockedAt.Should().Be(Now);           // حظر مكرّر لا يغيّر تاريخه

        customer.Unblock();
        (customer.Status, customer.BlockedAt).Should().Be((CustomerStatus.Active, null));
    }

    [Fact]
    public void المحو_يزيل_البيانات_الشخصية_ويحظر_نهائياً_ولا_يقبل_تعديلاً()
    {
        var customer = NewCustomer();
        customer.UpdateProfile("سارة", "0790000000");
        customer.AddAddress(Address(), "المنزل");

        customer.Erase(Now);
        customer.Erase(Now.AddDays(1));                // مضمون التكرار

        (customer.FullName, customer.Phone, customer.IsBlocked, customer.ErasedAt)
            .Should().Be((Customer.ErasedName, null, true, Now));
        customer.Email.Should().EndWith("@erased.invalid").And.NotContain("sara");
        customer.Addresses.Should().BeEmpty();
        ((Action)(() => customer.UpdateProfile("سارة", null))).Should().Throw<InvalidCustomerDataException>();
        ((Action)(() => customer.AddAddress(Address(), null))).Should().Throw<InvalidCustomerDataException>();
        ((Action)(() => customer.Unblock())).Should().Throw<InvalidCustomerDataException>();
    }

    [Fact]
    public void محو_الحساب_يمنع_الدخول_ويزيل_الهوية()
    {
        var user = new User("سارة أحمد", "sara@souq.test", "$2a$hash", Roles.Customer);
        var stamp = user.SecurityStamp;

        user.Erase();

        user.Email.Should().EndWith("@erased.invalid");
        user.NormalizedEmail.Should().Be(User.NormalizeEmail(user.Email));
        (user.PasswordHash, user.Status, user.FullName).Should().Be(("", UserStatus.Disabled, "حساب محذوف"));
        user.SecurityStamp.Should().NotBe(stamp);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void الطلب_يرفض_عنوان_شحن_فارغاً_أو_أطول_من_حدّه(string address)
    {
        ((Action)(() => new Order(1, address, "JOD"))).Should().Throw<InvalidOrderOperationException>();
        ((Action)(() => new Order(1, new string('x', Order.ShippingAddressMaxLength + 1), "JOD")))
            .Should().Throw<InvalidOrderOperationException>();
    }
}
