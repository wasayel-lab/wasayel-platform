namespace ACommerce.Templates.Shared.Contracts;

/// <summary>
/// واجهة للعناصر ذات السعر (عروض، منتجات، باقات).
/// تمكّن مكونات العرض السعري من العمل مع أي نوع بيانات.
/// </summary>
public interface IPricedItem
{
    /// <summary>السعر الحالي</summary>
    decimal Price { get; }

    /// <summary>السعر الأصلي قبل الخصم (اختياري)</summary>
    decimal? OriginalPrice { get; }

    /// <summary>رمز العملة (مثل: SAR)</summary>
    string Currency { get; }
}
