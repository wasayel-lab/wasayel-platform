using System.Text.RegularExpressions;
using ACommerce.Kit.Subscriptions;
using Marten;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>عَقد الاستِحقاق — مَفروضاً بِالتَوقيع وبِمَسح الشَجَرَة.</b>
/// هذه الاختِبارات لا تَفحَص «هَل يَعمَل» بَل <b>«هَل يُمكِن أَن
/// يُكتَب الخَطَأ أَصلاً»</b>: الذَرِّيَّة في التَوقيع، والمَعجَم
/// مُغلَق عِندَ مَواضِع الفَحص، والإعلان والجِسم لا يَفتَرِقان.</para>
/// </summary>
public class EntitlementContractTests
{
    // ─── ٥-٣: الذَرِّيَّة مَفروضَة بِالتَوقيع ────────────────────────

    /// <summary>
    /// <para><b>مَن يَملِك <c>IDocumentStore</c> يَفتَح جَلسَةً ثانِيَة
    /// ويَخسَر الذَرِّيَّة بِلا أَن يَشتَكي مُتَرجِم.</b> التَوقيع يَجعَل
    /// الخَطَأ <b>غَير قابِل لِلكِتابَة</b>: كُلّ دالَّة تَستَهلِك
    /// تَقبَل الجَلسَة ولا تَقبَل المَخزَن.</para>
    /// </summary>
    [Fact]
    public void Consuming_methods_take_a_session_and_never_a_store()
    {
        var consuming = typeof(IEntitlements).GetMethods()
            .Where(m => m.Name.Contains("Consume", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(consuming);   // عَدّاد: أَداة تَفحَص صِفراً أَداةٌ عَمياء

        foreach (var m in consuming)
        {
            Assert.Contains(m.GetParameters(), p => p.ParameterType == typeof(IDocumentSession));
            Assert.DoesNotContain(m.GetParameters(), p => p.ParameterType == typeof(IDocumentStore));
        }
    }

    /// <summary>والسُؤال بِلا أَثَر لا يَرى الجَلسَة أَصلاً — فَلا
    /// يُبنى عَلَيه قَرار كِتابَة بِالخَطَأ (القاعِدَة ٧: مُعتَرِضٌ
    /// يُحَوِّل لا يُشارِك مُعامَلَة).</summary>
    [Fact]
    public void Peek_never_sees_a_session_or_a_store()
    {
        var peek = typeof(IEntitlements).GetMethod(nameof(IEntitlements.PeekAsync))!;
        Assert.DoesNotContain(peek.GetParameters(), p => p.ParameterType == typeof(IDocumentSession));
        Assert.DoesNotContain(peek.GetParameters(), p => p.ParameterType == typeof(IDocumentStore));
    }

    // ─── التَنفيذ لا يَسمَح بِما لا يَعرِف ───────────────────────────

    /// <summary>
    /// <para>قُدرَتانِ يَخدِمُهُما هذا التَنفيذ — ونُموُّهُما قَرار
    /// مَرئيّ، وهذا هُوَ السَطر الَّذي يُحَمِّر نُمُوّاً صامِتاً.</para>
    ///
    /// <para><b>والثانِيَة دَخَلَت بِمَوجَة سَطح الـAPI</b>:
    /// <c>api.call</c> رايَةٌ لا حِصَّة، فَلا تَمُرّ بِعَدّاد التَيار
    /// — <c>Decide</c> يَفصِل الصِنفَين، و<c>ConsumeAsync</c> لا
    /// يُلحِق <c>QuotaConsumed</c> لِرايَة.</para>
    /// </summary>
    [Fact]
    public void Subscription_entitlements_serve_exactly_api_call_and_listing_create()
        => Assert.Equal(
            new[] { "api.call", "listing.create" },
            new SubscriptionEntitlements(null!).Handles.ToArray());

    /// <summary>
    /// <para><b>«سَمَحتُ لِأَنّي لا أَعرِف» ممنوع.</b> قُدرَة مِن
    /// المَعجَم لا يَخدِمُها هذا التَنفيذ تَرمي — لا تَمُرّ. هذا
    /// بِعَينِه شَكل العَطَب الَّذي قَتَلَ <c>OperationEngine</c>: يَمُرّ
    /// كُلّ شَيء بِصَمت، ويَخضَرّ كُلّ اختِبار مُوجِب.</para>
    /// </summary>
    [Theory]
    [InlineData("studio.analyze")]
    [InlineData("studio.refine")]
    [InlineData("studio.build")]
    [InlineData("studio.export")]
    public async Task Capabilities_it_does_not_serve_throw_instead_of_passing(string capability)
    {
        var ents = new SubscriptionEntitlements(null!);
        await Assert.ThrowsAsync<NotSupportedException>(
            () => ents.PeekAsync("t", Guid.NewGuid(), capability));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => ents.ConsumeAsync(null!, "t", Guid.NewGuid(), capability));
    }

    // ─── الاستِحقاقُ يُطلَبُ بِقُدرَتِه، ولا يُحقَنُ في تَوقيعِ نُقطَة ──

    /// <summary>
    /// <para><b>لا نُقطَةَ تَأخُذ <c>IEntitlements</c> وَسيطاً.</b>
    /// تَسجيلُ التَنفيذِ مُتَعَدِّد (<c>SubscriptionEntitlements</c> ثُمَّ
    /// <c>TenantPlanEntitlements</c>)، وحَقنُ الواجِهَةِ في تَوقيعِ
    /// لامدا هُوَ <c>GetRequiredService&lt;IEntitlements&gt;()</c>
    /// حَرفاً — <b>فَيُعطي آخِرَ مُسَجَّلٍ صامِتاً</b>. والمَسارُ
    /// الصَحيحُ واحِدٌ: <c>http.Entitlements(capability)</c> الَّذي
    /// يَسأَلُ <c>Handles</c> ويَرمي إن لَم يَخدِمها أَحَد.</para>
    ///
    /// <para><b>الكِلفَةُ الَّتي كَتَبَت هذا الفَحص (‏قيسَ حَيّاً
    /// ‏2026-08-31)</b>: <c>POST /{slug}/listings/create</c> كانَ
    /// يَحقِنُ <c>IEntitlements ents</c>، فَصارَ يَستَلِمُ
    /// <c>TenantPlanEntitlements</c> (يَخدِمُ <c>tenant.write</c>
    /// وَحدَها) ويَسأَلُه <c>listing.create</c> ⇒ <b>‏500
    /// <c>NotSupportedException</c> على كُلِّ نَشرِ إعلانٍ في كُلِّ
    /// مَتجَر</b>. دَخَلَ العَطَبُ مَعَ التَسجيلِ الثاني في
    /// <c>2782d1ab</c> (‏2026-08-23) وعاشَ <b>‏110 كوميتاتٍ وثَمانِيَةَ
    /// أَيّام</b> — <b>والتَعليقُ فَوقَ ذلكَ التَسجيلِ نَفسِه كانَ
    /// يَصِفُ هذا العَطَبَ بِالاسم</b>. تَعليقٌ يَتَنَبَّأُ ولا
    /// يُحَمِّر: تِلكَ هي القاعِدَة ٢ بِعَينِها — «الحَدُّ الَّذي لا
    /// يُقاسُ آلِيّاً يَنهار».</para>
    /// </summary>
    [Fact]
    public void No_endpoint_takes_the_entitlement_interface_as_a_parameter()
    {
        var endpoints = WriteEndpointGuardTests.AllMinimalApiEndpoints().ToList();

        // عَدّاد: أَداةٌ تَفحَصُ صِفراً أَداةٌ عَمياء (القاعِدَة ١٠).
        Assert.True(endpoints.Count >= 99,
            $"أَداة عَمياء: وُجِدَت {endpoints.Count} نُقطَة minimal API — والمَقيس ‏99 فَأَكثَر.");

        var injectors = endpoints
            .Where(e =>
            {
                var arrow = e.Body.IndexOf("=>", StringComparison.Ordinal);
                return arrow >= 0 && EntitlementParameter.IsMatch(e.Body[..arrow]);
            })
            .Select(e => $"{e.Route}   ({e.File})")
            .ToArray();

        Console.WriteLine(
            $"· قِياس حَقن الاستِحقاق: {injectors.Length} نُقطَة تَحقِن IEntitlements " +
            $"مِن {endpoints.Count} نُقطَة.");

        Assert.True(injectors.Length == 0,
            "نُقطَة تَحقِن IEntitlements في تَوقيعِها — الوِعاءُ يُعطيها آخِرَ مُسَجَّلٍ " +
            "صامِتاً، فَتَسأَلُ تَنفيذاً لا يَخدِمُ قُدرَتَها ويَرتَدُّ ‏500:\n  " +
            string.Join("\n  ", injectors) +
            "\nالمَسار الصَحيح: http.Entitlements(CapabilityCatalog.<القُدرَة>) — " +
            "يَسأَلُ Handles ويَرمي عِندَ تَركيبٍ ناقِص، لا يَسمَحُ صامِتاً.");
    }

    /// <summary><c>IEntitlements</c> كَنَوعِ وَسيط — بِحَدِّ كَلِمَة كَي
    /// لا يُطابِقَ اسماً يَنتَهي بِه، وبِـ<c>\s+\w</c> كَي لا يُطابِقَ
    /// نِداءً مِثلَ <c>GetServices&lt;IEntitlements&gt;()</c>.</summary>
    private static readonly Regex EntitlementParameter =
        new(@"\bIEntitlements\s+\w", RegexOptions.Compiled);

    /// <summary>ورَمزٌ خارِج المَعجَم كُلِّه يَرمي قَبل ذلك — البَوّابَة
    /// الأُولى قَبل الثانِيَة.</summary>
    [Theory]
    [InlineData("listing.publish_everywhere")]
    [InlineData("studio.custom_pattern")]
    [InlineData("")]
    public async Task Codes_outside_the_vocabulary_throw_before_anything_else(string capability)
    {
        var ents = new SubscriptionEntitlements(null!);
        await Assert.ThrowsAsync<ArgumentException>(
            () => ents.PeekAsync("t", Guid.NewGuid(), capability));
    }

    // ─── ٥-٢: المَعجَم مُغلَق عِندَ مَواضِع الفَحص ────────────────────

    /// <summary>
    /// <para><b>الطَرَف الَّذي أَعلَنَ <c>PermissionCatalog</c> أَنَّه
    /// تَرَكَه مَفتوحاً — مُغلَقاً هُنا.</b> كُلّ سِلسِلَة تَبلُغ
    /// <c>RequireEntitlement</c> عُضوٌ في <see cref="CapabilityCatalog"/>:
    /// إمّا مَكتوبَةً حَرفِيّاً وهي في المَعجَم، أَو مُحالَةً إلى ثابِت
    /// مِنه.</para>
    ///
    /// <para><b>وعَدّاد لِئَلّا تَكون الأَداة عَمياء</b> (القاعِدَة ١٠):
    /// يَفشَل إن لَم يَجِد مَوضِعاً واحِداً. «صِفر مُخالَفَة» بِلا
    /// عَدّاد لا يُميَّز عَن فَحصٍ لَم يَقرَأ شَيئاً.</para>
    /// </summary>
    [Fact]
    public void Every_RequireEntitlement_argument_comes_from_the_catalog()
    {
        var sites = 0;
        var breaches = new List<string>();

        foreach (var (file, text) in SourceFiles())
        {
            foreach (Match m in RequireEntitlementCall.Matches(text))
            {
                sites++;
                var arg = m.Groups["arg"].Value.Trim();

                // شَكلان مَقبولان لا ثالِث لَهُما:
                //   ١. ثابِت مُعلَن في CapabilityCatalog قيمَتُه في المَعجَم؛
                //   ٢. سِلسِلَة حَرفِيَّة عُضو في المَعجَم.
                var viaConstant = ConstantCall.Match(arg);
                if (viaConstant.Success &&
                    CatalogConstants.TryGetValue(viaConstant.Groups["m"].Value, out var value) &&
                    CapabilityCatalog.Contains(value))
                    continue;

                var literal = StringLiteral.Match(arg);
                if (literal.Success && CapabilityCatalog.Contains(literal.Groups["v"].Value))
                    continue;

                breaches.Add($"{Rel(file)}: RequireEntitlement({arg})");
            }
        }

        Assert.True(sites > 0,
            "أَداة عَمياء: لَم يُعثَر عَلى مَوضِع RequireEntitlement واحِد.");
        Assert.True(breaches.Count == 0,
            "رَمز قُدرَة خارِج المَعجَم في مَوضِع فَحص:\n" + string.Join("\n", breaches));
    }

    /// <summary>
    /// <para><b>الإعلان والجِسم لا يَفتَرِقان</b> — كُلّ مِلَفّ يُعلِن
    /// <c>RequireEntitlement</c> يَذكُر جِسمُه <c>ConsumeAsync</c>،
    /// والعَكس. الإعلانُ وَحدَه يَفحَص ولا يَستَهلِك (فَيَنفَد الرَصيد
    /// أَبَداً)، والجِسمُ وَحدَه حِراسَةٌ لا تُرى في التَوقيع فَتُنسى —
    /// وهو جَذر ثَغرَة الإدارَة المَقيسَة (القاعِدَة ٦).</para>
    /// </summary>
    [Fact]
    public void Declaration_and_body_never_drift_apart()
    {
        var declaring = new List<string>();
        var consuming = new List<string>();

        foreach (var (file, text) in SourceFiles())
        {
            // مِلَفّات الطَبَقَة نَفسِها (العَقد، التَنفيذ، المُرَشِّح،
            // المُوَسِّع) تُعَرِّف الآلِيَّة ولا تَستَعمِلُها.
            if (LayerFiles.Contains(Path.GetFileName(file), StringComparer.Ordinal)) continue;

            // <b>يُقرَأ الكود لا التَعليق</b>: ذِكرُ الاسم في تَعليق
            // شارِح لَيسَ مَوضِعَ نِداء، وعَدُّه كَذلكَ يَجعَل الأَداة
            // تَتَّهِم الوَثيقَة بِأَنَّها كود.
            var code = StripComments(text);

            if (RequireEntitlementCall.IsMatch(code)) declaring.Add(Rel(file));
            if (ConsumeCall.IsMatch(code)) consuming.Add(Rel(file));
        }

        Assert.True(declaring.Count > 0, "أَداة عَمياء: لا مَوضِع إعلان.");

        Assert.Empty(declaring.Except(consuming, StringComparer.Ordinal));
        Assert.Empty(consuming.Except(declaring, StringComparer.Ordinal));
    }

    // ─── الأَدَوات ────────────────────────────────────────────────────

    private static readonly Regex RequireEntitlementCall =
        new(@"RequireEntitlement\(\s*(?<arg>[^,\)]+)", RegexOptions.Compiled);

    private static readonly Regex ConsumeCall =
        new(@"\bConsumeAsync\s*\(", RegexOptions.Compiled);

    /// <summary>مِلَفّات الطَبَقَة — تُعَرِّف الآلِيَّة ولا تَستَعمِلُها،
    /// فَلا تَدخُل في مُقابَلَة الإعلان بِالجِسم.</summary>
    private static readonly string[] LayerFiles =
    {
        "Entitlements.cs",
        "SubscriptionEntitlements.cs",
        // ‏ADR-003: تَنفيذُ الاستِحقاقِ الثاني — مَصدَرُ حَقيقَتِه
        // وَثيقَةُ باقَةِ المُستَأجِر. مِلَفُّ طَبَقَةٍ كَسابِقِه: يُعَرِّف
        // الآلِيَّةَ ولا يَستَعمِلُها.
        "TenantPlanEntitlements.cs",
        // والمُوَجِّهُ الَّذي يَختارُ بَينَهُما — يَقرَأ `Handles` ولا
        // يَفحَص قُدرَةً بِنَفسِه.
        "GateContext.cs",
        "CapabilityCatalog.cs",
        "EntitlementFilter.cs",
        "GateExtensions.cs",
    };

    /// <summary>يَنزِع تَعليقات C# — الكُتلَة والسَطر والوَثيقَة —
    /// لِيُقرَأ الكود وَحدَه.</summary>
    private static string StripComments(string s)
    {
        s = Regex.Replace(s, @"/\*.*?\*/", "", RegexOptions.Singleline);
        s = Regex.Replace(s, @"^[ \t]*///.*$", "", RegexOptions.Multiline);
        s = Regex.Replace(s, @"^[ \t]*//.*$",  "", RegexOptions.Multiline);
        return s;
    }

    private static readonly Regex StringLiteral =
        new("^\"(?<v>[^\"]*)\"$", RegexOptions.Compiled);

    /// <summary><c>…CapabilityCatalog.ListingCreate</c> ← اسم العُضو.</summary>
    private static readonly Regex ConstantCall =
        new(@"(?:^|\.)CapabilityCatalog\.(?<m>[A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.Compiled);

    /// <summary>ثَوابِت المَعجَم بِأَسمائِها وقيَمِها — بِالانعِكاس، فَلا
    /// تُنسَخ قائِمَةٌ ثانِيَة تَنحَرِف.</summary>
    private static readonly IReadOnlyDictionary<string, string> CatalogConstants =
        typeof(CapabilityCatalog)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!, StringComparer.Ordinal);

    private static string Rel(string path) =>
        Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, path).Replace('\\', '/');

    /// <summary>
    /// <para>كُلّ مَصادِر <c>libs</c> و<c>apps</c> — بِلا
    /// <c>obj</c>/<c>bin</c>.</para>
    ///
    /// <para><b>و<c>.razor</c> مِنها، وهذا لَيسَ تَفصيلاً</b>: أَوَّل
    /// نُسخَة مِن هذا المَسح قَرَأَت <c>.cs</c> وَحدَها، فَاتَّهَمَت
    /// <c>ListingViewed</c> بِاليُتم — وهو يُصدَر في
    /// <c>TenantListingDetail.razor:601</c>. الأَداةُ كانَت تَكذِب، لا
    /// المَفحوص. والقاعِدَة ١٠: الأَداةُ تُقاس قَبل أَن يُوثَق بِها.</para>
    /// </summary>
    internal static IEnumerable<(string File, string Text)> SourceFiles()
    {
        foreach (var root in new[] { "libs", "apps" })
        {
            var dir = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, root);
            if (!Directory.Exists(dir)) continue;

            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (!f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                    !f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
                    continue;

                var norm = f.Replace('\\', '/');
                if (norm.Contains("/obj/", StringComparison.Ordinal) ||
                    norm.Contains("/bin/", StringComparison.Ordinal))
                    continue;
                yield return (f, File.ReadAllText(f));
            }
        }
    }
}
