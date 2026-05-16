namespace ACommerce.Templates.Shared.Contracts;

/// <summary>
/// واجهة للعناصر المملوكة (عروض مرتبطة بمتجر، منشورات مرتبطة بمستخدم).
/// تمكّن واجهات التحقق من الملكية من العمل عبر النطاقات.
/// </summary>
public interface IOwnedItem
{
    /// <summary>معرف المالك (مثل: Guid المتجر أو المستخدم)</summary>
    string OwnerId { get; }
}
