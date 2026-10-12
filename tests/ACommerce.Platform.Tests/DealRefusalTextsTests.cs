using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ رَفضُ النُقطَةِ يُقرَأُ عَلى صَفحَةِ الصَفقَةِ لا يُبتلَع ═══════════════════
//
// **المَقيسُ قَبلَ هذا الكوميت** (مُراجَعَةُ المَوجَة ١؛ مُثَبَّتٌ في
// `DealCashPathCharacterizationTests` ‏5554975): `/accept` و`/cancel` تَرتَدّانِ إلى
// `deals/{id}?err=<code>` وفي `MyDealDetail.razor` **صِفرُ** `SupplyParameterFromQuery` —
// فَالنَقرَةُ المَرفوضَةُ تَعودُ بِصَفحَةٍ كَأَنَّ شَيئاً لَم يَكُن.
//
// **والقَرار**: مُعامِلُ `err` ودالَّةٌ نَقِيَّةٌ `DealRefusalTexts.KeyFor` تَحُلُّ الرَمزَ
// مِفتاحاً مِن مَعجَمَي التَخويلِ وأُنبوبِ المُحادَثَة، والكَلامُ في `ar.json` (القاعِدَة
// ١١) — نَسَقُ `store.locked.write_denied` في `TenantHome.razor`. **لِكُلِّ رَمزٍ مِفتاحٌ
// حَيٌّ**، وما خَرَجَ عَن المَعجَمِ لا يُرسَمُ لَه شَيء.

public class DealRefusalTextsTests
{
    [Fact]
    public void The_lexicon_is_the_union_of_both_authorizations_and_the_chat_pipe_code_with_shared_codes_once()
    {
        // ورَمزُ تَعارُضِ النُسخَةِ عُضوٌ تاسِع (‏ADR-036، ‏2026-10-12) — يُضافُ هُنا بِاسمِه، والعَدُّ 8 ← 9.
        // ورُموزُ نُقطَةِ الشَحنِ ومَعجَمُ عَميلِ OTO (‏ADR-033 الخُطوَة 3، ‏2026-10-12) — يُضافانِ هُنا بِاسمَيهِما،
        // والعَدُّ 9 ← 21.
        var expected = DealAcceptAuthorization.All
            .Concat(DealCancelAuthorization.All)
            .Append(ChatStartReturn.PartnerMissing)
            .Append(DealsService.ChangedMeanwhile)
            .Concat(DealShipAuthorization.All)
            .Concat(ACommerce.Kit.Delivery.OtoFailures.All)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal);
        Assert.Equal(expected, DealRefusalTexts.Codes.OrderBy(c => c, StringComparer.Ordinal));
        // 6 + 3 − 2 مُشتَرَكَين (deal_not_found، deal_not_active) + 1 + 1 = 9؛
        // ثُمَّ الشَحن: 10 − 3 مُشتَرَكَةٍ مَعَ القَبول (deal_not_found، listing_owner_missing، deal_not_active) = 7،
        // وعَميلُ OTO 5 — فَـ9 + 7 + 5 = 21.
        Assert.Equal(21, DealRefusalTexts.Codes.Count);
        Assert.Equal(DealRefusalTexts.Codes.Count, DealRefusalTexts.Codes.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary><b>المُوجِب لِكُلِّ رَمز</b>: مِفتاحٌ بِالبادِئَةِ، ونَصٌّ عَرَبِيٌّ حَيٌّ في
    /// القامُوس لَيسَ نائِباً ولا المِفتاحَ خاماً.</summary>
    [Fact]
    public void Every_code_of_both_lexicons_and_the_chat_pipe_has_a_live_arabic_text()
    {
        var checked_ = 0;
        foreach (var code in DealAcceptAuthorization.All.Concat(DealCancelAuthorization.All).Append(ChatStartReturn.PartnerMissing)
                     .Append(DealsService.ChangedMeanwhile)
                     .Concat(DealShipAuthorization.All).Concat(ACommerce.Kit.Delivery.OtoFailures.All))
        {
            var key = DealRefusalTexts.KeyFor(code);
            Assert.Equal(DealRefusalTexts.Prefix + code, key);
            var text = LocaleCatalog.Text(LocaleCatalog.Arabic, key!);
            Assert.NotEqual(key, text);
            Assert.False(LocaleCatalog.IsPlaceholder(text), $"‏{key} نائِب.");
            Assert.True(text.Length > 10, $"‏{key} بِنَصٍّ أَقصَرَ مِن أَن يُفَسِّر.");
            checked_++;
        }
        // 6 + 3 + 1 + 1 (‏ADR-036) + 10 + 5 (‏ADR-033 الخُطوَة 3) — المُشتَرَكُ يُفحَصُ مَرَّتَين ويَصِلُ مِفتاحاً واحِداً
        Assert.Equal(26, checked_);
    }

    /// <summary><b>نَصُّ «بِلا مالِكِ إعلان» يُسَمّي مَن يُحَرِّكُها حَقّاً</b> (‏2026-10-11، مُراجَعَةُ الموجَةِ الثانِيَةِ
    /// الثانِيَة): كانَ «يُحَرِّكُها مُشرِفُ المَتجَر وَحدَه» — ولا دَورَ «مُشرِف مَتجَر» يُحَرِّكُ صَفقَة: البابُ الوَحيدُ
    /// الَّذي يُعَيِّنُ طَرَفاً لِصَفقَةٍ بِلا طَرَفٍ ويُحَرِّكُها مَسارُ الاستوديو، خَلفَ <c>StudioOwnsAsync</c> — مالِكُ
    /// المَتجَرِ نَفسُه. فَالنَصُّ يُقاسُ بِالكودِ الَّذي يَصِفُه: إن انتَقَلَ البابُ احمَرَّ هذا.</summary>
    [Fact]
    public void The_listing_owner_missing_text_names_the_store_owner_in_the_studio_who_alone_moves_it()
    {
        var text = LocaleCatalog.Text(LocaleCatalog.Arabic, DealRefusalTexts.KeyFor(DealAcceptAuthorization.ListingOwnerMissing)!);
        Assert.Equal("هذِه الصَّفقَة بِلا مالِكِ إعلانٍ مُسَجَّل — يُحَرِّكُها مالِكُ المَتجَر مِن الاستوديو وَحدَه.", text);
        Assert.DoesNotContain("مُشرِف", text, StringComparison.Ordinal);

        var endpoints = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs")));
        var start = endpoints.IndexOf("\"/studio/apps/{slug}/deals/{id:guid}/advance\"", StringComparison.Ordinal);
        Assert.True(start > 0, "مَسارُ الاستوديو لِلتَحريكِ غَيرُ مَوجود — الأَداةُ عَمياء.");
        var studio = endpoints.Substring(start, endpoints.IndexOf(".DisableAntiforgery();", start, StringComparison.Ordinal) - start);
        var owns   = studio.IndexOf("if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect(\"/studio\");", StringComparison.Ordinal);
        var assign = studio.IndexOf("if (deal.CounterpartyId is null)", StringComparison.Ordinal);
        Assert.True(owns > 0 && assign > owns, $"البابُ لا يَسأَلُ المِلكِيَّةَ قَبلَ التَعيين: owns={owns} assign={assign}");
    }

    /// <summary><b>نَصُّ تَعارُضِ النُسخَةِ بِحَرفِه</b> (‏ADR-036، ‏2026-10-12): يُرسَمُ لِـ<c>?err=deal_changed_meanwhile</c>
    /// عَلى صَفحَةِ الصَفقَةِ بَعدَ <c>/accept</c> أَو <c>/cancel</c> أَو <c>/advance</c> — والمُحاوَلَةُ الثانِيَةُ تَقرَأُ ما كُتِب.</summary>
    [Fact]
    public void The_changed_meanwhile_code_reads_the_dictionary_text_verbatim()
    {
        Assert.Equal("deal_changed_meanwhile", DealsService.ChangedMeanwhile);
        Assert.Equal("deals.detail.err_deal_changed_meanwhile", DealRefusalTexts.KeyFor(DealsService.ChangedMeanwhile));
        Assert.Equal("تَغَيَّرَت الصَفقَةُ في الأَثناء — أَعِد المُحاوَلَة",
            LocaleCatalog.Text(LocaleCatalog.Arabic, "deals.detail.err_deal_changed_meanwhile"));
    }

    /// <summary><b>السالِب</b>: رَمزٌ خارِجَ المَعجَم، أَو غِيابٌ، أَو فَراغ — لا مِفتاحَ،
    /// فَلا شَريطَ ولا مِفتاحَ خاماً عَلى الشاشَة.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("expired")]
    [InlineData("deal_not_found ")]
    [InlineData("DEAL_NOT_FOUND")]
    [InlineData("pay_card_unavailable")]
    public void A_code_outside_the_lexicon_has_no_key(string? code)
        => Assert.Null(DealRefusalTexts.KeyFor(code));

    // ─── والصَفحَةُ تَقرَأُ المُعامِلَ وتَرسُمُ الشَريطَ مِن الدالَّةِ نَفسِها ──────

    [Fact]
    public void The_deal_page_reads_err_and_renders_the_strip_through_the_lexicon()
    {
        var page = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "MyDealDetail.razor"));
        Assert.True(page.Length > 1000, "أَداةٌ عَمياء: `MyDealDetail.razor` لَم يُقرَأ.");

        Assert.Contains("[SupplyParameterFromQuery(Name = \"err\")] public string? Err { get; set; }", page, StringComparison.Ordinal);
        Assert.Contains("@if (DealRefusalTexts.KeyFor(Err) is { } errKey)", page, StringComparison.Ordinal);
        Assert.Contains("<AcAlert Variant=\"danger\" Message=\"@L[errKey]\" />", page, StringComparison.Ordinal);
        // ولا نَصَّ رَفضٍ مَكتوباً بِاليَد في الصَفحَة — المِفتاحُ مِن الدالَّة.
        Assert.DoesNotContain("deals.detail.err_", page, StringComparison.Ordinal);
        // واصطِلاحُ المِفتاحِ ثَلاثَةُ مَقاطِعَ (LocaleValidator.KeyPattern) — الرَمزُ في الثالِث.
        Assert.Equal("deals.detail.err_", DealRefusalTexts.Prefix);
        Assert.All(DealRefusalTexts.Codes, c => Assert.Equal(3, DealRefusalTexts.KeyFor(c)!.Split('.').Length));

        // والنُقطَتانِ تَرتَدّانِ بِالرَمزِ نَفسِه الَّذي تَقرَؤُه الصَفحَة.
        var endpoints = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs"));
        Assert.Equal(2, endpoints.Split("$\"?err={res.Violation!.Code}\"").Length - 1);
        // و`/advance` يَرتَدُّ بِرَمزِ نَتيجَتِه حينَ يوجَد (‏ADR-036): اليَومَ تَعارُضُ النُسخَةِ وَحدَه.
        Assert.Equal(1, endpoints.Split("(res.Code is { } code ? $\"?err={code}\" : \"\")").Length - 1);
    }
}
