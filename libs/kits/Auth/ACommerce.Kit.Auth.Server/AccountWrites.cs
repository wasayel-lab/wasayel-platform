using Marten;

namespace ACommerce.Kit.Auth.Server;

/// <summary>
/// <para><b>كِتابَةُ وَثيقَةِ حِسابٍ بِتَزامُنٍ تَفاؤُلِيّ — تُعادُ مَرَّةً مِن قِراءَةٍ جَديدَة</b> (‏2026-10-29، تَدقيقُ الموجَة ١١ G-1؛ تَوصيفُه
/// <c>9f887fa</c>). <c>User</c> (و<c>StudioUser</c>) وَثيقَةٌ واحِدَةٌ يَكتُبُها أَبوابٌ كَثيرَةٌ كامِلَةً، وكانَ الأَخيرُ يَربَح: اشتِراكُ إشعارٍ قَرَأَ
/// الحِسابَ قَبلَ «اخرج من كل الأجهزة» وحَفِظَ بَعدَه يُعيدُ الإصدارَ القَديمَ فَيَصلُحُ رَمزُ المُهاجِمِ ثانِيَة، ونُسخَةٌ قُرِئَت قَبلَ الحَذفِ تُعيدُ الحِسابَ
/// المَمحُوّ. والآن الوَثيقَتانِ مُسَجَّلَتانِ بِـ<c>UseOptimisticConcurrency</c>، فَحِفظُ نُسخَةٍ كَتَبَها غَيرُ هذا الطَلَبِ بَعدَ قِراءَتِه يَرمي
/// <c>JasperFx.ConcurrencyException</c>.</para>
///
/// <para><b>وهذِه الدالَّةُ نَسَقُ <c>DealsService.RecordAsync</c> لِلحِساب</b>: جَلسَةٌ جَديدَةٌ تَقرَأُ الوَثيقَةَ وتُطَبِّقُ <b>تَغييرَ هذا الطَلَبِ وَحدَه</b>
/// (<paramref name="apply"/>) وتَحفَظ؛ فَإن سَبَقَها كاتِبٌ آخَرُ قَرَأَت ما كَتَبَه وطَبَّقَت التَغييرَ نَفسَه عَلَيه مَرَّةً واحِدَة. والمَرَّةُ الثانِيَةُ
/// إن رُدَّت تَرمي — يَفشَلُ الطَلَبُ مُغلَقاً ولا يَكتُبُ فَوقَ ما لَم يَقرَأ. <paramref name="apply"/> يُعيدُ <c>false</c> إن لَم يَتَغَيَّر شَيءٌ فَلا يُحفَظ.</para>
///
/// <para><b>شَرطُ الاستِخراجِ (القاعِدَة ١) مُتَحَقِّقٌ يَومَ الوِلادَة</b>: كُتّابُ الحِسابِ في وَقتِ التَشغيلِ أَكثَرُ مِن ثَلاثَة — الرَفعانِ وبابُ الاشتِراكِ
/// و«حِسابي» بِأَبوابِه الأَربَعَةِ والمُرسِلُ وكُتّابُ الدُخولِ ورَبطُ الوَسيلَة؛ وكانَ كُلٌّ مِنها يَكتُبُ التَحميلَ والحِفظَ بِيَدِه.</para>
/// </summary>
public static class AccountWrites
{
    /// <summary>مُحاوَلَتانِ: الأُولى، وإعادَةٌ واحِدَةٌ مِن قِراءَةٍ جَديدَة — كَـ<c>DealsService</c>.</summary>
    public const int Attempts = 2;

    /// <summary>يُحَمِّلُ <typeparamref name="T"/> بِمُعَرِّفِه في جَلسَةِ <paramref name="tenant"/>، ويُطَبِّقُ <paramref name="apply"/>، ويَحفَظ — ويُعيدُ
    /// مَرَّةً عَلى <c>ConcurrencyException</c>. <c>null</c> = لا وَثيقَة؛ وإلّا الوَثيقَةُ كَما حُفِظَت (أَو كَما قُرِئَت إن لَم يَتَغَيَّر شَيء).</summary>
    /// <remarks>‏(‏2026-10-30، مُراجَعَةُ الموجَة ١١ لِلمَسار F1 — I-SEC-2 مَعَ G-1؛ تَوصيفُه <c>6e17ba9</c>) <b><paramref name="admit"/>: شَرطُ الكاتِبِ عَلى
    /// الوَثيقَةِ الجَديدَة</b> — يُسأَلُ بَعدَ كُلِّ قِراءَةٍ وقَبلَ <paramref name="apply"/>، فَإن رَدَّها عادَ <c>null</c> بِلا كِتابَة. كانَ الكاتِبُ بِاسمِ جَلسَةٍ
    /// يَفحَصُها مَرَّةً أَوَّلَ الطَلَب، فَطَلَبٌ عَبَرَ قَبلَ «اخرج من كل الأجهزة» وقَرَأَ بَعدَه — أَو أُعيدَ بَعدَ تَعارُضٍ مَعَه — يَكتُبُ عَلى حِسابٍ أُسقِطَت
    /// جَلسَتُه. يُعلَنُ في نِداءِ الكاتِبِ (لا سَطراً في <paramref name="apply"/>)، والعُدَّةُ لا تَعرِفُ إصدارَ الجَلسَة — يُعطيها القالِبُ قَرارَه.</remarks>
    public static async Task<T?> UpdateAsync<T>(IDocumentStore store, string tenant, Guid id, Func<T, bool> apply,
        CancellationToken ct = default, Func<T, bool>? admit = null) where T : class
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var s = store.LightweightSession(tenant);
            var doc = await s.LoadAsync<T>(id, ct);
            if (doc is null || (admit is not null && !admit(doc))) return null;
            if (!apply(doc)) return doc;
            s.Store(doc);
            try
            {
                await s.SaveChangesAsync(ct);
                return doc;
            }
            catch (JasperFx.ConcurrencyException) when (attempt < Attempts) { }
        }
    }
}
