namespace ACommerce.Kit.Favorites.Server;

/// <summary>
/// <para><b>الصِنف باقٍ فارِغاً عَمداً</b> — <c>Program.cs</c> يُحيل إلَيه
/// لِمَسح الـ assembly.</para>
///
/// <para><b>ما كانَ هُنا وَلِماذا زال:</b> نُقطَة
/// <c>GET /{slug}/api/favorites?userId=…</c> — <b>بِلا حارِس</b>، تُعيد
/// مُفَضَّلات أَيّ مُستَخدِم لِمَجهول يَعرِف مُعَرِّفَه. صِفر مُستَهلِك
/// مَقيس، وصَفحَة المُفَضَّلَة الحَيَّة تَقرَأ مِن Marten مُباشَرَةً
/// داخِل جَلسَة مُوَثَّقَة.</para>
/// </summary>
public static class FavoriteHandlers
{
}
