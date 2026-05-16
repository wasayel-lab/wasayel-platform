namespace Ejar.Customer.UI.Services;

/// <summary>
/// خِيارات إخفاء بُنود مُعَيَّنَة في صَفحات قالَب Marketplace. تَستَخدِمها
/// تَطبيقات لِإيقاف ميزات لا تُسَجِّل kit-ها (مَثَلاً V3 لا يَملِك
/// Subscriptions kit حاليّاً ⇒ "الباقات" و "اشتراكي" يَجِب أَن يَختَفيا).
///
/// <para>السُلوك الافتِراضي: كُلّ شَيء مَعروض (تَوافُق مَع تَطبيقات
/// قائِمَة كَ Ejar V1).</para>
///
/// <para>الاستِخدام في Program.cs لِلتَطبيق:</para>
/// <code>
/// services.AddSingleton(new MarketplaceUiOptions
/// {
///     ShowSubscriptionsMenu = false,   // لا باقات في هذا الإصدار
/// });
/// </code>
/// </summary>
public sealed class MarketplaceUiOptions
{
    /// <summary>
    /// إخفاء زُرَّي "الباقات" و "اشتراكي" مِن صَفحَة <c>/me</c>. تُعَطَّل
    /// عِندَما لا يُسَجِّل التَطبيق Subscriptions kit. الـ routes نَفسها
    /// لا تُحجَب — مُعَلَّقَة فَقَط إلى أَن يَعود الكيت.
    /// </summary>
    public bool ShowSubscriptionsMenu { get; set; } = true;
}
