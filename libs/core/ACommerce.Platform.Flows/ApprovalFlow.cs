namespace ACommerce.Platform.Flows;

/// <summary>
/// <para><b>تَدَفُّق الاعتِماد المُشتَرَك</b> — <c>pending</c> عِندَ
/// الكِتابَة، ثُمَّ <c>approved</c> أَو <c>rejected</c> بِقَرار بَشَريّ.
/// لا حالَة رابِعَة، ولا عَودَة مِن <c>approved</c> إلى <c>pending</c>:
/// المُراجَعَة تُعيد الكِتابَة بِوَثيقَة جَديدَة.</para>
///
/// <para><b>مَوضِع التَعريف الواحِد</b>. كانَ هذا المَعجَم مَكتوباً
/// <b>مَرَّتَين</b> — في <c>TenantRoleStatuses</c> و
/// <c>TenantThemeStatuses</c> — مُتَطابِقَين بايتاً ببايت عَدا اسم
/// الصَنف. صارَ هُنا مَرَّةً واحِدَة، والصَنفانِ يُحيلانِ إلَيه.</para>
///
/// <para><b>ولِماذا بَقِيَت الثَوابِت <c>const</c> لا
/// <c>static readonly</c></b>: القيَم تُستَخدَم في <b>أَنماط ثابِتَة</b>
/// (<c>existing is { Status: TenantRoleStatuses.Approved }</c>) وفي
/// تَعابير Marten LINQ — وكِلاهُما يَشتَرِط <c>const</c>. و<c>const</c>
/// يُسنَد مِن <c>const</c> آخَر، فَالإحالَة تَبقى تَعريفاً واحِداً
/// حَقيقِيّاً بِلا كَسر مَوضِع استِعمال واحِد.</para>
///
/// <para><b>وما لَم يُضَمّ عَمداً</b>: نِداء أَداة الوَكيل
/// (<c>AgentToolCall.Status</c>). مَعجَمُه أَربَعَة لا ثَلاثَة،
/// وحالَة نَجاحِه <c>applied</c> لا <c>approved</c> — فَضَمُّه كانَ
/// <b>تَغيير سُلوك</b> في وَثائِق مُخَزَّنَة قائِمَة لا إعادَة
/// تَنظيم. الحارِس مَكتوب في
/// <c>ApprovalFlowCharacterizationTests</c>.</para>
/// </summary>
public static class ApprovalFlow
{
    public const string Pending  = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";

    /// <summary>الحالات الثَلاث <b>بِتَرتيبِها</b> — والتَرتيب جُزء
    /// مِن العَقد: مُثَبَّت في التَوصيف.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Pending, Approved, Rejected };

    private static readonly HashSet<string> Set = new(All, StringComparer.Ordinal);

    /// <summary>العُضوِيَّة <b>حَسّاسَة لِلحالَة</b> (Ordinal) — كَما
    /// كانَت في الصَنفَين.</summary>
    public static bool Contains(string status) => Set.Contains(status);

    /// <summary>
    /// <para>يَبني تَعريف تَدَفُّق اعتِماد بِاسمِه وتَسمِيَتِه —
    /// <b>نَفس الشَكل</b> لِكُلّ مَن يَستَخدِمُه. هذا هو ما يَجعَل
    /// «تَعريفاً واحِداً» عِبارَةً دَقيقَةً لا مَجازاً: تَدَفُّقا
    /// الأَدوار والمَظهَر يَخرُجانِ مِن هذه الدالَّة، فَلا يُمكِن
    /// أَن يَنحَرِف أَحَدُهُما عَن الآخَر بِتَعديل يَدَويّ.</para>
    /// </summary>
    public static FlowDefinition Definition(string flow, FlowLabel label) => new(
        Flow: flow,
        Label: label,
        States: new[]
        {
            new FlowState(Pending,  new FlowLabel("مُعَلَّق", "Pending")),
            new FlowState(Approved, new FlowLabel("مُعتَمَد", "Approved"), IsTerminal: true),
            new FlowState(Rejected, new FlowLabel("مَرفوض", "Rejected"), IsTerminal: true),
        },
        Transitions: new[]
        {
            new FlowTransition(FlowVocabulary.Genesis, Pending, "system",
                new FlowLabel("اقتِراح تَعريف", "Propose"), new[] { "invalidate_tenant_cache" }),
            new FlowTransition(Pending, Approved, "moderator",
                new FlowLabel("اِعتَمِد", "Approve"), new[] { "revalidate_definition" }),
            new FlowTransition(Pending, Rejected, "moderator",
                new FlowLabel("اِرفُض", "Reject"), new[] { "invalidate_tenant_cache" }),
        },
        Initial: Pending);
}
