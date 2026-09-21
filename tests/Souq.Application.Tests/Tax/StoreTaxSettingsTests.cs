using AwesomeAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;

namespace Souq.Application.Tests.Tax;

// ============================================================================
// إعدادُ ضريبةِ متجرٍ: **الاختيار لا القواعد** (ADR-0055).
//
// وما يُختبر هنا هو أن تبقى الحالةُ صادقةً: لا جمعٌ بلا ملفّ، وإلغاءُ الاختيار يُوقف الجمع معه.
// فحالةٌ تقول «أجمع الضريبة» ولا ملفَّ لها تجعل التاجر يظنّ أنه ممتثل وهو لا يجمع شيئاً.
// ============================================================================
public class StoreTaxSettingsTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void الافتراضي_لا_ملفّ_ولا_جمع()
    {
        var settings = StoreTaxSettings.None();

        settings.TaxProfileId.Should().BeNull();
        settings.CollectionEnabled.Should().BeFalse();
        settings.SelectedAt.Should().BeNull();
        settings.RegistrationNumber.Should().BeNull();
    }

    [Fact]
    public void لا_يُفعَّل_الجمع_بلا_ملفّ_مختار()
    {
        var settings = StoreTaxSettings.None();

        ((Action)(() => settings.SetCollection(true))).Should()
            .Throw<InvalidStoreTaxSettingsException>("جمعٌ بلا ملفٍّ حالةٌ تقول ما لا تفعله");
        settings.CollectionEnabled.Should().BeFalse();
    }

    [Fact]
    public void اختيار_ملفّ_ثم_تفعيل_الجمع()
    {
        var settings = StoreTaxSettings.None();

        settings.SelectProfile(7, Now);
        settings.TaxProfileId.Should().Be(7);
        settings.SelectedAt.Should().Be(Now);
        settings.CollectionEnabled.Should().BeFalse("الاختيار وحده لا يجمع: التاجر يقرأ ملاحظات الاختصاص أولاً");

        settings.SetCollection(true);
        settings.CollectionEnabled.Should().BeTrue();
    }

    // إلغاءُ الاختيار يُوقف الجمع معه — لا يبقى مفعَّلاً بلا ملفّ.
    [Fact]
    public void إلغاء_اختيار_الملفّ_يُوقف_الجمع_معه()
    {
        var settings = StoreTaxSettings.None();
        settings.SelectProfile(7, Now);
        settings.SetCollection(true);

        settings.SelectProfile(null, Now);

        settings.TaxProfileId.Should().BeNull();
        settings.CollectionEnabled.Should().BeFalse();
        settings.SelectedAt.Should().BeNull();
    }

    [Fact]
    public void رقم_التسجيل_يُقصّ_ويُرفض_الطويل_والمحارف_الغريبة()
    {
        var settings = StoreTaxSettings.None();

        settings.SetRegistrationNumber("  123/4567  ");
        settings.RegistrationNumber.Should().Be("123/4567", "لا شكلَ مفروضاً: أشكالُ الأرقام تختلف باختلاف الاختصاص");

        settings.SetRegistrationNumber("   ");
        settings.RegistrationNumber.Should().BeNull();

        ((Action)(() => settings.SetRegistrationNumber(new string('9', StoreTaxSettings.RegistrationNumberMaxLength + 1))))
            .Should().Throw<InvalidStoreTaxSettingsException>();
        ((Action)(() => settings.SetRegistrationNumber("123\r\n456")))
            .Should().Throw<InvalidStoreTaxSettingsException>();
    }
}
