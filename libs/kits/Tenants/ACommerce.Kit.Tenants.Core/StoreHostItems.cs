namespace ACommerce.Kit.Tenants;

/// <summary>
/// <para><b>ما يَترُكُه حاقِنُ المُضيفِ في <c>HttpContext.Items</c> لِمَن بَعدَه</b> (‏2026-11-07، ADR-050 §٩ · W13-4·C2a·6). يَكتُبُهُما
/// <c>SubdomainTenantPathMiddleware</c> حينَ يَأتي الطَلَبُ عَلى مُضيفِ مَتجَر (<c>{slug}.R</c>)، قَبلَ أَن يَحقِنَ <c>/{slug}</c> في المَسار.</para>
///
/// <para><b>ولِماذا هُنا لا في الوَسيط</b>: كانا ثابِتَينِ في <c>Platform.MultiTenancy</c> <b>بِصِفرِ قارِئ</b> (القاعِدَة ١ — A2 §2.1)، وقُرّاؤُهُما
/// الآنَ في طَبَقَتَينِ لا تَريانِ ذلكَ المَشروع: القالِبُ (الـcanonical وog:url وسِياقُ العَميلِ في <c>App.razor</c>) وعُدَّةُ المُستَأجِرين
/// (خَريطَةُ مَوقِعِ المُضيف في <c>SeoHandlers</c>). فَالمَفتاحانِ في العُدَّةِ الَّتي يَراها الثَلاثَة، وثابِتا الوَسيطِ يُحيلانِ إلَيهِما.</para>
/// </summary>
public static class StoreHostItems
{
    /// <summary>مُعَرِّفُ مَتجَرِ المُضيف — وُجودُه هُوَ «الطَلَبُ عَلى مُضيفِ مَتجَر».</summary>
    public const string Slug = "acommerce.tenant.host-slug";

    /// <summary>المَسارُ كَما رَآه المُتَصَفِّحُ قَبلَ الحَقن — العُنوانُ الظاهِر.</summary>
    public const string OriginalPath = "acommerce.tenant.original-path";
}
