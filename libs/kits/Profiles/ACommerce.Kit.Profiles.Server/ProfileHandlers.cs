namespace ACommerce.Kit.Profiles.Server;

/// <summary>
/// <para><b>الصِنف باقٍ فارِغاً عَمداً</b> — <c>Program.cs</c> يُحيل إلَيه
/// لِمَسح الـ assembly.</para>
///
/// <para><b>ما كانَ هُنا وَلِماذا زال:</b> نُقطَتا HTTP —
/// <c>GET /{slug}/api/profile/{userId}</c> و
/// <c>POST /{slug}/api/profile/update</c> — <b>بِلا حارِس واحِد</b>.
/// الأُولى كانَت تُعيد <b>وَثيقَة <c>User</c> كامِلَةً</b> لِمَجهول
/// يَعرِف المُعَرِّف — وفيها <c>Phone</c> و<c>NationalId</c> (رَقم
/// الهُوِيَّة الوَطَنِيَّة)؛ والثانِيَة كانَت <b>تُعيد تَسمِيَة أَيّ
/// مُستَخدِم</b> بِـ <c>UserId</c> مِن جِسم الطَلَب. وهذا بِعَينِه
/// التَسَرُّب الَّذي وُثِّق في <c>TenantAdminGuard</c> على صَفَحات
/// الإدارَة، ناجِياً في طَبَقَة الـ API.</para>
///
/// <para><b>ولِماذا الحَذف لا الحِراسَة:</b> صِفر مُستَهلِك مَقيس؛
/// والتَعديل الحَيّ يَمُرّ بِـ <c>POST /{slug}/me/save</c> الَّذي يَأخُذ
/// المُعَرِّف مِن الجَلسَة لا مِن الجِسم.</para>
/// </summary>
public static class ProfileHandlers
{
}
