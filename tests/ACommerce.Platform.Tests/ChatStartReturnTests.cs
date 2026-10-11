using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ أُنبوبُ المُحادَثَةِ يَعودُ إلى مَنشَئِه عِندَ الفَشَل — لا إلى /drivers ═══════
//
// **المَقيسُ قَبلَ هذا الكوميت** (مُراجَعَةُ المَوجَة ١؛ مُثَبَّتٌ في
// `DealCashPathCharacterizationTests` ‏5554975): `POST /{slug}/users/{userId}/chat`
// وُلِدَ لِصَفحَةِ السائِقينَ ويُسقِطُ فَشَلَيه (النَفسُ، والطَرَفُ المَحذوف) عَلى
// `drivers` ويُحيلُ الدُخولَ إلى `login?returnUrl=/{slug}/drivers`؛ ولا يَقرَأُ
// `returnUrl`. ومُستَهلِكُه الثاني زِرُّ «تَواصَل مَع البائِع» عَلى صَفحَةِ الصَفقَة
// (‏S1·٧) — فَمُشتَرٍ حُذِفَ حِسابُ بائِعِه كانَ يُقذَفُ إلى صَفحَةِ السائِقينَ بِلا كَلِمَة.
//
// **والقَرار**: المَنشَأُ مِن الاستِمارَة (`returnUrl`) يُحَلُّ بِـ`LocalRedirect.Resolve`
// القائِمَة (القاعِدَة ٨؛ و`LocalRedirectTests.NoEndpointDecidesRedirectLocalityOnItsOwn`
// يَفرِضُ ذلك عَلى كُلِّ قِراءَة)، والفَشَلُ يَعودُ إلَيه بِرَمزِه `partner_missing`
// فَتَرسُمُه صَفحَةُ الصَفقَةِ مِن القامُوس؛ وبِلا مَنشَأٍ يَبقى مَسقَطُ الرَأسِ — وصَفحَةُ
// السائِقينَ لا تَقرَأُ `err` فَيَمُرُّ بِها الرَمزُ بِلا أَثَر (مَقيس أَدناه).

public class ChatStartReturnTests
{
    private const string Birthplace = "/shop/drivers";
    private const string DealPage   = "/shop/deals/0f0f0f0f-0f0f-4f0f-8f0f-0f0f0f0f0f0f";

    // ─── الدالَّةُ النَقِيَّة ───────────────────────────────────────────────

    /// <summary><b>المُوجِب</b> — حالَةُ البائِعِ المَحذوف: المُشتَري يَعودُ إلى صَفحَةِ
    /// صَفقَتِه بِرَمزٍ تَرسُمُه الصَفحَةُ.</summary>
    [Fact]
    public void A_deleted_partner_sends_the_buyer_back_to_the_deal_page_with_the_code()
        => Assert.Equal(DealPage + "?err=partner_missing",
            ChatStartReturn.Failure(DealPage, ChatStartReturn.PartnerMissing));

    [Fact]
    public void An_origin_that_already_carries_a_query_gets_the_code_appended()
        => Assert.Equal("/shop/deals/x?tab=1&err=partner_missing",
            ChatStartReturn.Failure("/shop/deals/x?tab=1", ChatStartReturn.PartnerMissing));

    /// <summary><b>السالِب</b> — بِلا مَنشَأٍ مَحَلِّيّ يَحُلُّ <c>LocalRedirect.Resolve</c>
    /// إلى مَسقَطِ الرَأس، فَيَصِلُ الرَمزُ صَفحَةَ السائِقين — <b>الَّتي لا تَقرَأُ
    /// <c>err</c></b>، فَلا أَثَرَ ولا تَحويلَ إلى مُضيفٍ آخَر.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/x")]
    [InlineData("/\\evil.example")]
    [InlineData("javascript:alert(1)")]
    public void Without_a_local_origin_the_failure_lands_on_the_birthplace(string? returnUrl)
    {
        var origin = LocalRedirect.Resolve(returnUrl, Birthplace);
        Assert.Equal(Birthplace, origin);
        Assert.Equal(Birthplace + "?err=partner_missing", ChatStartReturn.Failure(origin, ChatStartReturn.PartnerMissing));
    }

    /// <summary><b>المُوجِب</b> (مُراجَعَةُ المَوجَةِ الثانِيَة، ‏2026-10-11): مَنشَأٌ يَحمِلُ استِعلاماً يَعبُرُ صَفحَةَ
    /// الدُخولِ <b>قيمَةً واحِدَة</b> — كانَ يُحشَرُ خاماً فَيَنقَسِمُ عِندَ <c>&amp;</c> ويَعودُ المُستَخدِمُ إلى نِصفِه.</summary>
    [Fact]
    public void A_query_bearing_origin_crosses_the_login_page_as_one_returnUrl_value()
    {
        const string origin = "/shop/deals/0f0f0f0f-0f0f-4f0f-8f0f-0f0f0f0f0f0f?tab=1&err=partner_missing";
        var login = ChatStartReturn.Login(origin);
        Assert.Equal("login?returnUrl=%2Fshop%2Fdeals%2F0f0f0f0f-0f0f-4f0f-8f0f-0f0f0f0f0f0f%3Ftab%3D1%26err%3Dpartner_missing", login);

        var query = System.Web.HttpUtility.ParseQueryString(login[(login.IndexOf('?') + 1)..]);
        Assert.Single(query.AllKeys);
        Assert.Equal(origin, query["returnUrl"]);
    }

    /// <summary><b>السالِب</b>: مَنشَأٌ بِلا استِعلامٍ يَعودُ بِحَرفِه أَيضاً — التَرميزُ لا يُغَيِّرُ الوِجهَةَ ولا
    /// مَحَلِّيَّتَها (تِلكَ حُكمُ <c>LocalRedirect.Resolve</c> قَبلَه).</summary>
    [Fact]
    public void A_plain_origin_round_trips_unchanged_through_the_login_link()
    {
        var login = ChatStartReturn.Login(Birthplace);
        Assert.Equal("login?returnUrl=%2Fshop%2Fdrivers", login);
        Assert.Equal(Birthplace, System.Web.HttpUtility.ParseQueryString(login[(login.IndexOf('?') + 1)..])["returnUrl"]);
    }

    [Fact]
    public void The_code_has_a_dictionary_key_through_the_deal_refusal_lexicon()
    {
        Assert.Equal("partner_missing", ChatStartReturn.PartnerMissing);
        Assert.Equal("deals.detail.err_partner_missing", DealRefusalTexts.KeyFor(ChatStartReturn.PartnerMissing));
    }

    // ─── والنُقطَةُ والصَفحَتانِ ─────────────────────────────────────────────

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Read(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} لَم يُقرَأ.");
        return text;
    }

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    /// <summary>الأُنبوبُ يَحُلُّ مَنشَأَه بِـ<c>LocalRedirect.Resolve</c> مِن <c>returnUrl</c>،
    /// ويُسقِطُ الطَرَفَ المَحذوفَ عَلى <c>Failure(origin, PartnerMissing)</c> لا عَلى
    /// <c>drivers</c> مُباشَرَةً، وتَعليقُه يُسَمّي مُستَهلِكَه الثاني.</summary>
    [Fact]
    public void The_pipe_resolves_its_origin_and_sends_a_deleted_partner_back_with_the_code()
    {
        var text = Read(Template, "MarketplaceTemplateExtensions.cs");
        var start = text.IndexOf("\"/{slug}/users/{userId:guid}/chat\"", StringComparison.Ordinal);
        Assert.True(start > 0, "أُنبوبُ المُحادَثَةِ غَيرُ مَوجود — الأَداةُ عَمياء.");
        var end = text.IndexOf(".RequireStoreWritable();", start, StringComparison.Ordinal);
        var body = WriteEndpointGuardTests.StripComments(text.Substring(start, end - start));

        Assert.Contains("var origin = Services.LocalRedirect.Resolve(req.Form[\"returnUrl\"].ToString(), Link(req, slug, $\"drivers\"));", body, StringComparison.Ordinal);
        Assert.Contains("if (partner is null) return Results.Redirect(Services.ChatStartReturn.Failure(origin, Services.ChatStartReturn.PartnerMissing));", body, StringComparison.Ordinal);
        Assert.Contains("if (meId == userId) return Results.Redirect(origin);", body, StringComparison.Ordinal);
        // والدُخولُ يَحمِلُ المَنشَأَ مُرَمَّزاً قيمَةً واحِدَة — كانَ `login?returnUrl={origin}` خاماً
        // (مُراجَعَةُ المَوجَةِ الثانِيَة، ‏2026-10-11؛ الصَفّانِ أَدناه).
        Assert.Contains("return Results.Redirect(Link(req, slug, Services.ChatStartReturn.Login(origin)));", body, StringComparison.Ordinal);
        Assert.DoesNotContain("login?returnUrl={origin}", body, StringComparison.Ordinal);
        Assert.Equal(0, Count(body, "return Results.Redirect(Link(req, slug, $\"drivers\"));"));

        // والتَعليقُ فَوقَ الأُنبوبِ يُسَمّي المُستَهلِكَ الثاني بِاسمِ مِلَفِّه.
        var header = text.LastIndexOf("// ─── Start direct chat with another user", start, StringComparison.Ordinal);
        Assert.True(header > 0, "تَرويسَةُ الأُنبوبِ غَيرُ مَوجودَة.");
        Assert.Contains("MyDealDetail.razor", text.Substring(header, start - header), StringComparison.Ordinal);
    }

    /// <summary>وزِرُّ الصَفحَةِ يُرسِلُ مَنشَأَه — داخِلَ فَرعِ المُبادِرِ نَفسِه.</summary>
    [Fact]
    public void The_deal_page_contact_form_carries_its_origin()
    {
        var page = Read(Template, "Components", "Pages", "MyDealDetail.razor");
        var at = page.IndexOf("@if (isInitiator && vendorId is { } vid)", StringComparison.Ordinal);
        Assert.True(at > 0, "فَرعُ زِرِّ البائِعِ غَيرُ مَوجود — الأَداةُ عَمياء.");
        var block = page.Substring(at, page.IndexOf("</form>", at, StringComparison.Ordinal) - at);
        Assert.Contains("action=\"@($\"/{Slug}/users/{vid}/chat\")\"", block, StringComparison.Ordinal);
        Assert.Contains("<input type=\"hidden\" name=\"returnUrl\" value=\"@AuthSession.LinkFor(Slug, Role, $\"deals/{deal.Id}\")\" />", block, StringComparison.Ordinal);
        Assert.Equal(1, Count(page, "name=\"returnUrl\""));
    }

    /// <summary>ومَسقَطُ الرَأسِ لا يَقرَأُ <c>err</c> — فَالرَمزُ الَّذي يَصِلُه بِلا مَنشَأٍ
    /// يَمُرُّ بِلا أَثَر (يُقالُ ويُقاس، لا يُفتَرَض).</summary>
    [Fact]
    public void The_drivers_page_does_not_read_err_so_the_code_is_inert_there()
    {
        var drivers = Read(Template, "Components", "Pages", "TenantDrivers.razor");
        Assert.DoesNotContain("SupplyParameterFromQuery", drivers, StringComparison.Ordinal);
        Assert.Contains("@page \"/{slug}/drivers\"", drivers, StringComparison.Ordinal);
    }
}
