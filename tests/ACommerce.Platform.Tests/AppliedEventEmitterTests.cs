using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>لِكُلّ حَدَثٍ لَه <c>Apply</c> مُصدِرٌ واحِد عَلى الأَقَلّ.</b>
/// هذا هو الفَحص الآليّ لِلقاعِدَة ١ — التَجريد لا يَسبِق مُستَهلِكَه —
/// في أَغلى صُوَرِها المَقيسَة: حَدَثٌ بِمَنطِق <c>Apply</c> كامِل
/// <b>وصِفر مُصدِر</b>. مَنطِقٌ يُقرَأ ويُراجَع ويُوَثَّق ولا يُنَفَّذ
/// أَبَداً، ولا شَيءَ يَحمَرّ.</para>
///
/// <para><b>ولِماذا قائِمَة يَتامى مُثَبَّتَة بَدَل صِفر</b>: الفَحص
/// يَحمَرّ اليَوم بِأَربَعَة. حَذفُها كانَ سَيُخفي الدَين، وتَصحيحُها
/// كُلَّها في مَوجَة واحِدَة كانَ سَيَخلِط أَربَعَ مُشكِلات. الثالِثَة —
/// وهي المُختارَة، ونَمَطُها <c>FlowInventoryTests</c> والطَبَقَة
/// السابِعَة: يُوصَف الواقِع كَما هو، ويُثَبَّت كُلّ يَتيم
/// <b>بِاسمِه وسَبَبِه ومَوجَتِه</b>.</para>
///
/// <para><b>وثُنائيّ الاتِّجاه — وهذا نِصفُه الَّذي لا يَرِثّ</b>:</para>
/// <list type="bullet">
///   <item>يَتيمٌ جَديد غَير مُثَبَّت ⇒ <b>يَحمَرّ</b>. فَالدَين لا
///   يَنمو إلّا بِقَرار مَرئيّ في مُراجَعَة.</item>
///   <item>إدخالَةٌ عولِجَت ولَم تُرفَع ⇒ <b>تَحمَرّ</b>. فَالقائِمَة
///   لا تَبقى تَصِف ماضِياً انقَضى — وهذا بِعَينِه ما يَجعَلُها
///   تَتَقَلَّص إدخالَةً مَع كُلّ مَوجَة.</item>
/// </list>
///
/// <para><b>وحُدودُه مُعلَنَة</b>: مَسحُ النَصّ يُعطي إيجابيّاً كاذِباً
/// لَو بُنِيَ الحَدَث ولَم يُلحَق بِتَيار. مَقبول — الفَحص يَستَهدِف
/// <b>الغِياب التامّ</b>، وهو العَطَب الواقِع. وتَحليل IL أَغلى مِن
/// قيمَتِه لِفَريق بِهذا الحَجم.</para>
/// </summary>
public class AppliedEventEmitterTests
{
    /// <summary>سَبَب بَقاء يَتيمٍ يَتيماً — ومَوجَتُه.</summary>
    private sealed record Orphan(string Event, string Wave, string WhyAr);

    /// <summary>
    /// <para><b>قائِمَة اليَتامى المُثَبَّتَة</b> — تُحذَف مِنها إدخالَة
    /// مَع كُلّ مَوجَة. والقَرار مُعلَن ولا يُوَسَّع:</para>
    /// </summary>
    private static readonly Orphan[] PinnedOrphans =
    {
        new("SubscriptionExpired", "٢",
            "‏EndsAt مَكتوب سَلَفاً — الباعِث فَحصُ تاريخ عِندَ القِراءَة بِلا جَدوَلَة. " +
            "بِدونِه يُنشَأ الاشتِراك فَعّالاً ولا مَخرَج لَه أَبَداً."),

        new("SubscriptionCancelled", "٢",
            "يَلزَمُه سَطح إلغاء (زِرّ). مُدرَج لِأَنّ مِن دونِه تَبقى الحالَة مَيِّتَة — " +
            "والقاعِدَة ١٢: شاشَة بِلا مَدخَل غَير مَوجودَة."),

        new("QuotaRefunded", "٣",
            "لا مُستَهلِك: لا مَسار حَذف إعلان يُرجِع حِصَّة اليَوم. " +
            "يُؤَجَّل بِقَرار مُعلَن ولا يُبنى «احتِياطاً» — وذاكَ بِعَينِه ما صَنَعَ يُتمَه."),

        new("SubscriptionRenewed", "٤",
            "يَلزَمُه تَكامُل دَفع حَقيقيّ وهو غَير مَوجود " +
            "(‏«لا تكامل دفع حقيقي — الباقات نية فقط»)."),

        // ─── ولَيسَ مِن عائِلَة الاشتِراك ──────────────────────────────
        // وَجَدَه هذا الفَحص، ولَم يَكُن في حِساب المَوجَة: تَصميم
        // الطَبَقَة عَدَّ «خَمسَة يَتامى» وقَصَدَ أَحداثَ الاشتِراك
        // وَحدَها. والانعِكاس لا يَعرِف عائِلات — فَأَخرَجَ سادِساً.
        new("OfferExpired", "—",
            "الانتِهاء يُعرَض بِمُقارَنَة ExpiresAt > now في الواجِهَة، ولا حَدَث يَكتُبُه. " +
            "مُثَبَّت سَلَفاً في FlowInventoryTests بِرَمز state_unreachable — " +
            "وهذا الفَحص يَراه مِن جِهَة الحَدَث لا مِن جِهَة الحالَة. لا مَوجَة لَه بَعد."),

        // ─── ورُفِعَت إدخالَة: ListingEdited (المَوجَة ٤) ────────────────
        // وُلِدَت يَتيمَةً بِإغلاق الثَغرَة، وعاشَت دَورَةً واحِدَة.
        // باعِثُها اليَوم ListingEditService مِن شاشَةِ تَحرير مَحروسَة
        // (POST /{slug}/listings/{id}/edit، حارِسانِ في التَوقيع:
        // تَوثيقٌ ثُمَّ مِلكِيَّة)، لا مِن نُقطَةٍ مَكشوفَة تُعاد. وهذا
        // هُوَ النِصف الَّذي يَجعَل القائِمَة تَتَقَلَّص بَدَل أَن
        // تَصيرَ قائِمَةَ إسكات: تُرفَع الإدخالَة في نَفس الكوميت
        // الَّذي يُعطيها مُصدِراً — ولَو تُرِكَت لَاحمَرَّ الفَحص
        // بِالاتِّجاه المَقلوب، وقَد احمَرَّ فِعلاً قَبل رَفعِها.
        //
        // ولِـ ListingDeleted قِصَّةٌ أُخرى تُقال ولا تُخلَط: لَم يَكُن
        // يَتيماً أَصلاً — باعِثُه إشرافُ الاستوديو
        // (POST /studio/apps/{slug}/listings/{id}/moderate، مَحروسٌ
        // بِـ StudioOwnsAsync). فَما سَدَّته المَوجَة ٤ فيه لَيسَ
        // يُتماً بَل غِيابَ سَطحِ المالِك.
    };

    // ─── الفَحص ───────────────────────────────────────────────────────

    [Fact]
    public void Every_applied_event_has_at_least_one_emitter()
    {
        var applied = AppliedEventTypes();

        // عَدّاد: أَداةٌ تَفحَص صِفراً أَداةٌ عَمياء (القاعِدَة ١٠).
        // «صِفر مُخالَفَة» بِلا عَدّاد لا يُميَّز عَن فَحصٍ لَم يَقرَأ.
        Assert.True(applied.Count >= 20,
            $"أَداة عَمياء: وُجِدَ {applied.Count} حَدَثاً لَه Apply — والمَقيس ٢١.");

        var sources = SourceText();
        var actualOrphans = applied
            .Where(e => !HasEmitter(e, sources))
            .OrderBy(e => e, StringComparer.Ordinal)
            .ToArray();

        var pinned = PinnedOrphans.Select(o => o.Event)
            .OrderBy(e => e, StringComparer.Ordinal).ToArray();

        // ١. يَتيم جَديد غَير مُثَبَّت ⇒ يَحمَرّ.
        var unpinned = actualOrphans.Except(pinned, StringComparer.Ordinal).ToArray();
        Assert.True(unpinned.Length == 0,
            "حَدَثٌ لَه Apply وبِلا مُصدِر، وغَير مُثَبَّت في القائِمَة:\n  " +
            string.Join("\n  ", unpinned) +
            "\nإمّا أَن يُعطى مُصدِراً، أَو يُثَبَّت هُنا بِسَبَبِه ومَوجَتِه في نَفس الكوميت.");

        // ٢. إدخالَة عولِجَت ولَم تُرفَع ⇒ تَحمَرّ.
        var stale = pinned.Except(actualOrphans, StringComparer.Ordinal).ToArray();
        Assert.True(stale.Length == 0,
            "إدخالَة مُثَبَّتَة صارَ لَها مُصدِر ولَم تُرفَع مِن القائِمَة:\n  " +
            string.Join("\n  ", stale) +
            "\nاِرفَعها — القائِمَة تَصِف الواقِع أَو تَرِثّ.");
    }

    /// <summary>
    /// <para><b>العَدَد المُثَبَّت</b> — خَمسَة اليَوم، وهذا الرَقَم هو ما
    /// يَنقُص مَوجَةً بَعدَ مَوجَة.</para>
    ///
    /// <para><b>وحِسابُه لَيسَ ما تَوَقَّعَه التَصميم</b>: عَدَّ خَمسَةً
    /// وقَصَدَ أَحداثَ الاشتِراك الخَمسَة. الواقِع المَقيس: أَربَعَة
    /// مِنها بَقِيَت (<c>QuotaConsumed</c> نالَ مُصدِرَه في تِلكَ
    /// المَوجَة)، <b>وانضَمَّ إلَيها <c>OfferExpired</c></b> مِن عُدَّة
    /// أُخرى.</para>
    ///
    /// <para><b>ونَما سادِسٌ ثُمَّ سُدِّدَ — والدَورَةُ كامِلَةً هي
    /// البُرهان</b>: <c>ListingEdited</c> فَقَدَ باعِثَه حينَ حُذِفَت
    /// نُقطَةُ التَحرير المَكشوفَة، فَنَمَت القائِمَة <b>لِأَنّ الحَذفَ
    /// كَشَفَ فَجوَةَ مُنتَجٍ كانَت الثَغرَةُ تَستُرُها</b>؛ ثُمَّ
    /// سَدَّت المَوجَةُ ٤ الفَجوَةَ بِشاشَةٍ مَحروسَة فَرَجَعَ العَدَدُ
    /// إلى خَمسَة. نَمَت بِقَرارٍ مَرئيّ وتَقَلَّصَت بِقَرارٍ مَرئيّ،
    /// ولَم تَكُن في أَيٍّ مِن الطَرَفَين قائِمَةَ إسكات.</para>
    /// </summary>
    [Fact]
    public void Exactly_five_orphans_remain_and_the_settled_ones_have_real_emitters()
    {
        Assert.Equal(5, PinnedOrphans.Length);
        Assert.Equal(4, PinnedOrphans.Count(o => o.Event.StartsWith("Subscription", StringComparison.Ordinal)
                                              || o.Event.StartsWith("Quota", StringComparison.Ordinal)));
        Assert.DoesNotContain("QuotaConsumed", PinnedOrphans.Select(o => o.Event));

        // والباعِث الَّذي أُضيفَ فِعلاً — لا دَعوى في تَعليق.
        Assert.DoesNotContain("ListingEdited", PinnedOrphans.Select(o => o.Event));

        var src = SourceText();
        Assert.True(HasEmitter("ListingEdited", src),
            "ListingEdited بِلا مُصدِر — شاشَةُ التَحرير الَّتي تَدَّعيها المَوجَة ٤ غَير مَوجودَة.");

        // و ListingDeleted لَم يَكُن في القائِمَة قَطّ: باعِثُه إشرافُ
        // الاستوديو، وأَضافَت المَوجَة ٤ سَطحَ المالِك إلَيه. يُفحَص هُنا
        // كَي لا يُظَنّ أَنَّه سُدِّدَ في هذِه المَوجَة.
        Assert.True(HasEmitter("ListingDeleted", src),
            "ListingDeleted بِلا مُصدِر — وقَد كانَ لَه باعِثٌ مَحروسٌ قَبل المَوجَة ٤.");

        Assert.True(HasEmitter("QuotaConsumed", src),
            "‏QuotaConsumed بِلا مُصدِر — الباعِث الَّذي تَدَّعيه هذه المَوجَة غَير مَوجود.");
    }

    /// <summary>كُلّ يَتيم مُثَبَّت يُعلِن سَبَبَه ومَوجَتَه — فَالقائِمَة
    /// دَينٌ مَوصوف لا قائِمَةُ إسكات.</summary>
    [Fact]
    public void Every_pinned_orphan_declares_a_wave_and_a_reason()
    {
        foreach (var o in PinnedOrphans)
        {
            Assert.False(string.IsNullOrWhiteSpace(o.Wave),  $"«{o.Event}» بِلا مَوجَة.");
            Assert.False(string.IsNullOrWhiteSpace(o.WhyAr), $"«{o.Event}» بِلا سَبَب.");
            Assert.True(o.WhyAr.Length > 40,
                $"سَبَب «{o.Event}» أَقصَر مِن أَن يَكون سَبَباً.");
        }

        // ولا تَكرار — إدخالَتان لِحَدَثٍ واحِد تُخفيانِ رَفعَ إحداهُما.
        Assert.Equal(
            PinnedOrphans.Select(o => o.Event).Distinct(StringComparer.Ordinal).Count(),
            PinnedOrphans.Length);
    }

    /// <summary>
    /// <para><b>دَينٌ كانَ مُوَثَّقاً وقَد سُدِّد</b>: كانَ لِـ
    /// <c>SubscriptionCreated</c> <b>باعِثانِ مُتَوازِيان</b> — مُعالِج
    /// Wolverine (<c>SubscriptionHandlers</c>) ونُقطَة Minimal-API في
    /// القالِب، والمُستَهلِك الحَيّ هو الثاني. وقَد وُصِفَ يَومَها بِأَنّ
    /// تَوحيدَهُما «تَغييرُ سُلوك» يُؤَجَّل لِمَوجَتِه.</para>
    ///
    /// <para><b>وسَدَّدَته مَوجَةُ الثَغرَة بِلا أَن تَقصِدَه</b>: الباعِث
    /// الأَوَّل كانَ <c>POST /{slug}/api/subscriptions/start</c> — نُقطَةً
    /// <b>بِلا حارِس</b> تَمنَح حِصَّةَ إعلاناتٍ لِأَيّ مُعَرِّف يُكتَب في
    /// جِسم الطَلَب. فَحَذفُها أَزالَ الازدِواج وأَغلَقَ التِفافاً على
    /// الاستِحقاق مَعاً. <b>والباقي واحِد: مَسار القالِب المَحروس.</b></para>
    ///
    /// <para><b>والاختِبار يَبقى مُثَبِّتاً بِرَقَمِه</b> — لكِن بِالاتِّجاه
    /// المَقلوب: باعِثٌ ثانٍ يَعود يَحمَرّ.</para>
    ///
    /// <para><b>والباعِثُ الواحِد انتَقَل — والاختِبارُ يَتبَعُه بِقَرارٍ
    /// مَرئيّ</b>: كانَ في جِسم <c>MarketplaceTemplateExtensions.cs</c>،
    /// ثُمَّ في <c>Services/Subscriptions/SubscriptionRequestService.cs</c>،
    /// وصارَ في <c>Services/Subscriptions/PlanSubscribeService.cs</c> يَومَ
    /// ‏2026-08-23. والسَبَبُ أَنّ دَورَةَ «طَلَبِ اشتِراكٍ مُعَلَّقٍ ←
    /// اعتِماد» حُذِفَت كامِلَةً: قَرارُ المالِك «لا تَسمَح لِلتاجِر
    /// بِاستِلام حَوالات»، فَلَم يَبقَ مَسارٌ مَدفوعٌ يَلتَقي بِالمَجّانيّ
    /// أَصلاً. <b>والعَدَدُ لَم يَتَغَيَّر: واحِدٌ كَما كان.</b></para>
    /// </summary>
    [Fact]
    public void SubscriptionCreated_now_has_exactly_one_emitter_in_the_template()
    {
        var sites = EmitterSites("SubscriptionCreated").ToArray();
        var only = Assert.Single(sites);
        Assert.EndsWith("Subscriptions/PlanSubscribeService.cs", only, StringComparison.Ordinal);
    }

    // ─── الأَدَوات ────────────────────────────────────────────────────

    /// <summary>كُلّ نَوع حَدَث لَه <c>Apply</c> — بِالانعِكاس عَلى
    /// تَجميعات <c>ACommerce</c> المُحَمَّلَة بِجِوار الاختِبار.</summary>
    private static IReadOnlyCollection<string> AppliedEventTypes()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dll in Directory.EnumerateFiles(
                     AppContext.BaseDirectory, "ACommerce*.dll"))
        {
            Type[] types;
            try { types = Assembly.LoadFrom(dll).GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.OfType<Type>().ToArray(); }
            catch { continue; }

            foreach (var t in types)
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name != "Apply") continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 1) continue;
                    names.Add(ps[0].ParameterType.Name);
                }
        }

        return names;
    }

    /// <summary>هَل يُبنى هذا الحَدَث في مَوضِع غَير مِلَفّ تَعريفِه؟
    /// بِناؤُه هو إصدارُه — لا يُبنى إلّا لِيُلحَق.</summary>
    private static bool HasEmitter(string eventName, IReadOnlyList<(string File, string Text)> sources)
        => EmitterSites(eventName, sources).Any();

    private static IEnumerable<string> EmitterSites(string eventName) =>
        EmitterSites(eventName, SourceText());

    private static IEnumerable<string> EmitterSites(
        string eventName, IReadOnlyList<(string File, string Text)> sources)
    {
        var ctor = new Regex($@"\bnew\s+(?:[A-Za-z0-9_.]+\.)?{Regex.Escape(eventName)}\s*[\(\{{]",
            RegexOptions.Compiled);

        foreach (var (file, text) in sources)
        {
            // مِلَفّ التَعريف لَيسَ مَوضِع إصدار.
            if (DefinesType(text, eventName)) continue;
            if (ctor.IsMatch(StripComments(text))) yield return file.Replace('\\', '/');
        }
    }

    private static bool DefinesType(string text, string name) =>
        Regex.IsMatch(text, $@"\b(?:record|class|struct)\s+{Regex.Escape(name)}\b");

    private static string StripComments(string s)
    {
        s = Regex.Replace(s, @"/\*.*?\*/", "", RegexOptions.Singleline);
        s = Regex.Replace(s, @"^[ \t]*///.*$", "", RegexOptions.Multiline);
        s = Regex.Replace(s, @"^[ \t]*//.*$",  "", RegexOptions.Multiline);
        return s;
    }

    /// <summary>مَصادِر <c>libs</c> و<c>apps</c> — <b>بِلا مِلَفّات
    /// الاختِبار</b>: حَدَثٌ يُبنى في اختِبارٍ وَحدَه لَيسَ لَه مُصدِر
    /// في المُنتَج، وعَدُّه مُصدِراً يُخفي بِالضَبط ما وُجِدَ هذا الفَحص
    /// لِيُمسِكَه.</summary>
    private static IReadOnlyList<(string File, string Text)> SourceText() =>
        EntitlementContractTests.SourceFiles().ToList();
}
