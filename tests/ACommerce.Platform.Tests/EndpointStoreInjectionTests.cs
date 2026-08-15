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

        var unpinned = takers.Except(PinnedStoreTakers, StringComparer.Ordinal).ToArray();
        Assert.True(unpinned.Length == 0,
            "نُقطَة تَأخُذ IDocumentStore وغَير مُثَبَّتَة — تَفتَح جَلسَتَها بِيَدِها " +
            "فَتَخرُج مِن المُعامَلَة ومِن الصُندوق الصادِر:\n  " +
            string.Join("\n  ", unpinned) +
            "\nالمَسار الصَحيح: احقِن IDocumentSession بَدَلَه — الوَسيط المُعامَلاتيّ " +
            "يُركَّب حَولَها، وتَحمِل مُستَأجِر المَسار (‏مُثبَت في LiveOutboxTenantProofTests).");
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
}
