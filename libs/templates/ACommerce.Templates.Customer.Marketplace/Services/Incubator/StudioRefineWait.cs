namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// <para><b>أَما زالَ تَحسينُ القِسمِ جارِياً؟ — دالَّةٌ نَقِيَّةٌ تَسأَلُها صَفحَةُ الدِراسَة</b> (‏2026-10-24، تَيّارُ S16؛ مُراجَعَةُ تَيّارِ S15 الجَولَة 2؛
/// تَوصيفُها <c>StudioRefineWaitTests</c>، <c>3375ff1</c>).</para>
///
/// <para><b>العِلَّةُ المَقروءَة</b>: نُقطَةُ <c>/studio/s/{id}/refine</c> تَرُدُّ إلى <c>?refining={section}</c>، والصَفحَةُ تُحَدِّثُ نَفسَها كُلَّ ٤ ثَوانٍ
/// وتَرسُمُ شَريطَ «جارٍ التَحسين» ما دامَ المُعامِلُ في الرابِطِ والدِراسَةُ مُكتَمِلَة — وتَحسينٌ اكتَمَلَ يُبقيهِما، فَالاستِطلاعُ إلى الأَبَد.</para>
///
/// <para><b>والجَوابُ مِن مُعطَياتٍ ثَلاث لا حالَةٍ جَديدَة</b>: لَحظَةُ الطَلَبِ (<c>since</c>، تَكتُبُها النُقطَةُ في الرابِطِ قَبلَ الإطلاق)، و<c>UpdatedAt</c>
/// الدِراسَةِ الَّتي يَرفَعُها حِفظُ التَحسين، والساعَة. جارٍ ⇔ لَم تُكتَب الدِراسَةُ بَعدَ الطَلَب، ولَم تَنقَضِ نافِذَتُه — وهي مُهلَةُ نِداءِ الخَلفِيَّةِ
/// نَفسُها (<see cref="AgentCallBound.Background"/>): بَعدَها لا يَكتُبُ التَحسينُ شَيئاً (يَفشَلُ النِداءُ ويُبتلَعُ في الخَلفِيَّة). ورابِطٌ بِلا
/// <c>since</c> (قَديمٌ أَو مَصنوع) أَو بِلَحظَةٍ في المُستَقبَلِ لا يُحَدِّثُ شَيئاً. <b>ولا يَقرَأُ حالَةً مُخَزَّنَة</b> (القاعِدَة ٧): الدِراسَةُ تُعطاه.</para>
///
/// <para><b>وما لا يُغلِقُه</b>: حِفظٌ يَقَعُ بَعدَ آخِرِ النافِذَةِ بِأَجزاءِ ثانِيَةٍ (نِداءٌ أَجابَ عِندَ آخِرِ مُهلَتِه) يُرى بِإعادَةِ فَتحِ الصَفحَة؛
/// وكِتابَةٌ أُخرى لِلدِراسَةِ في الأَثناءِ (ملاحَظَةٌ، أَو تَحسينٌ آخَر) تُنهي الانتِظارَ قَبلَ أَن يُكتَبَ هذا.</para>
/// </summary>
public static class StudioRefineWait
{
    /// <summary>نافِذَةُ الانتِظار — مُهلَةُ نِداءِ الخَلفِيَّةِ بِعَينِها، لا رَقَمٌ ثانٍ يَنجَرِف.</summary>
    public static TimeSpan Window => AgentCallBound.Background;

    /// <summary>أَجارٍ؟ — <paramref name="refining"/> مِن الرابِط، و<paramref name="sinceTicks"/> لَحظَةُ الطَلَبِ بِـ<see cref="DateTime.Ticks"/> (UTC)،
    /// و<paramref name="studyUpdatedAtUtc"/> آخِرُ كِتابَةٍ لِلدِراسَة.</summary>
    public static bool Pending(string? refining, long? sinceTicks, DateTime studyUpdatedAtUtc, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(refining) || sinceTicks is not { } since) return false;
        var age = nowUtc.Ticks - since;
        if (age < 0 || age >= Window.Ticks) return false;
        return studyUpdatedAtUtc.Ticks <= since;
    }
}
