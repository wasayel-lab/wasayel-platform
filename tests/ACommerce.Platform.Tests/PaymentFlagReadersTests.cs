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
    /// <para><b>و«مَعَه» بِكَيفِ الجَمعِ لا بِالجِوار</b> (‏2026-10-24 — مُراجَعَةُ الموجَة ٤، M6 المَحمولَة): «<c>CanCharge(</c> في السَطرِ
    /// نَفسِه» كانَ يَقبَلُ <c>flag || CanCharge(…)</c> وسَطراً يَقرَأُ العَلَمَ ويَسأَلُ <c>CanCharge</c> في جُملَةٍ أُخرى لا تَجمَعُهُما. فَيُقرَأُ
    /// الآنَ بِـ<see cref="FlagReaderRefusal"/> — يُغلِقُ عِندَ الشَكّ، ولا يَقبَلُ إلّا شَكلَينِ مُسَمَّيَين. (‏2026-10-24، الجَولَةُ ١ — يُلحَقُ
    /// ولا يُعادُ حَرفٌ فَوقَه: صارَ شَكلاً واحِداً، وَسيطاً مُباشِراً لِلقَرار؛ شَرطُ <c>if</c> سَقَط — لا يُرى مِنه أَيُّ فَرعٍ يَرسُمُ البِطاقَة.)</para>
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
            var code = Code(File.ReadAllText(f));
            var lines = code.Split('\n');
            var offset = 0;
            for (var i = 0; i < lines.Length; offset += lines[i].Length + 1, i++)
            {
                var line = lines[i];
                if (!line.Contains("PaymentProviderConfigured", StringComparison.Ordinal)) continue;
                var at = $"{Path.GetRelativePath(RepoRoot, f).Replace('\\', '/')}:{i + 1}";
                if (line.Contains("public bool PaymentProviderConfigured { get; set; }", StringComparison.Ordinal)) continue;
                if (Regex.IsMatch(line, @"\bt\.PaymentProviderConfigured\s*(==|=)\s*collects\b")) { writer++; continue; }
                foreach (Match m in Regex.Matches(line, @"\bPaymentProviderConfigured\b"))
                {
                    readers.Add(at);
                    if (FlagReaderRefusal(code, offset + m.Index) is { } why) unguarded.Add($"{at} ({why})");
                }
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
    /// <para><b>أَيُقبَلُ قارِئُ العَلَمِ عِندَ <paramref name="at"/>؟</b> — <c>null</c> إن قُبِل، وإلّا رَمزُ الرَفض. شَكلٌ واحِدٌ لا ثانِيَ لَه،
    /// والفاحِصُ يُغلِقُ عِندَ الشَكّ (‏2026-10-24؛ بِنَمَطِ <c>StudioBillingPriceTests.IsSellCondition</c>):
    /// <b>وَسيطٌ مُباشِرٌ لِلقَرار</b> — أَصغَرُ قَوسٍ يَحوي العَلَمَ قائِمَةُ وَسائِطِ <c>CardFactsAsync(</c> (العَلَمُ ثانِيها و<c>CanCharge</c>
    /// ثالِثُها) أَو <c>new CheckoutCardFacts(</c> (أَوَّلُها وخامِسُها) — بِتَرتيبِ التَوقيعَين — والوَسيطُ هو العَلَمُ بِحَرفِه
    /// (<c>x.PaymentProviderConfigured</c> أَو <c>x?.PaymentProviderConfigured ?? false</c>)، وجارُه <c>PaymentChargeSurface.CanCharge(x)</c>
    /// بِحَرفِه. فَالقَرارُ يُحسَبُ في الدالَّةِ الَّتي تَرى الوَسيطَين مَعاً، لا في نَصٍّ يُقرَأُ هُنا.</para>
    /// <para>وما سِواهُ يُرَدّ — ولَو صَحَّ مَنطِقاً (إسنادٌ إلى مُتَغَيِّرٍ ثُمَّ شَرط): قارِئٌ جَديدٌ يُقَرَّرُ شَكلُه بِاسمِه، لا يُستَنتَج.</para>
    /// <para><b>‏2026-10-24 (الجَولَةُ ١ لِمُراجَعَةِ هذا الفاحِص) — سَقَطَ شَكلٌ ثانٍ كانَ يُقبَل: «شَرطُ <c>if</c>/<c>@if</c> عَطفٌ»</b>
    /// (العَلَمُ و<c>CanCharge</c> طَرَفانِ في عَطفٍ بِلا <c>||</c> ولا <c>?</c> ولا نَفي). كانَ يَقرَأُ الشَرطَ ولا يَرى أَيَّ فَرعٍ يَرسُمُ
    /// البِطاقَة: <c>@if (flag &amp;&amp; CanCharge(p)) { } else { &lt;b&gt;card&lt;/b&gt; }</c> — النَفيُ نَفسُه في الفَرع — مُحقَناً في
    /// <c>CheckoutPage.razor</c> يَمُرُّ ‏4/4 بِـDLL ‏<c>905c230</c>، وكَذا <c>if (flag &amp;&amp; CanCharge(p)) return;</c> ثُمَّ البِطاقَة. ولا قارِئَ
    /// حَيَّ بِه (قارِئا اليَومِ كِلاهُما وَسيطٌ لِـ<c>CardFactsAsync</c>) — فَالشَكلُ بِلا مُستَهلِك (القاعِدَة ١) وفاحِصُه بِلا قُدرَةٍ عَلى
    /// الحُكم (القاعِدَة ٢): لا يُقرَأُ أَيُّ فَرعٍ يُرسَمُ بِنَمَطٍ نَصِّيّ. فَيُرَدُّ كُلُّ <c>if</c> بِـ<c>not_a_named_shape</c>؛ ومَن احتاجَه
    /// يَوماً يُخرِجُ القَرارَ إلى دالَّةٍ تَأخُذُ الوَسيطَين ويُضيفُ اسمَها إلى الشَكلِ هُنا بِقَرارٍ مَرئِيّ.</para>
    /// </summary>
    internal static string? FlagReaderRefusal(string code, int at)
    {
        const string Flag = @"(?:\w+(?:\?\.|\.))+PaymentProviderConfigured";
        const string Charge = @"(?:ACommerce\.Kit\.Payments\.)?PaymentChargeSurface\.CanCharge\([\w.]+\)";
        static string Fold(string s) => Regex.Replace(s, @"\s+", " ").Trim();

        // أَصغَرُ قَوسٍ مَفتوحٍ يَحوي المَوضِع.
        var depth = 0;
        var open = -1;
        for (var i = at - 1; i >= 0; i--)
        {
            if (code[i] == ')') depth++;
            else if (code[i] == '(' && depth-- == 0) { open = i; break; }
        }
        if (open < 0) return "not_in_parens";
        var close = open;
        for (int i = open, d = 0; i < code.Length; i++)
        {
            if (code[i] == '(') d++;
            else if (code[i] == ')' && --d == 0) { close = i; break; }
        }
        if (close <= at) return "unbalanced";
        var inner = code[(open + 1)..close];
        var before = code[..open].TrimEnd();
        // ‏«‎(x?.PaymentProviderConfigured ?? false)‎» قَوسٌ يَلُفُّ العَلَمَ وَحدَه — فَيُقرَأُ القَوسُ الَّذي يَحويه.
        if (Regex.IsMatch(Fold(inner), $@"^{Flag} \?\? false$")) return FlagReaderRefusal(code, open);

        (int Flag, int Charge)? call =
            Regex.IsMatch(before, @"\.CardFactsAsync$") ? (1, 2)
            : Regex.IsMatch(before, @"\bnew\s+CheckoutCardFacts$") ? (0, 4)
            : null;
        if (call is not { } pos) return "not_a_named_shape";

        var args = TopLevelSplit(inner, ',').Select(Fold).ToList();
        if (args.Count <= Math.Max(pos.Flag, pos.Charge)) return "call_arity";
        var flagArg = args.FindIndex(a => a.Contains("PaymentProviderConfigured", StringComparison.Ordinal));
        if (flagArg != pos.Flag) return "call_flag_position";
        if (!Regex.IsMatch(args[pos.Flag], $@"^(?:{Flag}|{Flag} \?\? false|\({Flag} \?\? false\))$")) return "call_flag_not_direct";
        if (!Regex.IsMatch(args[pos.Charge], $"^{Charge}$")) return "call_charge_not_beside";
        return null;
    }

    /// <summary>تَقسيمٌ عِندَ <paramref name="sep"/> خارِجَ <c>( )</c> و<c>{ }</c> و<c>[ ]</c>.</summary>
    private static IEnumerable<string> TopLevelSplit(string s, char sep)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c is '(' or '{' or '[') depth++;
            else if (c is ')' or '}' or ']') depth--;
            else if (depth == 0 && c == sep) { yield return s[start..i]; start = i + 1; }
        }
        yield return s[start..];
    }

    /// <summary><b>حَقنُ عَيب — فاحِصُ قُرّاءِ العَلَمِ يُحمِرُّ فِعلاً</b> (‏2026-10-24): شَكلُ اليَومِ (قارِئا البِطاقَةِ بِحَرفِهِما و<c>new CheckoutCardFacts</c>)
    /// يُقبَل، و<c>flag || CanCharge(…)</c> وأَخَواتُه تُرَدّ — ومِنها سَطرٌ يَقرَأُ العَلَمَ ويَسأَلُ <c>CanCharge</c> في جُملَتَين، وكانَ يَقبَلُه
    /// «في السَطرِ نَفسِه». <b>ولِكُلِّ مَرفوضٍ رَمزُه المُتَوَقَّع</b> (الجَولَةُ ١): فَرَفضٌ بِسَبَبٍ غَيرِ سَبَبِه يَحمَرُّ — والـ<c>if</c> بِأَشكالِه
    /// (العَطفُ الَّذي كانَ يُقبَل، والـ<c>else</c>، والرُجوعُ المُبَكِّر) <c>not_a_named_shape</c>.</summary>
    [Fact]
    public void The_flag_reader_checker_can_actually_go_red()
    {
        var accepted = new[]
        {
            "Facts = await Gateway.CardFactsAsync(Slug, tenant?.PaymentProviderConfigured ?? false, PaymentChargeSurface.CanCharge(Payments), cart,\n            MarketplaceTemplateExtensions.PatternFromTenant(tenant));",
            "var card = await gateway.CardFactsAsync(slug, tenant?.PaymentProviderConfigured ?? false, ACommerce.Kit.Payments.PaymentChargeSurface.CanCharge(payments), cart, pattern);",
            "var f = new CheckoutCardFacts(tenant.PaymentProviderConfigured, sealedKey, vault, client, PaymentChargeSurface.CanCharge(payments));",
            "var card = await gateway.CardFactsAsync(slug, (tenant?.PaymentProviderConfigured ?? false), PaymentChargeSurface.CanCharge(payments), cart);",
        };
        var rejected = new (string Sample, string Code)[]
        {
            ("var card = await gateway.CardFactsAsync(slug, tenant?.PaymentProviderConfigured ?? false || PaymentChargeSurface.CanCharge(payments), PaymentChargeSurface.CanCharge(payments), cart);", "call_flag_not_direct"),
            ("var card = await gateway.CardFactsAsync(slug, (tenant.PaymentProviderConfigured || PaymentChargeSurface.CanCharge(payments)), PaymentChargeSurface.CanCharge(payments));", "not_a_named_shape"),
            ("var card = await gateway.CardFactsAsync(slug, tenant.PaymentProviderConfigured);", "call_arity"),
            ("var card = await gateway.CardFactsAsync(slug, PaymentChargeSurface.CanCharge(payments), tenant.PaymentProviderConfigured);", "call_flag_position"),
            ("var card = await gateway.CardFactsAsync(slug, PaymentChargeSurface.CanCharge(payments), tenant.PaymentProviderConfigured, cart);", "call_flag_position"),
            ("var card = await gateway.CardFactsAsync(slug, tenant.PaymentProviderConfigured, true, cart);", "call_charge_not_beside"),
            ("var card = await gateway.CardFactsAsync(slug, tenant.PaymentProviderConfigured", "unbalanced"),
            ("var card = await gateway.CardFactsAsync(slug, tenant.PaymentProviderConfigured, !PaymentChargeSurface.CanCharge(payments));", "call_charge_not_beside"),
            ("var f = new CheckoutCardFacts(tenant.PaymentProviderConfigured || PaymentChargeSurface.CanCharge(payments), a, b, c, PaymentChargeSurface.CanCharge(payments));", "call_flag_not_direct"),
            ("var ok = Decide(tenant.PaymentProviderConfigured, PaymentChargeSurface.CanCharge(payments));", "not_a_named_shape"),
            ("var on = tenant.PaymentProviderConfigured; var can = PaymentChargeSurface.CanCharge(payments);", "not_in_parens"),
            ("var on = tenant.PaymentProviderConfigured || PaymentChargeSurface.CanCharge(payments);", "not_in_parens"),
            ("if (tenant.PaymentProviderConfigured || PaymentChargeSurface.CanCharge(payments)) { Show(); }", "not_a_named_shape"),
            ("if (tenant.PaymentProviderConfigured && PaymentChargeSurface.CanCharge(payments) || true) { Show(); }", "not_a_named_shape"),
            ("if (!tenant.PaymentProviderConfigured && PaymentChargeSurface.CanCharge(payments)) { Show(); }", "not_a_named_shape"),
            ("if (tenant.PaymentProviderConfigured && !PaymentChargeSurface.CanCharge(payments)) { Show(); }", "not_a_named_shape"),
            ("if (tenant.PaymentProviderConfigured == false && PaymentChargeSurface.CanCharge(payments)) { Show(); }", "not_a_named_shape"),
            ("if (tenant?.PaymentProviderConfigured ?? false && PaymentChargeSurface.CanCharge(payments)) { Show(); }", "not_a_named_shape"),
            ("if (!(tenant.PaymentProviderConfigured && PaymentChargeSurface.CanCharge(payments))) { Show(); }", "not_a_named_shape"),
            ("if (tenant.PaymentProviderConfigured && PaymentChargeSurface.CanCharge(payments) ? false : true) { Show(); }", "not_a_named_shape"),
            // ‏ما كانَ الشَكلُ الثاني يَقبَلُه — العَطفُ في الشَرطِ أَيّاً كانَ فَرعُه — يُرَدُّ الآن:
            ("if (tenant.PaymentProviderConfigured && PaymentChargeSurface.CanCharge(payments)) { Show(); }", "not_a_named_shape"),
            ("@if ((tenant?.PaymentProviderConfigured ?? false) && PaymentChargeSurface.CanCharge(Payments)) { <b>x</b> }", "not_a_named_shape"),
            ("if (ready && tenant.PaymentProviderConfigured && PaymentChargeSurface.CanCharge(payments)) { Show(); }", "not_a_named_shape"),
            ("@if ((Tenant?.PaymentProviderConfigured ?? false) && PaymentChargeSurface.CanCharge(Payments)) { } else { <b>card</b> }", "not_a_named_shape"),
            ("if (tenant.PaymentProviderConfigured && PaymentChargeSurface.CanCharge(payments)) return;\n<b>card</b>", "not_a_named_shape"),
            ("foreach (var v in vendors) { if (v.Tenant.PaymentProviderConfigured && PaymentChargeSurface.CanCharge(payments)) continue; Card(v); }", "not_a_named_shape"),
        };
        string? Verdict(string sample) => FlagReaderRefusal(sample,
            Regex.Match(sample, @"\bPaymentProviderConfigured\b").Index);

        var wrong = accepted.Where(x => Verdict(x) is not null).Select(x => $"رُفِضَ حَيٌّ ({Verdict(x)}): {x}")
            .Concat(rejected.Where(x => Verdict(x.Sample) != x.Code).Select(x => $"رَمزُه {Verdict(x.Sample) ?? "قُبِل"} لا {x.Code}: {x.Sample}"))
            .ToList();
        foreach (var x in rejected) output.WriteLine($"مَرفوضٌ ({Verdict(x.Sample)}): {x.Sample}");
        var codes = rejected.Select(x => x.Code).Distinct().ToArray();
        output.WriteLine($"رُموزُ الرَفضِ المَقيسَة: {codes.Length} — {string.Join(", ", codes)}");
        Assert.True(accepted.Length >= 4 && rejected.Length >= 26 && codes.Length >= 7,
            $"أَداةٌ عَمياء: حُقِنَ {rejected.Length} شَكلاً مَرفوضاً بِـ{codes.Length} رَمزاً و{accepted.Length} مَقبولاً.");
        Assert.True(wrong.Count == 0,
            $"حُقِنَ {rejected.Length} شَكلاً مَرفوضاً و{accepted.Length} مَقبولاً، وأَخطَأَ الفاحِصُ في {wrong.Count}:\n  " + string.Join("\n  ", wrong));

        // ‏والقاعِدَةُ القَديمَةُ («CanCharge( في السَطرِ نَفسِه») كانَت تَقبَلُ مِن المَرفوضاتِ هذِه — تُعَدُّ لِيُرى ما سَدَّه التَشديد.
        var oldRuleAccepted = rejected.Count(x => x.Sample.Contains("PaymentChargeSurface.CanCharge(", StringComparison.Ordinal));
        output.WriteLine($"مَقبولٌ بِالقاعِدَةِ القَديمَةِ مِن المَرفوضات: {oldRuleAccepted}/{rejected.Length}");
        Assert.True(oldRuleAccepted >= 23, $"العَيِّنَةُ لا تَقيسُ ما سَدَّه التَشديد: {oldRuleAccepted}.");
    }

    /// <summary><b>قارِئٌ بِشَكلِ <c>if</c> يُرَدُّ أَيّاً كانَ الفَرعُ الَّذي يَرسُمُ البِطاقَة — مَحقوناً في مِلَفٍّ حَقيقيّ</b> (‏2026-10-24، الجَولَةُ ١):
    /// الصَفُّ الأَوَّلُ يَقرَأُ المِلَفّاتِ بِـ<see cref="Code"/> ثُمَّ <see cref="FlagReaderRefusal"/>؛ فَتُقرَأُ هُنا نُسخَةٌ مِن
    /// <c>CheckoutPage.razor</c> بِالطَريقِ نَفسِه وقَد حُقِنَ فيها قَبلَ <c>@code {</c> قارِئٌ يَرسُمُ البِطاقَةَ في <c>else</c> — الطَفرَةُ الَّتي
    /// مَرَّت ‏4/4 بِـDLL ‏<c>905c230</c> — وفي ‏<c>@code</c> رُجوعٌ مُبَكِّر. ويُقرَأُ قارِئُ اليَومِ في المِلَفِّ نَفسِه مَقبولاً، فَلا يُحمَرُّ لِأَنّ
    /// المِلَفَّ صارَ يُرفَضُ كُلُّه. ويُعَدُّ المَحقونُ فَوقَ قُرّاءِ المِلَفِّ الحَيَّةِ لا رَقماً ثابِتاً.</summary>
    [Fact]
    public void An_if_shaped_reader_is_refused_whichever_branch_draws_the_card()
    {
        var page = Read(Template, "Components", "Pages", "CheckoutPage.razor");
        var at = page.IndexOf("@code {", StringComparison.Ordinal);
        Assert.True(at > 0, "لا @code في CheckoutPage.razor — الأَداةُ عَمياء.");
        const string ElseReader = "@if ((Tenant?.PaymentProviderConfigured ?? false) && PaymentChargeSurface.CanCharge(Payments)) { } else { <b>card</b> }\n\n";
        const string ReturnReader = "\n    void Early() { if (Tenant!.PaymentProviderConfigured && PaymentChargeSurface.CanCharge(Payments)) return; Card(); }\n";
        var brace = page.IndexOf('{', at);
        var mutated = page[..at] + ElseReader + page[at..(brace + 1)] + ReturnReader + page[(brace + 1)..];
        var code = Code(mutated);

        var verdicts = Regex.Matches(code, @"\bPaymentProviderConfigured\b").Select(m =>
        {
            var line = code[..m.Index].Count(c => c == '\n') + 1;
            return (Line: line, Text: code.Split('\n')[line - 1].Trim(), Why: FlagReaderRefusal(code, m.Index));
        }).ToList();
        foreach (var v in verdicts) output.WriteLine($"CheckoutPage.razor(مَحقونَة):{v.Line} — {v.Why ?? "مَقبول"} — {v.Text}");

        var live = Regex.Matches(Code(page), @"\bPaymentProviderConfigured\b").Count;
        Assert.True(live > 0, "صِفرُ قارِئٍ في CheckoutPage.razor — الأَداةُ عَمياء.");
        Assert.Equal(live + 2, verdicts.Count);
        Assert.Equal("not_a_named_shape", verdicts.Single(v => v.Text.StartsWith("@if", StringComparison.Ordinal)).Why);
        Assert.Equal("not_a_named_shape", verdicts.Single(v => v.Text.StartsWith("void Early", StringComparison.Ordinal)).Why);
        Assert.All(verdicts.Where(v => !v.Text.StartsWith("@if", StringComparison.Ordinal) && !v.Text.StartsWith("void Early", StringComparison.Ordinal)),
            v => Assert.Null(v.Why));
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
