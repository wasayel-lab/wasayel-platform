namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// <para><b>إعادَةُ مَن عَلِقَ بِدَرَجَةٍ أَضيَقَ مِن المَجّانِيَّةِ إلَيها</b> (‏2026-10-29، قَرارُ المالِك د16 «أ»؛ ‏ADR-026 مُلحَق د16) — القَرارُ
/// هُنا دالَّةً نَقِيَّة، والسكربتُ <c>scripts/repair-studio-tier.cs</c> يَعرِضُ ثُمَّ يُنَفِّذُ بِعَدَدٍ يُؤَكَّد.</para>
///
/// <para><b>العِلَّةُ المَقيسَة</b> («ج» S11·١٩): مَن خُزِّنَت لَه <c>lite</c> (‏3 · 10 · 3) أَو <c>growth</c> (‏10 · 50 · 10) قَبلَ إغلاقِ
/// <c>/studio/billing/select</c> يَومَ ‏2026-08-30 حُدودُه اليَومَ أَضيَقُ مِن المَجّانِيَّةِ المَرفوعَة (‏40 · 200 · 40)، ولا رُجوعَ لَه إلَيها:
/// <see cref="StudioTierPurchase.Refuse"/> يَرُدُّ <c>spark</c> لِأَنَّ لَها سِعراً ولا دَفعَ ذاتِيّاً.</para>
///
/// <para><b>والمُرَشَّحُ يُقاسُ بِالأَرقامِ لا بِالأَسماء</b>: دَرَجَةٌ مَعروفَةٌ في الكاتالوجِ تَتَفَوَّقُ عَلَيها المَجّانِيَّةُ اليَومَ
/// (<see cref="TierCatalog.Exceeds"/> — لا تَنقُصُ في حَدٍّ وتَزيدُ في واحِد). فَـ<c>scale</c> (حُدودُها حُدودُ المَجّانِيَّةِ المَرفوعَة) لا تُمَسّ،
/// ويَومَ يُنهى الرَفعُ المُؤَقَّتُ لا يَعودُ مُرَشَّحٌ واحِد — فَلا يُنزِلُ السكربتُ أَحَداً إلى أَضيَق. ودَرَجَةٌ مُخَزَّنَةٌ لا يَعرِفُها الكاتالوجُ يَقرَؤُها
/// <see cref="TierCatalog.For"/> مَجّانِيَّةً أَصلاً فَلا تُمَسّ.</para>
///
/// <para><b>والدَرَجَةُ السابِقَةُ تُحفَظُ في سَطرِ التَدقيق</b> (<c>before</c>) لا في حَقلٍ جَديد (القاعِدَة ١: لا مُستَهلِكَ لَه اليَوم) — ويَومَ يُنهى الرَفعُ
/// يُعادُ كُلٌّ إلى الأَوسَعِ بَينَ دَرَجَتِه السابِقَةِ والمَجّانِيَّةِ يَومَئِذٍ، لا إلى أَضيَقَ مِنها (نَصُّ القَرار، ‏ADR-026 مُلحَق د16).</para>
/// </summary>
public static class StudioTierRepair
{
    /// <summary>الدَرَجَةُ المَجّانِيَّة.</summary>
    public const string Free = "spark";

    /// <summary>سَطرُ التَدقيقِ لِكُلِّ مُستَخدِمٍ يُعادُ — <c>before</c> دَرَجَتُه السابِقَة و<c>after</c> المَجّانِيَّة.</summary>
    public const string AuditAction = "billing.tier.repair_to_free";

    /// <summary>أَعالِقٌ هذا المُستَخدِمُ بِدَرَجَةٍ أَضيَقَ مِن المَجّانِيَّةِ اليَوم؟</summary>
    public static bool IsStuck(string? storedTier)
        => storedTier is { Length: > 0 } t
        && !string.Equals(t, Free, StringComparison.Ordinal)
        && TierCatalog.All.ContainsKey(t)
        && TierCatalog.Exceeds(TierCatalog.For(Free), TierCatalog.For(t));
}
