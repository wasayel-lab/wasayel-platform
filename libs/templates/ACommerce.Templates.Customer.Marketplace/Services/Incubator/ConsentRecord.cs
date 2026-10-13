using System.Linq.Expressions;

namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// سِجِلّ مُوافَقَة المُستَخدِم على شُروط الحاضِنَة (المَرحَلَة ٧).
/// يُحفَظ تَحت tenant "_studio". وُجود سِجِلّ بِنَفس <c>Version</c> الحالي
/// = المُستَخدِم وافَق. تَغيير الـ Version يُجبِر القَبول مَرَّة أُخرى.
/// </summary>
public sealed class ConsentRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int Version { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Ip { get; set; } = "";
    public string UserAgent { get; set; } = "";
}

public static class ConsentPolicy
{
    /// <summary>إصدار الشُروط الحاليّ — بَدِّلها لِتُجبِر القَبول مَرَّة أُخرى.
    ///
    /// <para><b>‏1 → 2 في ‏2026-10-11</b> (موجَزُ الفَجَوات P2-1): البَندُ الرابِعُ كانَ
    /// يَقولُ «الدَّفع مُسَجَّل كَ نِيَّة فَقَط … (لا يَتِم تَحصيل)» بَينَما باقَةُ
    /// المَتجَرِ تُضبَطُ بِسِعرٍ وتُعَلَّقُ عِندَ عَدَمِ السَداد — فَكُلُّ مَن قَبِلَ
    /// الإصدارَ 1 يُعادُ إلَيه النَصُّ الصادِقُ قَبلَ تَحليلِه التالي.</para></summary>
    public const int CurrentVersion = 2;

    /// <summary>
    /// <para><b>شَرطُ «وافَقَ عَلى هذا الإصدار»</b> — مُسنَدٌ واحِدٌ يَقرَؤُه بابُ
    /// صَفحَةِ الشُروطِ (<c>StudioQueries.HasConsentedAsync</c>) ونُقطَةُ استِئنافِ
    /// المُطالَبَةِ قَبلَ أَوَّلِ تَحليل (<c>ResumeStudioPromptAsync</c>)، فَلا نُسخَتانِ
    /// تَنجَرِفان (القاعِدَة ٨؛ كانَتا نُسخَتَينِ مُتَطابِقَتَينِ في مَوضِعَين حَتّى
    /// ‏2026-10-11). والمُساواةُ لا <c>&gt;=</c> مَقصودَة: سِجِلُّ إصدارٍ أَقدَمَ لا
    /// يَكفي، وهذا بِعَينِه ما يُجبِرُ القَبولَ مَرَّةً أُخرى حينَ يُرفَعُ
    /// <see cref="CurrentVersion"/>.</para>
    /// <para>تَعبيرٌ لا دالَّة: يُمَرَّرُ إلى <c>Where</c> فَيُتَرجِمُه Marten إلى SQL،
    /// ويُجَمَّعُ في الاختِبارِ فَيُقاسُ بِلا قاعِدَةِ بَيانات.</para>
    /// <para><b>شَرطُ الاستِخراجِ</b> (القاعِدَة ١ — مُستَهلِكانِ لا ثَلاثَة، ‏2026-10-13): استُخرِجَ لِأَنّ قارِئَي
    /// مُسنَدٍ واحِدٍ يَجِبُ أَن يَتَحَرَّكا مَعاً حينَ يُرفَعُ الإصدار، ولا ثالِثَ مُتَوَقَّع — فَإن بَقِيَ قارِئٌ واحِدٌ
    /// أُعيدَ المُسنَدُ مُضَمَّناً فيه.</para>
    /// </summary>
    public static Expression<Func<ConsentRecord, bool>> AcceptedBy(Guid userId, int version)
        => c => c.UserId == userId && c.Version == version;
}
