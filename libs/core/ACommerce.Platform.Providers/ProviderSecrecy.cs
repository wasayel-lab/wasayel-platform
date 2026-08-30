namespace ACommerce.Platform.Providers;

/// <summary>
/// <para><b>العَتامَة — والقَرارُ يُؤخَذ بِالنَوعِ لا بِالنِيَّة.</b>
/// الفارِقُ الفاصِلُ بَينَ نَوعَينِ ثَلاثَةُ أَعمِدَة: ما يُخَزَّن،
/// <b>أَيُعرَض</b>، أَيُسترَجَع. وهذا المِلَفُّ هُوَ العَمودُ الثاني
/// وَحدَه.</para>
///
/// <para><b>والقَيدُ الَّذي يَفرِضُه</b>: السِرُّ لا يُعرَض بَعدَ حِفظِه
/// أَبَداً — لا في شاشَةٍ ولا في API ولا في لوغ ولا في رِسالَةِ خَطَأ.
/// و<c>secret_key</c> وَحدَه يُعرَض مِنه آخِرُ أَربَعَةِ مَحارِف،
/// لِيَعرِفَ صاحِبُه أَيَّ مِفتاحٍ رَبَط.</para>
///
/// <para><b>ولِماذا يُكتَبُ اليَومَ وثَلاثَةُ أَنواعٍ فَقَط مَشحونَة</b>:
/// لِأَنّ لَه ثَلاثَةَ مُستَهلِكينَ في هذِه المَوجَة — الشاشَةُ الَّتي
/// تَرسُم القيمَة، وسِجِلُّ التَدقيقِ الَّذي يَكتُب
/// <c>Before</c>/<c>After</c>، ونُقطَةُ الرَبطِ الَّتي تَرُدّ. وهذا
/// شَرطُ القاعِدَةِ ١ مُستَوفىً بِالعَدّ لا بِالنِيَّة.</para>
/// </summary>
public static class ProviderSecrecy
{
    /// <summary>القِناعُ الَّذي يَحُلُّ مَحَلَّ ما لا يُعرَض. ثابِتٌ
    /// لا يَتَغَيَّرُ بِطولِ السِرّ — فَطولُ القِناعِ نَفسُه تَسريب.</summary>
    public const string Mask = "••••••";

    /// <summary>الأَنواعُ الَّتي تُعرَض كامِلَةً — وكُلُّها <b>عامَّةٌ
    /// بِتَعريفِ مُزَوِّدِها</b>: رابِطُ فاتورَةٍ يُشارَك، ومِفتاحٌ
    /// تَقول وَثيقَتُه إنَّه «آمِنٌ لِيُشحَنَ في كودِ العَميل»،
    /// ومُعَرِّفُ حِسابِ خِدمَةٍ لَيسَ سِرّاً أَصلاً.</summary>
    public static bool IsDisplayable(string kind) =>
        kind is CredentialKinds.HostedLink
             or CredentialKinds.PublishedKey
             or CredentialKinds.DelegatedGrant;

    /// <summary><c>secret_key</c> وَحدَه يُعرَض مِنه ذَيلٌ.</summary>
    public static bool ShowsTail(string kind) => kind is CredentialKinds.SecretKey;

    public const int TailLength = 4;

    /// <summary>
    /// <para>الصورَةُ الوَحيدَةُ الَّتي يَجوز أَن تَخرُجَ إلى شاشَةٍ
    /// أَو لوغٍ أَو حَقلِ تَدقيق.</para>
    /// </summary>
    public static string Censor(string kind, string? value)
    {
        if (kind == CredentialKinds.None) return "";
        if (string.IsNullOrEmpty(value)) return "";
        if (IsDisplayable(kind)) return value;

        if (ShowsTail(kind) && value.Length > TailLength)
            return Mask + value[^TailLength..];

        return Mask;
    }

    /// <summary><b>لَقطَةُ الرَبطِ كَما تُكتَب في التَدقيق</b> —
    /// مُعَتَّمَةً بِالبِناء لا بِالنِيَّة: الدالَّةُ لا تَقبَل نَصّاً
    /// صَريحاً إلّا مِن نَوعٍ يُعرَض.</summary>
    public static string ForAudit(
        string? providerSlug, string status,
        IEnumerable<KeyValuePair<string, string>> kindsByField,
        Func<string, string?> rawOf)
    {
        var parts = kindsByField
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={Censor(p.Value, rawOf(p.Key))}");

        return $"provider={providerSlug ?? "-"}; status={status}; " +
               string.Join("; ", parts);
    }
}
