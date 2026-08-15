namespace ACommerce.Kit.Subscriptions.Server;

/// <summary>
/// <para><b>الصِنف باقٍ فارِغاً عَمداً</b> — <c>Program.cs</c> يُحيل إلَيه
/// لِمَسح الـ assembly.</para>
///
/// <para><b>ما كانَ هُنا وَلِماذا زال:</b> ثَلاث نِقاط HTTP —
/// <c>GET /{slug}/api/plans</c> و<c>POST /{slug}/api/subscriptions/start</c>
/// و<c>GET /{slug}/api/subscriptions/{userId}</c> — <b>بِلا حارِس واحِد</b>.
/// والأَخطَر <c>start</c>: مَجهول يَبدَأ اشتِراكاً لِأَيّ
/// <c>UserId</c> يَكتُبُه في الجِسم، فَيَمنَح <b>حِصَّة إعلانات</b>
/// (‏<c>ListingsQuota</c>) بِلا دَفع ولا جَلسَة — أَي التِفاف كامِل
/// على الاستِحقاق الَّذي يَحرُس <c>listings/create</c>.</para>
///
/// <para><b>ولِماذا الحَذف لا الحِراسَة:</b> صِفر مُستَهلِك مَقيس؛
/// والمَسار الحَيّ <c>POST /{slug}/plans/{planId}/subscribe</c> مَحروس
/// (‏<c>302</c> إلى الدُخول لِلمَجهول) ويَأخُذ المُستَخدِم مِن
/// الجَلسَة.</para>
/// </summary>
public static class SubscriptionHandlers
{
}
