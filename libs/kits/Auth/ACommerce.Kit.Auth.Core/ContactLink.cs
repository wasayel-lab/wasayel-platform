namespace ACommerce.Kit.Auth;

/// <summary>خَرقٌ في رَبطِ وَسيلَةِ دُخولٍ بِحِسابٍ قائِم — <c>Code</c> ثابِتٌ لِلاختِبارِ والقامُوس، و<c>MessageAr</c> لِلمُراجِع (القاعِدَة ٤).</summary>
public sealed record ContactLinkViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>«أَضِف بَريدَك» و«أَضِف جَوّالَك» — رَبطُ وَسيلَةِ دُخولٍ ثانِيَةٍ بِالحِسابِ نَفسِه</b> (‏2026-10-28، قَرارُ المالِك د27 «ج»؛ مَرجِعُه NIST 800-63B).
/// كانَ الدُخولُ بِالبَريدِ يَبحَثُ بِالبَريدِ وَحدَه ويُنشِئُ حِساباً جَديداً إن لَم يَجِد، فَمَن دَخَلَ مَرَّةً بِجَوّالِه ومَرَّةً بِبَريدِه صارَ حِسابَين
/// (تَوصيفُه <c>bad4a73</c>). والآن تُضافُ الوَسيلَةُ <b>مِن داخِلِ جَلسَةٍ مُثبَتَة</b> و<b>بَعدَ إثباتِ مِلكِيَّتِها بِرَمزٍ يَصِلُها</b> — ولا دَمجَ تِلقائِيٌّ
/// أَبَداً: وَسيلَةٌ مُستَعمَلَةٌ في حِسابٍ آخَرَ تُرفَضُ بِرَمزٍ يَشرَحُ الحَلَّ في القامُوس (ادخُل بِها، واحذِف ذلِكَ الحِسابَ إن كانَ فارِغاً، ثُمَّ عُد).</para>
/// <para><b>والقَرارُ كُلُّه هُنا نَقِيّ</b> (القاعِدَة ٦): ما يُقبَلُ لِلبَدء، وما يُقبَلُ لِلرَبطِ بَعدَ الرَمز، وما يَكتُبُه الرَبط. والنُقطَتانِ تُطَبِّقانِه.
/// <b>و«مُستَعمَلٌ» يُسأَلُ بَعدَ الرَمزِ لا قَبلَه</b>: سُؤالُه قَبلَه يُخبِرُ مَن لا يَملِكُ الوَسيلَةَ أَنَّ لَها حِساباً (تَعدادُ الحِسابات).</para>
/// </summary>
public static class ContactLink
{
    /// <summary>نَوعا الوَسيلَة — مَعجَمٌ مُغلَق.</summary>
    public const string Email = "email";
    public const string Phone = "phone";
    public static readonly IReadOnlyList<string> Kinds = new[] { Email, Phone };

    // ─── مَعجَمُ الرَفضِ المُغلَق ───────────────────────────────────
    public const string KindUnknown = "contact_kind_unknown";
    public const string NoAccount = "contact_no_account";
    public const string Invalid = "contact_invalid";
    public const string AlreadySet = "contact_already_set";
    public const string InUse = "contact_in_use";

    public static readonly IReadOnlyList<string> All = new[] { KindUnknown, NoAccount, Invalid, AlreadySet, InUse };

    /// <summary>الوَسيلَةُ كَما تُخَزَّنُ وتُقارَن: البَريدُ بِتَطبيعِ الدُخولِ نَفسِه (<see cref="EmailAddress.Normalize"/>)، والجَوّالُ مَقصوصاً كَما يُخَزِّنُه الدُخول.</summary>
    public static string Normalize(string? kind, string? value)
        => kind == Email ? EmailAddress.Normalize(value) : (value ?? "").Trim();

    /// <summary>
    /// <para><b>أَيُقبَلُ الرَبط؟</b> <c>null</c> = امضِ. التَرتيب: الحِساب، ثُمَّ النَوع، ثُمَّ القيمَة، ثُمَّ أَن لا تَكونَ لِلحِسابِ وَسيلَةٌ مِن هذا النَوعِ أَصلاً
    /// (الإضافَةُ لا التَبديل) — وهذا ما يُسأَلُ قَبلَ إرسالِ الرَمز (<paramref name="holders"/> = <c>null</c>). وبَعدَ رَمزٍ صَحيحٍ تُمَرَّرُ مَن يَحمِلُ
    /// الوَسيلَةَ في المَتجَر، فَيُضافُ الشَرطُ الأَخير: <b>لا حِسابَ آخَرَ قائِمٌ يَحمِلُها</b>.</para>
    /// </summary>
    public static ContactLinkViolation? Validate(User? user, string? kind, string value, IEnumerable<User>? holders = null)
    {
        if (user is null || user.DeletedAt is not null)
            return new(NoAccount, "لا حِسابَ قائِمٌ بِهذِه الجَلسَة.");
        if (kind is not (Email or Phone))
            return new(KindUnknown, "نَوعُ الوَسيلَةِ خارِجَ المَعجَم (email|phone).");
        if (kind == Email ? !EmailAddress.IsValid(value) : string.IsNullOrWhiteSpace(value))
            return new(Invalid, "الوَسيلَةُ فارِغَةٌ أَو لَيسَت بَريداً صالِحاً.");
        if (Current(user, kind).Length > 0)
            return new(AlreadySet, "لِلحِسابِ وَسيلَةٌ مِن هذا النَوع — الإضافَةُ لا التَبديل.");
        if (holders is not null && holders.Any(h => h.Id != user.Id && h.DeletedAt is null))
            return new(InUse, "الوَسيلَةُ مُستَعمَلَةٌ في حِسابٍ آخَرَ في هذا المَتجَر — ولا يُدمَجُ شَيءٌ بِصَمت.");
        return null;
    }

    public static bool IsAllowed(User? user, string? kind, string value, IEnumerable<User>? holders = null)
        => Validate(user, kind, value, holders) is null;

    /// <summary>الرَبط: الوَسيلَةُ في حَقلِها ومُتَحَقَّقٌ مِنها (أَثبَتَها رَمز). يُنادى بَعدَ <see cref="Validate"/> بِمَن يَحمِلُها وَحدَه.</summary>
    public static void Link(User user, string kind, string value, DateTime at)
    {
        if (kind == Email) { user.Email = value; user.EmailVerified = true; }
        else { user.Phone = value; user.PhoneVerified = true; }
        user.UpdatedAt = at;
    }

    /// <summary>مَوضوعُ مُحاوَلَةِ الرَبط: الحِسابُ والوَسيلَة — فَرَمزٌ أُرسِلَ لِحِسابٍ لا يَربِطُ بِحِسابٍ غَيرِه، ولا يُبَدِّلُ طَلَبُ غَيرِه مُحاوَلَتَه.</summary>
    public static string Subject(Guid userId, string value) => $"{userId:N}|{value}";

    /// <summary>القيمَةُ مِن مَوضوعٍ لِهذا الحِسابِ وَحدَه — وإلّا <c>null</c>.</summary>
    public static string? ValueFor(string? subject, Guid userId)
    {
        var prefix = $"{userId:N}|";
        return subject is not null && subject.StartsWith(prefix, StringComparison.Ordinal) && subject.Length > prefix.Length
            ? subject[prefix.Length..] : null;
    }

    private static string Current(User user, string kind) => (kind == Email ? user.Email : user.Phone) ?? "";
}
