using ACommerce.Platform.Shared;

namespace ACommerce.Kit.Listings.Server;

/// <summary>
/// <para><b>الصِنف باقٍ فارِغاً عَمداً</b> — <c>Program.cs</c> يُحيل إلَيه
/// (<c>AddKitAssembly(typeof(ListingHandlers).Assembly)</c>) لِيَمسَح
/// الـ assembly، فَحَذفُه يَكسِر التَركيب بِلا فائِدَة.</para>
///
/// <para><b>ما كانَ هُنا وَلِماذا زال:</b> خَمس نِقاط HTTP —
/// <c>POST /{slug}/api/listings</c> و<c>…/{id}/edit</c> و<c>…/{id}/delete</c>
/// و<c>GET …/{id}</c> و<c>GET …</c> — <b>بِلا حارِس واحِد</b>. القياس الحَيّ
/// (‏2026-08-15): طَلَب <c>curl</c> مَجهول بِلا cookie ولا رَأس تَخويل رَدَّ
/// <c>200</c> وأَنشَأَ إعلاناً فِعلِيّاً، وعَدَّلَ عُنوان إعلانٍ قائِم
/// وسِعرَه، وحَذَفَه — في <b>أَيّ</b> مُستَأجِر يُسَمّى في المَسار. وقَد
/// وَقَعَ ذلك فِعلاً في <c>ashare</c> (عَرض المُستَثمِرين): ثَلاثَة إعلانات
/// دَخيلَة.</para>
///
/// <para><b>ولِماذا الحَذف لا الحِراسَة:</b> النِقاط الخَمس
/// <b>بِصِفر مُستَهلِك مَقيس</b> — مَسحُ المُستَودَع كُلِّه
/// (<c>.cs</c>, <c>.razor</c>, <c>.js</c>) لَم يَجِد لِمَسارِها ذِكراً
/// خارِجَ سَطر إعلانِها نَفسِه؛ والواجِهَة كُلُّها SSR تَمُرّ بِـ
/// <c>POST /{slug}/listings/create</c> المَحروس بِأَربَعَة حُرّاس
/// (‏<c>RequireAuth</c> + <c>RequireTerms</c> +
/// <c>RequirePermission("listing.create")</c> +
/// <c>RequireEntitlement</c>). فَحِراسَتُها كانَت سَتُبقي سَطحاً
/// مُوازِياً بِلا مُستَعمِل — والقاعِدَة ١: التَجريد لا يَسبِق
/// مُستَهلِكَه. وهُوِيَّة الفاعِل فيها كانَت تَأتي <b>مِن جِسم الطَلَب</b>
/// (‏<c>SenderId</c>/<c>UserId</c>)، فَحارِسُ تَوثيقٍ وَحدَه كانَ
/// سَيُنتِج بَوّابَةً تَبدو مُغلَقَة وتَبقى مَفتوحَة (القاعِدَة ٦).</para>
///
/// <para>المَنطِق نَفسُه (‏<c>ListingCreated</c>/<c>Edited</c>/<c>Deleted</c>
/// وتَجميعُها) في <c>ACommerce.Kit.Listings.Core</c> بِلا مَساس.</para>
/// </summary>
public static class ListingHandlers
{
}
