namespace ACommerce.Templates.Shared.Models;

/// <summary>
/// مساعدات تحويل حالة الطلبات/الحجوزات إلى مفاتيح CSS وتسميات عرض.
/// مُشتركة عبر كل التطبيقات لتجنب تكرار switch expressions.
/// </summary>
public static class StatusHelpers
{
    // ── حالات الطلبات (orders) ────────────────────────────────────────────
    // 0=معلق، 1=مقبول، 2=جاهز، 3=مُسلَّم، 4=ملغى

    /// <summary>
    /// تحويل كود حالة الطلب إلى مفتاح CSS (مثل "pending"، "accepted").
    /// </summary>
    public static string OrderStatusKey(int status) => status switch
    {
        0 => "pending",
        1 => "accepted",
        2 => "ready",
        3 => "delivered",
        4 => "cancelled",
        _ => "pending"
    };

    /// <summary>
    /// تسمية حالة الطلب بالعربية.
    /// </summary>
    public static string OrderStatusAr(int status) => status switch
    {
        0 => "في الانتظار",
        1 => "مقبول",
        2 => "جاهز",
        3 => "تم التسليم",
        4 => "ملغى",
        _ => "غير معروف"
    };

    /// <summary>
    /// تسمية حالة الطلب بالإنجليزية.
    /// </summary>
    public static string OrderStatusEn(int status) => status switch
    {
        0 => "Pending",
        1 => "Accepted",
        2 => "Ready",
        3 => "Delivered",
        4 => "Cancelled",
        _ => "Unknown"
    };

    /// <summary>
    /// تسمية حالة الطلب حسب اللغة.
    /// </summary>
    public static string OrderStatusLabel(int status, bool isArabic)
        => isArabic ? OrderStatusAr(status) : OrderStatusEn(status);

    // ── حالات الحجوزات (bookings) ─────────────────────────────────────────
    // 0=معلقة، 1=مؤكدة، 2=مكتملة، 3=ملغاة

    /// <summary>
    /// تحويل كود حالة الحجز إلى مفتاح CSS.
    /// </summary>
    public static string BookingStatusKey(int status) => status switch
    {
        0 => "pending",
        1 => "confirmed",
        2 => "completed",
        3 => "cancelled",
        _ => "pending"
    };

    /// <summary>
    /// تسمية حالة الحجز بالعربية.
    /// </summary>
    public static string BookingStatusAr(int status) => status switch
    {
        0 => "في الانتظار",
        1 => "مؤكد",
        2 => "مكتمل",
        3 => "ملغى",
        _ => "غير معروف"
    };

    /// <summary>
    /// تسمية حالة الحجز بالإنجليزية.
    /// </summary>
    public static string BookingStatusEn(int status) => status switch
    {
        0 => "Pending",
        1 => "Confirmed",
        2 => "Completed",
        3 => "Cancelled",
        _ => "Unknown"
    };

    /// <summary>
    /// تسمية حالة الحجز حسب اللغة.
    /// </summary>
    public static string BookingStatusLabel(int status, bool isArabic)
        => isArabic ? BookingStatusAr(status) : BookingStatusEn(status);
}
