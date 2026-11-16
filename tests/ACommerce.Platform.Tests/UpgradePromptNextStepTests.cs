using System.Reflection;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using UpgradePrompt = ACommerce.Templates.Customer.Marketplace.Components.UpgradePrompt;

namespace ACommerce.Platform.Tests;

// ═══ رَسائِلُ الحَدِّ تَقولُ الحَدَّ ولا تُعطي خُطوَةً تالِيَة — والتَرقِيَةُ لا تُباع ═══
//
// قَرارُ المالِك د15 (+د46) «ب» (‏2026-10-29): «تبقى الرسالة تقول الحدّ، وتُضاف إليها جملة ثانية تعطي خطوة حقيقيّة: موعد تجدّد الحصّة، أو قناة
// التواصل لرفع الحدّ. وتُبدَّل كلمة «ترقية» حيث لا توجد درجة تُشترى.» **تَوصيفُ «اليَوم» قَبلَ التَبديل** (القاعِدَة ٣): لافِتَةُ الحَدِّ
// (`UpgradePrompt.razor`) لا تَأخُذُ إلّا السَبَبَ والدَرَجَة — فَلا مَوعِدَ تَجَدُّدٍ تَعرِفُه — ورِسالَتُها جُملَةُ الحَدِّ وَحدَها (مُثَبَّتَةٌ بِحَرفِها في
// `UpgradePromptTextsCharacterizationTests`)؛ ولا مِفتاحَ لِجُملَةِ تَجَدُّدٍ أَو تَواصُلٍ في القامُوس؛ ورَدُّ الوَكيلِ عِندَ حَدِّ التَطبيقاتِ يَنتَهي عِندَ
// القالَبِ المُنَسَّقِ (`AgentStoreLimitRefusalTests`). تُقلَبُ صُفوفُ «Today_» بِأَسمائِها في كوميتِ التَبديل.
public class UpgradePromptNextStepTests(ITestOutputHelper output)
{
    internal static IReadOnlyList<string> PromptParameters() => typeof(UpgradePrompt)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null)
        .Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>لافِتَةٌ واحِدَةٌ بِوَسائِطَ صَريحَة — كَما تَرسُمُها الصَفحَتان.</summary>
    internal static async Task<string> Render(string reason, string tier, string? renewsOn)
    {
        await using var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddSingleton(new L(new Microsoft.AspNetCore.Http.HttpContextAccessor()))
            .BuildServiceProvider();
        await using var renderer = new Microsoft.AspNetCore.Components.Web.HtmlRenderer(services, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<UpgradePrompt>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(UpgradePrompt.Reason)] = reason,
                [nameof(UpgradePrompt.CurrentTier)] = tier,
                [nameof(UpgradePrompt.RenewsOn)] = renewsOn,
            }))).ToHtmlString());
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، كوميتُ التَبديل)</b> — كانَ <c>Today_the_banner_takes_only_a_reason_and_a_tier_and_names_no_next_step</c>
    /// (‏<c>4effde3</c>): «وَسائِطُ اللافِتَة: CurrentTier, Reason · لافِتاتٌ بِخُطوَةٍ تالِيَة: 0/16». والآن لِلافِتَةِ وَسيطٌ ثالِثٌ <c>RenewsOn</c> تَحسِبُه الصَفحَتانِ
    /// بِـ<see cref="StudioLimitNextStep.RenewsAt"/> وتُنَسِّقانِه بِساعَةِ الطَلَب، والمِفتاحانِ في القامُوس — والرَسمُ بِلا وَسيطٍ ولا بَريدٍ كَما كانَ حَرفاً.</summary>
    [Fact]
    public async Task The_banner_takes_the_renewal_date_from_its_page_and_the_two_next_step_keys_exist()
    {
        var parameters = PromptParameters();
        // ‏(‏2026-11-07، مُراجَعَةُ C1 · W13-1·F1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) اللافِتاتُ المَرسومَةُ وَحدَها: السَبَبُ المُتَقاعِدُ لا يَرسُمُ شَيئاً فَلا رِسالَةَ تُقرَأ.
        var all = (await UpgradePromptTextsCharacterizationTests.RenderAll()).Where(b => b.Html.Trim().Length > 0).ToList();
        var withStep = all.Count(b =>
        {
            var m = UpgradePromptTextsCharacterizationTests.MessageOf(b.Html);
            return m.Contains("تَتَجَدَّدُ", StringComparison.Ordinal) || m.Contains("تَواصَل", StringComparison.Ordinal);
        });
        output.WriteLine($"وَسائِطُ اللافِتَة: {string.Join(", ", parameters)} · لافِتاتٌ بِخُطوَةٍ تالِيَة بِلا مَوعِدٍ ولا بَريد: {withStep}/{all.Count}");
        Assert.Equal(new[] { nameof(UpgradePrompt.CurrentTier), nameof(UpgradePrompt.Reason), nameof(UpgradePrompt.RenewsOn) }, parameters);
        Assert.Equal(0, withStep);
        Assert.Equal("تَتَجَدَّدُ حِصَّتُكَ في {0}.", LocaleCatalog.Find(LocaleCatalog.Arabic, StudioLimitNextStep.RenewsOnKey));
        Assert.Equal("لِرَفعِ الحَدِّ تَواصَل مَعَنا عَلى {0}.", LocaleCatalog.Find(LocaleCatalog.Arabic, StudioLimitNextStep.ContactKey));
        foreach (var page in new[] { "StudioHome.razor", "StudioStudy.razor" })
        {
            var code = UpgradePromptPriceTests.Code("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", page);
            Assert.Contains("RenewsOn=\"@renewsOn\"", code, StringComparison.Ordinal);
            Assert.Contains("StudioLimitNextStep.RenewsAt(u.PeriodStart, DateTime.UtcNow) is { } at", code, StringComparison.Ordinal);
            Assert.Contains("Culture.FormatTime(at, \"yyyy-MM-dd\")", code, StringComparison.Ordinal);
        }
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_the_agent_store_limit_refusal_ends_at_the_formatted_limit</c>: الرَدُّ يَنتَهي عِندَ القالَب.
    /// والآن تُتبِعُه جُملَةُ التَواصُلِ مِن <see cref="StudioLimitNextStep.Contact"/> — والقالَبُ ونِداؤُه بِحَرفِهِما.</summary>
    [Fact]
    public void The_agent_store_limit_refusal_is_followed_by_the_same_contact_sentence()
    {
        var body = AgentStoreLimitRefusalTests.CreateTenantBody();
        var at = body.IndexOf("if (!gate.Allowed)", StringComparison.Ordinal);
        Assert.True(at > 0, "لا رَدَّ عِندَ الحَدّ — الأَداةُ عَمياء.");
        var refusal = body[at..body.IndexOf(';', at)];
        output.WriteLine($"الرَدّ: {refusal.Replace('\n', ' ')}");
        Assert.Contains("StoreLimitRefusalKey), gate.Used, gate.Limit),", refusal, StringComparison.Ordinal);
        Assert.Contains("StudioLimitNextStep.Contact(k => LocaleCatalog.Text(LocaleCatalog.Arabic, k))", refusal, StringComparison.Ordinal);
    }

    // ─── الخُطوَتانِ: مُوجَبٌ وسالِب ─────────────────────────────────────────────────────────────

    [Fact]
    public void The_quota_renews_thirty_days_after_the_period_start_and_not_once_it_has_lapsed()
    {
        var now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(now.AddDays(20), StudioLimitNextStep.RenewsAt(now.AddDays(-10), now));
        Assert.Equal(now.AddSeconds(1), StudioLimitNextStep.RenewsAt(now.AddDays(-30).AddSeconds(1), now));
        Assert.Null(StudioLimitNextStep.RenewsAt(now.AddDays(-30), now));     // انقَضَت: الاستِعمالُ التالي يُجَدِّد
        Assert.Null(StudioLimitNextStep.RenewsAt(now.AddDays(-45), now));
    }

    [Fact]
    public void The_contact_sentence_appears_only_once_the_contact_email_is_filled()
    {
        string Filled(string k) => k == StudioLimitNextStep.ContactEmailKey ? "hello@example.com" : LocaleCatalog.Text(LocaleCatalog.Arabic, k);
        Assert.Equal("لِرَفعِ الحَدِّ تَواصَل مَعَنا عَلى hello@example.com.", StudioLimitNextStep.Contact(Filled));
        Assert.True(LocaleCatalog.IsPlaceholder(LocaleCatalog.Text(LocaleCatalog.Arabic, StudioLimitNextStep.ContactEmailKey)), "بَريدُ التَواصُلِ مَملوءٌ — صَفُّ النائِبِ لا يَقيسُ شَيئاً.");
        Assert.Null(StudioLimitNextStep.Contact(k => LocaleCatalog.Text(LocaleCatalog.Arabic, k)));
    }

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-11-07، مُراجَعَةُ C1 · W13-1·F1)</b> — كانَ <c>The_renewal_date_follows_the_monthly_quotas_only</c>: صَفّا analyze وrefine
    /// يُتبِعانِ الحَدَّ بِـ«تَتَجَدَّدُ حِصَّتُكَ في 2026-11-18.»، وbuild بِلا مَوعِد، وexport «غَيرُ مَشمولٍ في باقَتِك.». والحِصَّتانِ الشَهرِيَّتانِ خَرَجَتا مِن الرِحلَة
    /// (·٥) والتَصديرُ بِلا باقَة (·٧) — فَالمُتَقاعِدُ لا يَرسُمُ شَيئاً ولَو أُعطِيَ مَوعِداً، ولافِتَةُ build تَقولُ الحَدَّ بِلا مَوعِد. وجُملَةُ التَجَدُّدِ في القامُوسِ
    /// يَتيمَةٌ مُثَبَّتَةٌ في <c>TierOrphanFieldsTests</c> حَتّى ADR التَسعير.</summary>
    [Theory]
    [InlineData(StudioUpgradeReason.Analyses, "")]
    [InlineData(StudioUpgradeReason.Refines,  "")]
    [InlineData(StudioUpgradeReason.Stores,   "عَدَدُ التَطبيقاتِ في باقَتِكَ مَحدود.")]
    [InlineData(StudioUpgradeReason.Export,   "")]
    public async Task No_drawn_banner_carries_a_renewal_date_and_a_retired_reason_draws_nothing(string reason, string message)
    {
        Assert.False(StudioTierPurchase.SelfServiceCheckoutExists);
        foreach (var tier in TierCatalog.All.Keys)
        {
            var html = await Render(reason, tier, "2026-11-18");
            if (message.Length == 0) Assert.Equal("", html.Trim());
            else Assert.Equal(message, UpgradePromptTextsCharacterizationTests.MessageOf(html));
        }
    }

    /// <summary>عُنوانُ الميزَةِ ورِسالَةُ التَصديرِ بِلا «تَرقِيَة» حينَ لا عَرض — و«الترقية» تَبقى مِفتاحَها لِيَومِ البَيع.</summary>
    [Fact]
    public async Task Without_a_self_purchasable_tier_the_feature_banner_says_not_included_not_upgrade()
    {
        // ‏(‏2026-11-07، مُراجَعَةُ C1 · W13-1·F1) لافِتَةُ «الميزَة» يَرسُمُها السَبَبُ المَجهولُ وَحدَه بَعدَ أَن كَفَّ التَصديرُ عَن الرَسم — الفَرعُ نَفسُه في `TitleFor`؛ وكانَت تُقرَأُ مِن `Export`.
        foreach (var tier in TierCatalog.All.Keys)
        {
            var html = await Render(UpgradePromptLiveReasonsCharacterizationTests.UnknownReason, tier, null);
            Assert.Equal("هذِه الميزَةُ غَيرُ مَشمولَةٍ في باقَتِك", UpgradePromptTextsCharacterizationTests.TitleOf(html));
            Assert.DoesNotContain("تَرقيَة", html, StringComparison.Ordinal);
            Assert.DoesNotContain("الأَعلى", html, StringComparison.Ordinal);
        }
        var code = UpgradePromptPriceTests.Code("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "UpgradePrompt.razor");
        Assert.Contains("Offer is null ? L[\"upgrade.title.feature_not_included\"] : L[\"upgrade.title.feature\"]", code, StringComparison.Ordinal);
        Assert.Contains("Offer is null ? L[\"upgrade.msg.export_not_included\"] : L[\"upgrade.msg.export\"]", code, StringComparison.Ordinal);
        Assert.Contains("if (Offer is not null || limit.Length == 0) return limit;", code, StringComparison.Ordinal);
    }
}
