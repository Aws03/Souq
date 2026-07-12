using FluentAssertions;
using NSubstitute;
using Souq.Application.Common.Interfaces;
using Souq.Application.Features.Auth.Commands;
using Souq.Domain.Common;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Tests.Auth;

public class RegisterHandlerTests
{
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtTokenGenerator _jwt = Substitute.For<IJwtTokenGenerator>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private RegisterHandler CreateHandler() => new(_customers, _hasher, _jwt, _uow);

    [Fact]
    public async Task بريد_مستخدم_مسبقاً_يُرفض_ولا_يُنشئ_حساباً()
    {
        _customers.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Customer("موجود", "taken@souq.com", "hash"));

        var result = await CreateHandler().Handle(
            new RegisterCommand("جديد", "taken@souq.com", "Passw0rd!"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("EmailTaken");
        await _customers.DidNotReceive().AddAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task تسجيل_صالح_يُجزّئ_كلمة_المرور_ويمنح_دور_Customer_ويصدر_توكناً()
    {
        _customers.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);
        _hasher.Hash("Passw0rd!").Returns("hashed-value");
        _jwt.Generate(Arg.Any<Customer>()).Returns(("jwt-token", DateTime.UtcNow.AddHours(1)));

        Customer? created = null;
        _customers.When(x => x.AddAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>()))
            .Do(ci => created = ci.Arg<Customer>());

        var result = await CreateHandler().Handle(
            new RegisterCommand("مستخدم جديد", "New@Souq.com", "Passw0rd!"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Token.Should().Be("jwt-token");
        result.Value.User.Role.Should().Be(Roles.Customer);

        created.Should().NotBeNull();
        created!.PasswordHash.Should().Be("hashed-value");
        created.Email.Should().Be("new@souq.com"); // مُطبَّع لحروف صغيرة
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

public class LoginHandlerTests
{
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtTokenGenerator _jwt = Substitute.For<IJwtTokenGenerator>();

    private LoginHandler CreateHandler() => new(_customers, _hasher, _jwt);

    [Fact]
    public async Task بريد_غير_مسجّل_يُرجع_رسالة_موحّدة()
    {
        _customers.GetByEmailAsync("nobody@souq.com", Arg.Any<CancellationToken>()).Returns((Customer?)null);

        var result = await CreateHandler().Handle(
            new LoginCommand("nobody@souq.com", "whatever"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("InvalidCredentials");
    }

    [Fact]
    public async Task كلمة_مرور_خاطئة_تُرجع_نفس_الرسالة_الموحّدة()
    {
        var customer = new Customer("مستخدم", "user@souq.com", "hashed");
        _customers.GetByEmailAsync("user@souq.com", Arg.Any<CancellationToken>()).Returns(customer);
        _hasher.Verify("wrong", "hashed").Returns(false);

        var result = await CreateHandler().Handle(
            new LoginCommand("user@souq.com", "wrong"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("InvalidCredentials");
    }

    [Fact]
    public async Task دخول_صالح_يصدر_توكناً_ويحمل_بيانات_المستخدم()
    {
        var customer = new Customer("مستخدم", "user@souq.com", "hashed", Roles.Admin);
        _customers.GetByEmailAsync("user@souq.com", Arg.Any<CancellationToken>()).Returns(customer);
        _hasher.Verify("correct", "hashed").Returns(true);
        _jwt.Generate(customer).Returns(("jwt-token", DateTime.UtcNow.AddHours(1)));

        var result = await CreateHandler().Handle(
            new LoginCommand("user@souq.com", "correct"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Token.Should().Be("jwt-token");
        result.Value.User.Role.Should().Be(Roles.Admin);
    }
}

public class ForgotPasswordHandlerTests
{
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private ForgotPasswordHandler CreateHandler() => new(_customers, _email, _uow);

    [Fact]
    public async Task بريد_موجود_يولّد_رمزاً_ويرسل_بريد_إعادة_التعيين()
    {
        var customer = new Customer("مستخدم", "user@souq.com", "hashed");
        _customers.GetByEmailAsync("user@souq.com", Arg.Any<CancellationToken>()).Returns(customer);

        var result = await CreateHandler().Handle(new ForgotPasswordCommand("user@souq.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        customer.PasswordResetToken.Should().NotBeNullOrWhiteSpace();
        await _email.Received(1).SendPasswordResetEmailAsync(
            "user@souq.com", customer.PasswordResetToken!, Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task بريد_غير_موجود_يُرجع_نجاحاً_دون_إرسال_بريد()
    {
        // نجاح دائماً — لا نكشف عبر رمز/زمن استجابة مختلفَين إن كان البريد مسجّلاً
        // (Enumeration). لكن لا يجب أن يُرسَل بريد فعلياً لعنوان غير موجود أصلاً.
        _customers.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);

        var result = await CreateHandler().Handle(new ForgotPasswordCommand("nobody@souq.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _email.DidNotReceive().SendPasswordResetEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

public class ResetPasswordHandlerTests
{
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private ResetPasswordHandler CreateHandler() => new(_customers, _hasher, _uow);

    [Fact]
    public async Task رمز_غير_موجود_يُرجع_InvalidToken()
    {
        _customers.GetByResetTokenAsync("bad-token", Arg.Any<CancellationToken>()).Returns((Customer?)null);

        var result = await CreateHandler().Handle(new ResetPasswordCommand("bad-token", "NewPassw0rd!"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("InvalidToken");
    }

    [Fact]
    public async Task رمز_منتهي_الصلاحية_يُرجع_TokenExpired_ولا_يحفظ()
    {
        var customer = new Customer("مستخدم", "user@souq.com", "old-hash");
        customer.GenerateResetToken();
        typeof(Customer).GetProperty(nameof(Customer.PasswordResetTokenExpiry))!
            .SetValue(customer, DateTime.UtcNow.AddMinutes(-1));
        _customers.GetByResetTokenAsync("expired-token", Arg.Any<CancellationToken>()).Returns(customer);

        var result = await CreateHandler().Handle(new ResetPasswordCommand("expired-token", "NewPassw0rd!"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("TokenExpired");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task رمز_صالح_يُحدّث_كلمة_المرور_ويمسح_الرمز_ويحفظ()
    {
        var customer = new Customer("مستخدم", "user@souq.com", "old-hash");
        var token = customer.GenerateResetToken();
        _customers.GetByResetTokenAsync(token, Arg.Any<CancellationToken>()).Returns(customer);
        _hasher.Hash("NewPassw0rd!").Returns("new-hashed-value");

        var result = await CreateHandler().Handle(new ResetPasswordCommand(token, "NewPassw0rd!"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        customer.PasswordHash.Should().Be("new-hashed-value");
        customer.PasswordResetToken.Should().BeNull();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
