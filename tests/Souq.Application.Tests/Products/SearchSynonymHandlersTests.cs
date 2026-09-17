using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Products.Commands;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;

namespace Souq.Application.Tests.Products;

// ============================================================================
// معالجات مفردات البحث (M3، ADR-0042). ما يُختبَر هنا هو ما لا يراه المجال: الحدّ لكل متجر، والتكرار
// المقيس **على الصورة المطبَّعة**، و"صفّ متجر آخر غير موجود". أمّا صلاحية الزوج نفسه فقاعدة مجال مُختبَرة
// في SearchSynonymTests ولا تُكرَّر هنا.
// ============================================================================
public class CreateSearchSynonymHandlerTests
{
    private readonly ISearchSynonymRepository _synonyms = Substitute.For<ISearchSynonymRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private CreateSearchSynonymHandler CreateHandler() => new(_synonyms, _uow);

    [Fact]
    public async Task الزوج_الجديد_يُحفظ()
    {
        var result = await CreateHandler().Handle(
            new CreateSearchSynonymCommand("ar", "جوال", "هاتف"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _synonyms.Received(1).AddAsync(
            Arg.Is<SearchSynonym>(s => s.TermNormalized == "جوال" && s.ExpansionNormalized == "هاتف"),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task التكرار_يُقاس_على_الصورة_المطبَّعة_لا_على_ما_كُتب()
    {
        // التاجر كتب "مَكْنَسَة" والصفّ القائم يحمل "مكنسه": الزوج نفسه، فيُرفض قبل أن يرفضه الفهرس الفريد.
        _synonyms.ExistsAsync("ar", "هوفر", "مكنسه", null, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(
            new CreateSearchSynonymCommand("ar", "هوفر", "مَكْنَسَة"), CancellationToken.None);

        result.ErrorCode.Should().Be("SearchSynonymExists");
        await _synonyms.DidNotReceive().AddAsync(Arg.Any<SearchSynonym>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task الحدّ_الأقصى_للمتجر_يُفحص_قبل_أي_عمل()
    {
        // المفردات تُقرأ في كل بحث، فعددها يدخل زمن الاستجابة — الحدّ ليس تعسّفاً.
        _synonyms.CountAsync(Arg.Any<CancellationToken>()).Returns(SearchSynonym.MaxPerStore);

        var result = await CreateHandler().Handle(
            new CreateSearchSynonymCommand("ar", "جوال", "هاتف"), CancellationToken.None);

        result.ErrorCode.Should().Be("SearchSynonymLimitReached");
        await _synonyms.DidNotReceive().AddAsync(Arg.Any<SearchSynonym>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task زوج_غير_صالح_يرتدّ_من_المجال_بلا_حفظ()
    {
        var act = () => CreateHandler().Handle(
            new CreateSearchSynonymCommand("ar", "مكنسة", "مكنسه"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidSearchSynonymException>();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

public class UpdateSearchSynonymHandlerTests
{
    private readonly ISearchSynonymRepository _synonyms = Substitute.For<ISearchSynonymRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private UpdateSearchSynonymHandler CreateHandler() => new(_synonyms, _uow);

    [Fact]
    public async Task صفّ_غير_موجود_يعطي_NotFound_لا_استثناء()
    {
        // مرشّح المستأجر يُخفي صفّ متجر آخر، فيصل هنا كأنّه غير موجود — وهذا هو الجواب الصحيح (404 لا 403).
        _synonyms.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns((SearchSynonym?)null);

        var result = await CreateHandler().Handle(
            new UpdateSearchSynonymCommand(7, "ar", "جوال", "هاتف"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task التعديل_يستثني_الصفّ_نفسه_من_فحص_التكرار()
    {
        // بلا هذا الاستثناء لرفض التعديل نفسه كتكرار عن نفسه — فلا يستطيع التاجر حفظ صفّ بلا تغيير حقيقي.
        var existing = new SearchSynonym("ar", "جوال", "هاتف");
        _synonyms.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(existing);
        _synonyms.ExistsAsync("ar", "جوال", "هاتف", 7, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateHandler().Handle(
            new UpdateSearchSynonymCommand(7, "ar", "جوال", "هاتف"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task التعديل_إلى_زوج_قائم_يُرفض()
    {
        _synonyms.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(new SearchSynonym("ar", "جوال", "هاتف"));
        _synonyms.ExistsAsync("ar", "موبايل", "هاتف", 7, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(
            new UpdateSearchSynonymCommand(7, "ar", "موبايل", "هاتف"), CancellationToken.None);

        result.ErrorCode.Should().Be("SearchSynonymExists");
    }
}

public class DeleteSearchSynonymHandlerTests
{
    private readonly ISearchSynonymRepository _synonyms = Substitute.For<ISearchSynonymRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task الحذف_يزيل_الصفّ_ويحفظ()
    {
        var existing = new SearchSynonym("ar", "جوال", "هاتف");
        _synonyms.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await new DeleteSearchSynonymHandler(_synonyms, _uow)
            .Handle(new DeleteSearchSynonymCommand(3), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _synonyms.Received(1).Remove(existing);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task حذف_غير_موجود_يعطي_NotFound()
    {
        _synonyms.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns((SearchSynonym?)null);

        var result = await new DeleteSearchSynonymHandler(_synonyms, _uow)
            .Handle(new DeleteSearchSynonymCommand(3), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _synonyms.DidNotReceive().Remove(Arg.Any<SearchSynonym>());
    }
}
