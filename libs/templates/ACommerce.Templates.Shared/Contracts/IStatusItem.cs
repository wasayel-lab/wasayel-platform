namespace ACommerce.Templates.Shared.Contracts;

/// <summary>
/// واجهة للعناصر ذات الحالة (طلبات، حجوزات، مدفوعات).
/// تمكّن مكونات الحالة من العمل مع أي نوع بيانات بغض النظر عن نطاقه.
/// </summary>
public interface IStatusItem
{
    /// <summary>رمز الحالة الرقمي (مثل: 0=معلق، 1=مقبول، 2=جاهز، 3=مُسلَّم، 4=ملغى)</summary>
    int Status { get; }

    /// <summary>تسمية الحالة للعرض (مثل: "معلق"، "Pending")</summary>
    string StatusLabel { get; }

    /// <summary>مفتاح CSS لتنسيق الحالة (مثل: "pending"، "accepted"، "cancelled")</summary>
    string StatusKey { get; }
}
