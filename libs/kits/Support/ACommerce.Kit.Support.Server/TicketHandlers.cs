namespace ACommerce.Kit.Support.Server;

/// <summary>
/// <para><b>الصِنف باقٍ فارِغاً عَمداً</b> — <c>Program.cs</c> يُحيل إلَيه
/// لِمَسح الـ assembly.</para>
///
/// <para><b>ما كانَ هُنا وَلِماذا زال:</b> ثَلاث نِقاط HTTP —
/// <c>POST /{slug}/api/support/open</c> و<c>…/{id}/reply</c>
/// و<c>GET …/mine</c> — <b>بِلا حارِس واحِد</b>. القياس الحَيّ
/// (‏2026-08-15): مَجهول فَتَحَ تَذكِرَة، و<b>رَدَّ عَلى أَيّ تَذكِرَة
/// بِـ <c>FromStaff: true</c></b> — أَي انتَحَلَ الدَعم الفَنّيّ نَفسَه —
/// وقَرَأَ تَذاكِر أَيّ مُستَخدِم بِمُعَرِّفِه.</para>
///
/// <para><b>ولِماذا الحَذف لا الحِراسَة:</b> صِفر مُستَهلِك مَقيس؛
/// والمَسار الحَيّ <c>POST /{slug}/support/open</c> مَحروس
/// (‏<c>302</c> إلى الدُخول لِلمَجهول)، والرَدّ الإشرافيّ يَمُرّ بِـ
/// <c>POST /studio/apps/{slug}/tickets/{id}/reply</c> خَلف
/// <c>StudioOwnsAsync</c>. و<c>FromStaff</c> المَأخوذ مِن جِسم الطَلَب
/// يَجعَل التَوثيق وَحدَه غَير كافٍ (القاعِدَة ٦).</para>
/// </summary>
public static class TicketHandlers
{
}
