using System.Text.RegularExpressions;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>جِسم نُقطَةٍ لا يَأخُذ <c>IDocumentStore</c>.</b> يُفحَص
/// <b>التَوقيع لا النِيَّة</b> — وهذا نَصّ القاعِدَة ٦ («الحِراسَة في
/// التَوقيع لا في الجِسم») مَنقولاً إلى مِلكِيَّة الجَلسَة، ونَصّ
/// القاعِدَة ٧ حَرفاً: «قابِل لِلفَحص بِتَوقيع الدالَّة: مَن يَقبَل
/// <c>IDocumentSession</c> ومَن لا يَقبَلُها لا يَجتَمِعان».</para>
///
/// <para><b>لِماذا التَوقيع هُوَ المِقياس، وهذا مَقيس مِن الحُزمَة لا
/// مَذوق</b>: التَوثيق المَشحون يَقول إنّ الوَسيط المُعامَلاتيّ يُركَّب
/// حَولَ <b>تَبَعِيَّةِ خِدمَةٍ مَعروفَة</b> — «Helps connect
/// transactional middleware». فَنُقطَةٌ تَأخُذ <c>IDocumentStore</c>
/// وتَفتَح جَلسَتَها بِيَدِها <b>لا يُركَّب حَولَها شَيء</b>: لا
/// مُعامَلَة، ولا صُندوق صادِر، ولا حَصر مُستَأجِر مِن الظَرف. وهو
/// بِالضَبط ما أَثبَتَه <c>LiveOutboxTenantProofTests</c> مِن
/// الطَرَف المُقابِل: الجَلسَة <b>المَحقونَة</b> تَحمِل مُستَأجِر
/// المَسار و<c>FlushOutgoingMessagesOnCommit</c>.</para>
///
/// <para><b>والفَحص لا يَدَّعي أَكثَر مِمّا يَفعَل</b>: يَقرَأ قائِمَة
/// وُسَطاء اللامدا — ما بَينَ <c>Map…(</c> وأَوَّل <c>=&gt;</c> — ولا
/// يَحكُم عَلى ما يَفعَلُه الجِسم. نُقطَةٌ تَأخُذ المَخزَن ولا
/// تَستَعمِلُه تَحمَرّ هُنا، وهذا صَحيح: الوَسيط لا يُركَّب لَها
/// أَيضاً.</para>
///
/// <para><b>وثُنائيّ الاتِّجاه</b>: مَسارٌ يَأخُذ المَخزَن وهُوَ غَير
/// مُثَبَّت ⇒ <b>يَحمَرّ</b>؛ ومَسارٌ مُثَبَّت لَم يَعُد يَأخُذُه (أَو
/// زالَ) ⇒ <b>يَحمَرّ</b>. فَالقائِمَة تَتَقَلَّص مَسارًا مَع كُلّ
/// مَوجَة تَرحيل، ولا تَبقى تَصِف ماضِياً انقَضى.</para>
/// </summary>
public class EndpointStoreInjectionTests
{
    /// <summary>
    /// <para><b>السِجِلّ</b> — كُلّ نُقطَة تَأخُذ <c>IDocumentStore</c>
    /// اليَوم. والسَبَب واحِدٌ لِلكُلّ ولِذلك يُقال مَرَّةً بَدَل أَن
    /// يُكَرَّر سِتّينَ مَرَّة: <b>هذِه هي الحالَة المَوروثَة</b> —
    /// ‏83٪ مِن دَين مِلَفّ النِقاط جاءَ في كوميت استيراد واحِد
    /// (<c>23067e3e</c>)، لا مِن قَرارٍ اتُّخِذَ هُنا. والقائِمَة
    /// وُجِدَت لِتَتَقَلَّص، لا لِتُبَرَّر.</para>
    ///
    /// <para><b>ونُمُوُّها قَرارٌ مَرئيّ في مُراجَعَة</b>: كُلّ إضافَة
    /// هُنا سَطرٌ يُكتَب بِيَدٍ في نَفس الكوميت الَّذي يَخرِق
    /// القاعِدَة — وهذا هُوَ الفَرق بَينَ دَينٍ يُتَّخَذ ودَينٍ
    /// يَنزَلِق.</para>
    /// </summary>
    private static readonly string[] PinnedStoreTakers = PinnedRoutes.StoreTakers;

    [Fact]
    public void No_new_endpoint_takes_the_document_store()
    {
        var endpoints = WriteEndpointGuardTests.AllMinimalApiEndpoints().ToList();

        // عَدّاد: أَداةٌ تَفحَص صِفراً أَداةٌ عَمياء (القاعِدَة ١٠).
        Assert.True(endpoints.Count >= 99,
            $"أَداة عَمياء: وُجِدَت {endpoints.Count} نُقطَة — والمَقيس ‏99 فَأَكثَر.");

        var takers = StoreTakers(endpoints).ToArray();

        // وعَدّادٌ ثانٍ: لَو أَخفَقَ مُستَخرِج الوُسَطاء وأَعطى صِفراً
        // لَبَدا الدَين مَسدوداً وهُوَ قائِم. «صِفر مُخالَفَة» مِن
        // مُستَخرِجٍ مَكسور لا يُمَيَّز عَن صِفرٍ حَقيقيّ.
        Assert.True(takers.Length >= 40,
            $"أَداة عَمياء: مُستَخرِج الوُسَطاء وَجَدَ {takers.Length} نُقطَة تَأخُذ المَخزَن — " +
            "والمَقيس عَشَرات. اُنظُر StripComments/StatementFrom قَبل أَن تُصَدِّق الصِفر.");

        // سَطر القِياس — يُطبَع دائِماً لا عِندَ الفَشَل، فَالرَقَم الَّذي
        // لا أَمرَ يُنتِجُه يَتَعَفَّن في الوَثائِق (وقَد تَعَفَّنَ: §٣ في
        // `docs/ARCHITECTURE-ENFORCEMENT.md` بَقِيَ يَقول «‏86 مِن ‏99»
        // بَعدَ أَن صارَت النِقاط ‏104).
        Console.WriteLine(
            $"· قِياس حَقن المَخزَن: {takers.Length} نُقطَة تَأخُذ IDocumentStore " +
            $"مِن {endpoints.Count} نُقطَة.");

        var unpinned = takers.Except(PinnedStoreTakers, StringComparer.Ordinal).ToArray();
        Assert.True(unpinned.Length == 0,
            "نُقطَة تَأخُذ IDocumentStore وغَير مُثَبَّتَة — تَفتَح جَلسَتَها بِيَدِها " +
            "فَتَخرُج مِن المُعامَلَة ومِن الصُندوق الصادِر:\n  " +
            string.Join("\n  ", unpinned) +
            "\nالمَسار الصَحيح: رَحِّل النُقطَةَ إلى Wolverine.Http — فَجَلسَتُها المَحقونَةُ " +
            "يُركَّبُ حَولَها الوَسيطُ المُعامَلاتيّ **وتَحمِلُ مُستَأجِرَ المَسار** " +
            "(‏مُثبَت في LiveOutboxTenantProofTests). " +
            "\n**وحَقنُ IDocumentSession في Minimal API لَيسَ بَديلاً**: كَشفُ المُستَأجِرِ " +
            "‏`opts.TenantId.IsRouteArgumentNamed(\"slug\")` يَحكُمُ سَلاسِلَ Wolverine وَحدَها، " +
            "فَجَلسَةُ Minimal API تَحمِلُ `*DEFAULT*` — ووَثيقَةٌ مُتَعَدِّدَةُ الإيجارِ تُقرَأُ " +
            "بِها `null` وتُكتَبُ في اللامَكان، صامِتَةً. وذاكَ ما قاسَه فَحصُ " +
            "‏`No_minimal_api_endpoint_reads_a_tenanted_document_from_an_injected_session` أَدناه، " +
            "بَعدَ أَن أَوقَعَت هذِه الرِسالَةُ نَفسُها شاشَةَ حَذفِ حِسابٍ لا تَحذِف.");
    }

    /// <summary><b>والنِصف الآخَر</b>: مَسارٌ سُدِّدَ دَينُه ولَم يُرفَع
    /// مِن السِجِلّ يَحمَرّ — وإلّا صارَ السِجِلّ يَصِف ماضِياً
    /// انقَضى، فَلا يَتَقَلَّص أَبَداً ولا يُلاحَظ.</summary>
    [Fact]
    public void No_pinned_store_taker_outlives_its_debt()
    {
        var takers = StoreTakers(WriteEndpointGuardTests.AllMinimalApiEndpoints())
            .ToHashSet(StringComparer.Ordinal);

        var settled = PinnedStoreTakers.Except(takers, StringComparer.Ordinal).ToArray();

        Assert.True(settled.Length == 0,
            "إدخالَة مُثَبَّتَة لَم تَعُد تَأخُذ IDocumentStore (‏رُحِّلَت أَو زالَت) " +
            "ولَم تُرفَع:\n  " + string.Join("\n  ", settled) +
            "\nاِرفَعها — السِجِلّ يَصِف الواقِع أَو يَرِثّ.");
    }

    /// <summary>ولا تَكرار في السِجِلّ — إدخالَتانِ لِمَسارٍ واحِد
    /// تُخفيانِ رَفعَ إحداهُما.</summary>
    [Fact]
    public void The_ledger_has_no_duplicate_entries()
        => Assert.Equal(
            PinnedStoreTakers.Distinct(StringComparer.Ordinal).Count(),
            PinnedStoreTakers.Length);

    // ─── الأَدَوات ────────────────────────────────────────────────────

    /// <summary>قائِمَة وُسَطاء اللامدا: ما بَينَ <c>Map…(</c> وأَوَّل
    /// <c>=&gt;</c> في العِبارَة. ونُقطَةٌ بِلا لامدا (‏مَرجِع مِنهَج)
    /// لا وُسَطاءَ نَصِّيَّة لَها، فَلا تُعَدّ.</summary>
    private static IEnumerable<string> StoreTakers(IEnumerable<WriteEndpointGuardTests.Endpoint> endpoints)
    {
        foreach (var e in endpoints)
        {
            var arrow = e.Body.IndexOf("=>", StringComparison.Ordinal);
            if (arrow < 0) continue;
            var signature = e.Body[..arrow];
            if (StoreParameter.IsMatch(signature)) yield return e.Route;
        }
    }

    /// <summary><c>IDocumentStore</c> كَنَوع وَسيط — بِحَدّ كَلِمَة، كَي
    /// لا يُطابِقَ اسماً يَنتَهي بِه.</summary>
    private static readonly Regex StoreParameter =
        new(@"\bIDocumentStore\b", RegexOptions.Compiled);

    // ═════════════════════════════════════════════════════════════════
    //  ونِصفُ السِجِلِّ الآخَر: مَن يَحقِنُ **الجَلسَةَ** في Minimal API
    // ─────────────────────────────────────────────────────────────────
    //  **الكَلفَةُ الَّتي كَتَبَت هذا الفَحص (‏2026-09-02)**: نُقطَةُ
    //  `/{slug}/me/delete/confirm` كُتِبَت تَحقِنُ `IDocumentSession`
    //  اتِّباعاً لِرِسالَةِ الفَحصِ أَعلاه — وهي رِسالَةٌ كانَت تَقول
    //  «احقِن الجَلسَةَ فَهي تَحمِلُ مُستَأجِرَ المَسار». والقِياسُ
    //  الحَيّ: ‏302 إلى `me/delete?err=user_not_found` لِمُستَخدِمٍ
    //  قائِمٍ بِكوكي صَحيحَة، و`deletedAt=null` في القاعِدَةِ بَعدَ
    //  النَقر — أَي **شاشَةُ حَذفٍ لا تَحذِف**. والعِلَّةُ أَنّ
    //  `opts.TenantId.IsRouteArgumentNamed("slug")` يَحكُمُ سَلاسِلَ
    //  Wolverine وَحدَها (‏`HostingExtensions` يَقولُها نَصّاً)، فَجَلسَةُ
    //  Minimal API تَحمِلُ `*DEFAULT*` — و`User` وَثيقَةٌ مُتَعَدِّدَةُ
    //  الإيجار، فَـ`LoadAsync<User>` تَرُدُّ `null` صامِتَةً.
    //
    //  **ولِماذا هذا الشَكلُ لا سِجِلٌّ ثانٍ يُملَأ بِاليَد**: حَقنُ
    //  الجَلسَةِ لَيسَ خَطَأً بِذاتِه — سَبعُ نِقاطِ الفَوتَرَة تَحقِنُها
    //  وهي صَحيحَة، لِأَنّ كُلَّ وَثيقَةٍ تَلمِسُها **مُعلَنَةٌ
    //  `SingleTenanted`** فَلا مَعنى لِلمُستَأجِرِ فيها. فَالحَدُّ
    //  المَقيسُ لَيسَ «لا تَحقِن» بَل: **جَلسَةٌ مَحقونَةٌ في Minimal API
    //  لا تَلمِسُ وَثيقَةً مُتَعَدِّدَةَ الإيجار**. والمَعجَمُ يُقرَأُ مِن
    //  `HostingExtensions` نَفسِه فَلا نُسخَةَ ثانِيَةً تَنجَرِف.

    [Fact]
    public void No_minimal_api_endpoint_reads_a_tenanted_document_from_an_injected_session()
    {
        var endpoints = WriteEndpointGuardTests.AllMinimalApiEndpoints().ToList();

        Assert.True(endpoints.Count >= 99,
            $"أَداة عَمياء: وُجِدَت {endpoints.Count} نُقطَة — والمَقيس ‏99 فَأَكثَر.");

        var singleTenanted = SingleTenantedDocumentTypes();

        // عَدّادٌ عَلى المَعجَمِ نَفسِه: لَو انكَسَرَ مُستَخرِجُ
        // `SingleTenanted` وأَعطى صِفراً لَاتُّهِمَت كُلُّ نُقطَةٍ
        // بِالخَرق — وأَداةٌ تَتَّهِمُ الكُلَّ كَأَداةٍ لا تَتَّهِمُ أَحَداً.
        Assert.True(singleTenanted.Count >= 7,
            $"أَداة عَمياء: مُستَخرِجُ `SingleTenanted` وَجَدَ {singleTenanted.Count} نَوعاً — " +
            "والمَقيس سَبعَةٌ فَأَكثَر. اُنظُر HostingExtensions قَبلَ أَن تُصَدِّقَ الرَقَم.");

        var sessionTakers = SessionTakers(endpoints).ToList();

        Assert.True(sessionTakers.Count >= 7,
            $"أَداة عَمياء: {sessionTakers.Count} نُقطَة تَحقِنُ IDocumentSession — والمَقيس سَبعٌ فَأَكثَر.");

        var loads = 0;
        var breaches = new List<string>();

        foreach (var e in sessionTakers)
        {
            foreach (Match m in DocumentTypeArgument.Matches(e.Body))
            {
                loads++;
                var type = m.Groups["type"].Value.Split('.').Last().Trim();
                if (!singleTenanted.Contains(type))
                    breaches.Add($"{e.Route}   ← {m.Groups["call"].Value}<{type}>   ({e.File})");
            }
        }

        Assert.True(loads >= 10,
            $"أَداة عَمياء: {loads} نِداءَ وَثيقَةٍ مَفحوص في نِقاطٍ تَحقِنُ الجَلسَة — والمَقيس عَشَرَةٌ فَأَكثَر.");

        Console.WriteLine(
            $"· قِياس حَقن الجَلسَة: {sessionTakers.Count} نُقطَة تَحقِنُ IDocumentSession، " +
            $"‏{loads} نِداءَ وَثيقَةٍ فيها، مُقابَلَةً بِـ{singleTenanted.Count} نَوعٍ SingleTenanted.");

        Assert.True(breaches.Count == 0,
            "جَلسَةٌ مَحقونَةٌ في Minimal API تَلمِسُ وَثيقَةً مُتَعَدِّدَةَ الإيجار — " +
            "وهي تَحمِلُ `*DEFAULT*` لا مُستَأجِرَ المَسار، فَالقِراءَةُ تَرُدُّ `null` " +
            "والكِتابَةُ تَقَعُ في اللامَكان، **صامِتَتَين**:\n  " +
            string.Join("\n  ", breaches) +
            "\nالمَسار الصَحيح: خُذ IDocumentStore وافتَح `store.LightweightSession(slug)`، " +
            "وثَبِّت المَسارَ في PinnedRoutes.StoreTakers بِسَبَبِه في نَفسِ الكوميت — " +
            "أَو رَحِّل النُقطَةَ إلى Wolverine.Http حَيثُ يَقَعُ كَشفُ المُستَأجِر.");
    }

    /// <summary>أَسماءُ الوَثائِقِ المُعلَنَةِ <c>SingleTenanted</c> —
    /// تُقرَأُ مِن <c>HostingExtensions.cs</c> نَفسِه لا مِن نُسخَةٍ
    /// مَكتوبَةٍ هُنا تَنجَرِف.</summary>
    private static IReadOnlySet<string> SingleTenantedDocumentTypes()
    {
        var file = EntitlementContractTests.SourceFiles()
            .FirstOrDefault(f => f.File.Replace('\\', '/')
                .EndsWith("/ACommerce.Platform.Hosting/HostingExtensions.cs", StringComparison.Ordinal));

        Assert.False(string.IsNullOrEmpty(file.Text),
            "لَم يُعثَر عَلى HostingExtensions.cs — ومَعجَمُ `SingleTenanted` يُقرَأُ مِنه.");

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in SingleTenantedDeclaration.Matches(file.Text))
            set.Add(m.Groups["type"].Value.Split('.').Last().Trim());
        return set;
    }

    /// <summary>النِقاطُ الَّتي تَحقِنُ <c>IDocumentSession</c> وَسيطاً.</summary>
    private static IEnumerable<WriteEndpointGuardTests.Endpoint> SessionTakers(
        IEnumerable<WriteEndpointGuardTests.Endpoint> endpoints)
    {
        foreach (var e in endpoints)
        {
            var arrow = e.Body.IndexOf("=>", StringComparison.Ordinal);
            if (arrow < 0) continue;
            if (SessionParameter.IsMatch(e.Body[..arrow])) yield return e;
        }
    }

    private static readonly Regex SessionParameter =
        new(@"\bIDocumentSession\b", RegexOptions.Compiled);

    /// <summary><c>opts.Schema.For&lt;X&gt;()…&#46;SingleTenanted()</c> —
    /// والفَجوَةُ مَحصورَةٌ بِما لا يَحوي <c>Schema.For</c> ثانِيَةً كَي
    /// لا يَقفِزَ الإعلانُ إلى جارِه.</summary>
    private static readonly Regex SingleTenantedDeclaration =
        new(@"Schema\.For<(?<type>[^>]+)>\((?:(?!Schema\.For).)*?\.SingleTenanted\(\)",
            RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>نِداءُ وَثيقَةٍ بِوَسيطٍ نَوعِيّ على الجَلسَة.</summary>
    private static readonly Regex DocumentTypeArgument =
        new(@"\b(?<call>LoadAsync|LoadManyAsync|Query|Delete|DeleteWhere)<(?<type>[^<>]+)>",
            RegexOptions.Compiled);
}
