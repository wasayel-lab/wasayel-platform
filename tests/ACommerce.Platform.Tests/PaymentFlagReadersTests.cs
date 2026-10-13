using System.Text.RegularExpressions;
using ACommerce.Kit.Payments;
using ACommerce.Kit.Subscriptions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ لا بابَ يُفتَحُ عَلى عَلَمِ «الرابِطِ المَلصوق» وَحدَه — ولا بابَ يَسأَلُ مُزَوِّداً لا يَمُرُّ بِه مالُه ═══
//
// **ما سَلَّمَته مُراجَعَةُ المَوجَةِ الأولى** (‏2026-10-11): بابُ شِراءِ الباقَةِ في `MarketplaceTemplateExtensions.cs` يُفتَحُ عَلى
// `Tenant.PaymentProviderConfigured` وَحدَه، بِلا سُؤالِ «أَيَقبِضُ مُزَوِّدُ المَنصَّة؟» — والعِلاجُ المُقتَرَح: `PaymentChargeSurface.CanCharge`
// في قارِئَيه كَما تَفعَلُ بِطاقَةُ إتمامِ الشِراء.
//
// **والمَقيسُ اليَوم (‏2026-10-13) — المُقَدِّمَةُ لَم تَعُد تَصدُق، والعِلاجُ يَضُرّ**:
//   ١. قارِئا بابِ الباقَةِ (`Plans.razor` ونُقطَةُ `/{slug}/plans/{planId}/subscribe`) لا يَقرَآنِ العَلَمَ بَعدَ ‏ADR-032 الخُطوَة 8
//      (تَيّارُ S9، ‏2026-10-12): يَقرَآنِ `TenantProviderSet.PlansPayable` = رابِطُ دَفعٍ مَملوءٌ في رَبطِ المَتجَر، وهُوَ بِعَينِه ما
//      تُصَيِّرُه `PlanPay.razor` — والمُشتَري يَدفَعُ عَلى صَفحَةِ مُزَوِّدِ التاجِرِ إلى حِسابِه، و«وَسايِل لَيسَت في المَسار».
//   ٢. و`CanCharge` يَسأَلُ عَن `IPaymentProvider` **النُسخَة** — وهُوَ في الإنتاجِ `UnavailablePaymentProvider` — والباقَةُ لا تَمُرُّ بِه
//      أَصلاً: `PlanPay.razor` لا يَحقِنُه. فَضَمُّه إلى بابِ الباقَةِ يُغلِقُ في الإنتاجِ باقَةً يَعمَلُ رابِطُها — رَفضٌ كاذِب، لا حارِس.
//   ٣. وقارِئا العَلَمِ الباقِيانِ هُما قارِئا البِطاقَة (`CheckoutPage.razor` ونُقطَةُ `checkout/submit`)، وكِلاهُما يَسأَلُ `CanCharge`
//      مُنذُ المَوجَةِ الأولى. فَالثابِتُ الَّذي أَرادَته المُراجَعَة — «لا بابَ يُفتَحُ عَلى الرابِطِ وَحدَه» — قائِم، ويُثَبَّتُ هُنا:
//      قارِئٌ جَديدٌ لِلعَلَمِ بِلا `CanCharge` يَحمَرُّ بِاسمِه.

public class PaymentFlagReadersTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Read(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path}");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} لَم يُقرَأ.");
        return text;
    }

    /// <summary>الكودُ بِلا تَعليقاتِه — كُتَلُ <c>@* … *@</c> وأَسطُرُ <c>//</c> و<c>///</c> الكامِلَة — بِحِفظِ أَرقامِ الأَسطُر. ولا تُنزَعُ
    /// <c>/* … */</c> ولا <c>//</c> بَعدَ كود: مَساراتٌ مِثلُ <c>"/{slug}/*"</c> ورَوابِطُ <c>https://</c> في السَلاسِلِ تَفتَحُ «تَعليقاً» لا
    /// وُجودَ لَه فَتَمحو كوداً حَقيقيّاً — مَقيسٌ عِندَ كِتابَتِه: مَحا كاتِبَ العَلَمِ في <c>MarketplaceTemplateExtensions.cs</c> فَعُدَّ صِفراً.</summary>
    private static string Code(string text)
    {
        string Blank(Match m) => Regex.Replace(m.Value, "[^\n]", " ");
        text = Regex.Replace(text, @"@\*.*?\*@", Blank, RegexOptions.Singleline);
        return string.Join('\n', text.Split('\n').Select(l => l.TrimStart().StartsWith("//", StringComparison.Ordinal) ? "" : l));
    }

    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    /// <summary>
    /// <para><b>كُلُّ قارِئٍ لِـ<c>Tenant.PaymentProviderConfigured</c> يَسأَلُ مَعَه <c>PaymentChargeSurface.CanCharge</c></b> — في السَطرِ
    /// نَفسِه، كَما يُمَرِّرُهُما قارِئا البِطاقَةِ إلى <c>CardFactsAsync</c>. والتَعريفُ (<c>Tenant.cs</c>) والكاتِبُ الوَحيدُ
    /// (<c>SyncPaymentProviderFlagAsync</c>) لَيسا قارِئَين. يَطبَعُ ما عَدَّه ويَفشَلُ عَلى الصِفر.</para>
    /// </summary>
    [Fact]
    public void Every_reader_of_the_pasted_link_flag_also_asks_whether_the_provider_can_charge()
    {
        var files = new[] { "libs", "apps" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(RepoRoot, d), "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.True(files.Count > 100, $"أَداةٌ عَمياء: {files.Count} مِلَفّاً.");

        var readers = new List<string>();
        var unguarded = new List<string>();
        var writer = 0;
        foreach (var f in files)
        {
            var lines = Code(File.ReadAllText(f)).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!line.Contains("PaymentProviderConfigured", StringComparison.Ordinal)) continue;
                var at = $"{Path.GetRelativePath(RepoRoot, f).Replace('\\', '/')}:{i + 1}";
                if (line.Contains("public bool PaymentProviderConfigured { get; set; }", StringComparison.Ordinal)) continue;
                if (Regex.IsMatch(line, @"\bt\.PaymentProviderConfigured\s*(==|=)\s*collects\b")) { writer++; continue; }
                readers.Add(at);
                if (!line.Contains("PaymentChargeSurface.CanCharge(", StringComparison.Ordinal)) unguarded.Add(at);
            }
        }

        foreach (var r in readers) output.WriteLine($"قارِئ: {r}");
        output.WriteLine($"كاتِب: {writer} سَطراً");
        Assert.Equal(2, writer);   // ‏المُقارَنَةُ والإسنادُ في `SyncPaymentProviderFlagAsync` — كاتِبٌ واحِد
        Assert.True(readers.Count > 0, "أَداةٌ عَمياء: صِفرُ قارِئٍ لِلعَلَم.");
        Assert.True(unguarded.Count == 0,
            $"فُحِصَ {readers.Count} قارِئاً لِـPaymentProviderConfigured، ومِنها {unguarded.Count} بِلا PaymentChargeSurface.CanCharge: "
          + string.Join(", ", unguarded));
    }

    /// <summary>
    /// <para><b>بابُ الباقَةِ يَقرَأُ الرابِطَ الَّذي يُصَيِّرُه، لا العَلَمَ ولا مُزَوِّدَ النُسخَة</b>: الصَفحَةُ ونُقطَةُ الاشتِراكِ ومُلاحَظَةُ
    /// الاستوديو تَقرَأُ <c>PlansPayable</c>، وصَفحَةُ الدَفعِ لا تَحقِنُ <c>IPaymentProvider</c> ولا تَسأَلُ <c>CanCharge</c> — مالُ الباقَةِ
    /// لا يَمُرُّ بِمُزَوِّدِ النُسخَة.</para>
    /// </summary>
    [Fact]
    public void The_plan_door_reads_the_link_it_renders_and_never_the_instance_provider()
    {
        var plans = Code(Read(Template, "Components", "Pages", "Plans.razor"));
        Assert.Contains("PlanPurchasePolicy.Visible(all, (await Providers.ForAsync(Slug)).PlansPayable)", plans, StringComparison.Ordinal);

        var endpoints = Code(Read(Template, "MarketplaceTemplateExtensions.cs"));
        var subscribe = endpoints.IndexOf("app.MapPost(\"/{slug}/plans/{planId}/subscribe\"", StringComparison.Ordinal);
        Assert.True(subscribe > 0, "نُقطَةُ الاشتِراكِ غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        var body = endpoints.Substring(subscribe, endpoints.IndexOf(".RequireStoreWritable();", subscribe, StringComparison.Ordinal) - subscribe);
        Assert.Contains("var configured = (await providers.ForAsync(slug)).PlansPayable;", body, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentProviderConfigured", body, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymentProvider", body, StringComparison.Ordinal);

        var pay = Code(Read(Template, "Components", "Pages", "PlanPay.razor"));
        Assert.DoesNotContain("IPaymentProvider", pay, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentChargeSurface", pay, StringComparison.Ordinal);

        Assert.Contains("plansPayable = (await Providers.ForAsync(Slug)).PlansPayable;",
            Code(Read(Template, "Components", "Pages", "StudioAppPlans.razor")), StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>الجَدوَلُ الموجَبُ والسالِبُ لِلبابَين</b> — البِطاقَةُ تَسأَلُ مُزَوِّدَ النُسخَة (المُعَطَّلُ لا يَقبِض، وغَيرُه يَقبِض)، وبابُ الباقَةِ
    /// لا يَأخُذُ مُزَوِّداً أَصلاً: باقَةٌ بِسِعرٍ تَظهَرُ بِالرابِطِ وتَختَفي بِدونِه، والمَجّانِيَّةُ تَظهَرُ في الحالَين.</para>
    /// </summary>
    [Fact]
    public void Card_asks_the_instance_provider_while_the_plan_door_asks_only_the_link()
    {
        Assert.False(PaymentChargeSurface.CanCharge(new UnavailablePaymentProvider()));
        Assert.False(PaymentChargeSurface.CanCharge(null));

        var free = new Plan { Id = "free", Price = 0m };
        var paid = new Plan { Id = "pro", Price = 199m };
        var all = new[] { free, paid };

        Assert.Equal(new[] { "free", "pro" }, PlanPurchasePolicy.Visible(all, true).Select(p => p.Id));   // رابِطٌ مَملوء
        Assert.Equal(new[] { "free" }, PlanPurchasePolicy.Visible(all, false).Select(p => p.Id));         // بِلا رابِط
        Assert.Equal(PlanPurchasePolicy.PaidUnavailable, PlanPurchasePolicy.Refuse(paid, false));
        Assert.Null(PlanPurchasePolicy.Refuse(paid, true));
        Assert.Null(PlanPurchasePolicy.Refuse(free, false));

        // ‏ولا وَسيطَ مُزَوِّدٍ في تَوقيعِ بابِ الباقَة — بِالانعِكاس، فَإن أُضيفَ حَمِرَ هُنا لِيُقالَ لِماذا.
        foreach (var m in typeof(PlanPurchasePolicy).GetMethods().Where(m => m.Name is "Visible" or "Refuse" or "Decide"))
            Assert.DoesNotContain(m.GetParameters(), p => typeof(IPaymentProvider).IsAssignableFrom(p.ParameterType));
    }
}
