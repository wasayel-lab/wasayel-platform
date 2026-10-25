namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// <para><b>بابُ وَكيلِ التَطبيقاتِ في الاستوديو (<c>/studio/agent</c>) — دالَّةٌ نَقِيَّةٌ تَسأَلُها الصَفحَةُ ولَوحَتي مَعاً</b>
/// (‏2026-10-25، تَدقيقُ الإطلاق — تَيّارُ W8-3، <c>lens-security-2</c>؛ عَلى سابِقَةِ <see cref="StudioAnalysisDoor"/>).</para>
///
/// <para><b>العِلَّةُ المَقيسَة</b> (‏<c>StudioAgentDoorTests</c>، وتَوصيفُها <c>92bd370</c>): كانَت الصَفحَةُ تَفتَحُ مُحادَثَتَها
/// لِ<b>كُلِّ صاحِبِ جَلسَةِ استوديو</b> — والدُخولُ مَفتوحٌ لِأَيِّ رَقَمٍ أَو بَريد — وكُلُّ رِسالَةٍ (و«طَبِّق»/«ارفُض») نِداءُ نَموذَجِ لُغَةٍ
/// بِتاريخِ المُحادَثَةِ كامِلاً عَلى مِفتاحِ المالِك، <b>بِلا عَدّادٍ ولا حَدّ</b> (‏<c>LanguageModelQuotaGateTests.PinnedUngated</c>،
/// ‏ADR-016 §٧). ومِفتاحُ <c>Agent__ApiKey</c> وَحدَه — المَكتوبُ في DEPLOY §٤·ب لازِماً لِلتَحليل — يَفتَحُه، لِأَنّ
/// <c>Agents:Studio:*</c> يَسقُطُ إلَيه (‏<c>AgentProfileResolver.Resolve</c>).</para>
///
/// <para><b>والافتِراضُ إلى أَن يوجَدَ رَقَم — قابِلٌ لِلنَقض</b>: البابُ لِمُشرِفِ المَنَصَّةِ وَحدَه — <b>سُلطَةُ المالِكِ عَلى
/// مِفتاحِه</b>، التَصنيفُ الَّذي تَعرِفُه <c>LanguageModelQuotaGateTests.OwnerAuthorityGates</c> لِـ<c>/admin/agent</c> — والمُدخَلُ
/// قَرارُ <see cref="PlatformAdminGuard"/> نَفسُه لا تَعريفٌ ثانٍ. <b>ولا يُعَدُّ رِسالَةُ وَكيلٍ تَحليلاً</b>
/// (<c>CheckAnalyzeAsync</c>): لافِتَةُ التَرقِيَةِ تَكذِبُ حينَئِذٍ (‏ADR-016 §٧). <b>وحَدُّ الرَسائِلِ لِكُلِّ دَرَجَةٍ رَقَمُ المالِك</b>
/// («ب» W8-3 · ١ في سِجِلِّه، القاعِدَة ١٦)؛ ويَومَ يَقولُه يَصيرُ البابُ «أَيُّ صاحِبِ جَلسَة» ويَدخُلُ <c>CheckAgentMessageAsync</c>
/// بَعدَه في المُعالِجات — والتَبديلُ هُنا وفي سَطرَي الصَفحَتَين.</para>
///
/// <para><b>ومَن يَسأَلُه</b>: <c>StudioHome</c> (زِرُّ الشَريطِ وبِطاقَةُ «وَكيل التَّطبيقات» — لا تُرسَمانِ لِمَن لا يُفتَحُ لَه، القاعِدَة ١٢)،
/// و<c>StudioAgent</c> (المُحادَثَةُ والمُحَرِّرُ داخِلَ فَرعِه المَفتوح، ونَصُّ <see cref="ClosedMessageKey"/> في غَيرِه، والمُعالِجاتُ
/// الأَربَعَةُ تَسأَلُه قَبلَ أَيِّ نِداء). وكُلُّ رابِطٍ إلى الصَفحَةِ خارِجَ هذا البابِ صَفٌّ يَحمَرّ
/// (<c>StudioAgentDoorTests.Every_link_to_the_agent_page_sits_inside_an_open_agent_door</c>).</para>
///
/// <para><b>ولا يَقرَأُ حالَةً مُخَزَّنَة</b> (القاعِدَة ٧): مُدخَلُه قَرارٌ قُرِئَ قَبلَه.</para>
/// </summary>
public static class StudioAgentDoor
{
    /// <summary>مِفتاحُ القامُوسِ الَّذي يُرسَمُ مَكانَ المُحادَثَة. <b>والنَصُّ مِن كِتابَةِ الوَكيلِ إلى أَن يَكتُبَه المالِك</b>
    /// («ب» W8-3 · ١ في سِجِلِّه).</summary>
    public const string ClosedMessageKey = "studio.agent.unavailable";

    /// <summary>أَيُفتَحُ البابُ؟ — نَعَم حينَ تَكونُ خَلفِيَّةُ وَكيلِ <c>Studio</c> مَضبوطَةً (<see cref="AgentService.IsConfigured"/>:
    /// مِفتاحٌ مَحلولٌ بِقاعِدَةِ السُقوط، أَو Ollama مَحَلِّيّ) <b>و</b>صاحِبُ الجَلسَةِ مُشرِفُ المَنَصَّة
    /// (<see cref="PlatformAdminGuard"/>: <see cref="PlatformAdminOutcome.Allowed"/>).
    ///
    /// <para><b>‏2026-10-25 (W8-3 — <c>lens-dead-doors-1</c>؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق)</b>: كانَ مُدخَلُه الإشرافَ وَحدَه — فَبِلا
    /// مِفتاحِ وَكيلِ <c>Studio</c> (و<c>Agents__Analysis__ApiKey</c> وَحدَه، كَما في «ج» S12·٥، يَفتَحُ التَحليلَ ولا يَفتَحُه) كانَ الرابِطانِ يُرسَمانِ
    /// ويَفتَحانِ عَلى مُحَرِّرٍ مُعَطَّلٍ ونَصِّ مُشَغِّل. صارَ الغِيابُ يُخفي المَدخَلَ (القاعِدَة ١٢) ويَرسُمُ <see cref="ClosedMessageKey"/> نَفسَه.</para></summary>
    public static bool Opens(bool agentBackendConfigured, bool platformAdmin) => agentBackendConfigured && platformAdmin;
}
