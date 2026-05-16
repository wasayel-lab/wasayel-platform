namespace ACommerce.Templates.Shared.Contracts;

/// <summary>
/// واجهة للعناصر التي تظهر في قوائم العرض.
/// تمكّن قوالب القوائم من العمل مع أي نوع بيانات طالما يُطبّق هذه الواجهة.
/// </summary>
public interface IListableItem
{
    /// <summary>معرف العنصر (مستخدم للتنقل والمفتاح)</summary>
    string Id { get; }

    /// <summary>العنوان الرئيسي للعنصر</summary>
    string Title { get; }

    /// <summary>النص الفرعي (اختياري)</summary>
    string? Subtitle { get; }

    /// <summary>الإيموجي أو الأيقونة (اختياري)</summary>
    string? Emoji { get; }

    /// <summary>مسار التنقل عند النقر على العنصر (اختياري)</summary>
    string? Href { get; }
}
