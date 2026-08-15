namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>سِجِلّات المَسارات المُثَبَّتَة</b> — مَعزولَة في مِلَفِّها
/// عَمداً. القائِمَتانِ أَدناه <b>بَيانات لا مَنطِق</b>، وطولُهُما
/// عَشَرات الأَسطُر؛ وتَركُهُما داخِلَ صِنف الفَحص يَدفِن الفَحص
/// نَفسَه تَحتَ سِجِلِّه.</para>
///
/// <para><b>وهُما يَتَقَلَّصانِ لا يَنمُوان</b>: كُلّ مَوجَة تَرحيل
/// تَحذِف سُطوراً مِن هُنا، وكُلّ إضافَة سَطرٍ هُنا قَرارٌ يُتَّخَذ
/// بِاليَد في نَفس الكوميت الَّذي يَخرِق القاعِدَة — وهذا هُوَ الفَرق
/// بَينَ دَينٍ يُتَّخَذ ودَينٍ يَنزَلِق.</para>
/// </summary>
internal static class PinnedRoutes
{
    /// <summary>نُقاط تَأخُذ <c>IDocumentStore</c> في تَوقيعِها —
    /// السِجِلّ الابتِدائيّ. راجِع
    /// <c>EndpointStoreInjectionTests</c>.</summary>
    internal static readonly string[] StoreTakers =
    {
        "/{slug}/auth/phone/login",
        "/{slug}/auth/phone/verify",
        "/{slug}/auth/email/login",
        "/{slug}/auth/email/verify",
        "/{slug}/auth/nafath/verify",
        "/{slug}/auth/logout",
        "/{slug}/api/me/unread",
        "/{slug}/listings/{id:guid}/favorite",
        "/{slug}/listings/{id:guid}/chat",
        "/{slug}/me/role/save",
        "/{slug}/me/role/onboarding/save",
        "/{slug}/me/save",
        "/{slug}/plans/{planId}/subscribe",
        "/{slug}/support/open",
        "/{slug}/listings/{id:guid}/report",
        "/{slug}/listings/create",
        "/{slug}/searches/save",
        "/{slug}/searches/{id:guid}/delete",
        "/{slug}/searches/{id:guid}/toggle",
        "/{slug}/listings/{id:guid}/offers",
        "/{slug}/offers/{id:guid}/accept",
        "/{slug}/offers/{id:guid}/reject",
        "/{slug}/trips/{listingId:guid}/arrived",
        "/{slug}/trips/{listingId:guid}/complete",
        "/{slug}/trips/{listingId:guid}/abort",
        "/{slug}/offers/{id:guid}/withdraw",
        "/api/{slug}/manifest.json",
        "/api/{slug}/r/{role}/manifest.json",
        "/api/{slug}/icon.svg",
        "/api/{slug}/r/{role}/icon.svg",
        "/api/{slug}/og.png",
        "/api/{slug}/push/subscribe",
        "/api/{slug}/unread-counts",
        "/{slug}/me/area/save",
        "/{slug}/users/{userId:guid}/chat",
        "/{slug}/chats/{conversationId:guid}/send",
        "/admin/tenants/create",
        "/admin/tenants/{slug}/users/{userId:guid}/grant-admin",
        "/admin/tenants/{slug}/users/{userId:guid}/revoke-admin",
        "/admin/tenants/{slug}/roles/save",
        "/admin/tenants/{slug}/roles/definitions/{roleSlug}/decide",
        "/admin/tenants/{slug}/theme/propose",
        "/admin/tenants/{slug}/theme/{themeSlug}/decide",
        "/admin/tenants/{slug}/theme/apply",
        "/admin/tenants/{slug}/categories/save",
        "/admin/tenants/{slug}/branding/save",
        "/admin/tenants/{slug}/pwa/save",
        "/admin/tenants/{slug}/regions/save",
        "/admin/tenants/{slug}/attributes/save",
        "/admin/agent/ask",
        "/admin/agent/tool/{toolId}/apply",
        "/admin/agent/tool/{toolId}/reject",
        "/admin/agent/reset",
        "/studio/auth/verify",
        "/studio/consent/accept",
        "/studio/s/{id:guid}/feedback",
        "/studio/billing/select",
        "/{slug}/listings/{id:guid}/cart/add",
        "/{slug}/cart/{listingId:guid}/qty",
        "/{slug}/cart/clear",
        "/{slug}/checkout/submit",
        "/{slug}/vendor/{vendorId:guid}/chat",
        "/studio/apps/{slug}/deals/{id:guid}/review",
        "/studio/apps/{slug}/listings/{id:guid}/moderate",
        "/studio/apps/{slug}/tickets/{id:guid}/reply",
        "/studio/apps/{slug}/tickets/{id:guid}/close",
        "/{slug}/listings/{id:guid}/deal",
        "/{slug}/deals/{id:guid}/accept",
        "/{slug}/deals/{id:guid}/advance",
        "/{slug}/deals/{id:guid}/cancel",
        "/{slug}/deals/{id:guid}/review",
        "/admin/tenants/{slug}/suspend",
        "/studio/apps/{slug}/deals/seed",
        "/studio/apps/{slug}/deals/{id:guid}/advance",
        "/studio/apps/{slug}/deals/{id:guid}/cancel",
        "/studio/apps/{slug}/deals/{id:guid}/dispute",
        "/studio/apps/{slug}/branding/save",
        "/studio/apps/{slug}/categories/save",
        "/studio/apps/{slug}/roles/save",
        "/studio/apps/{slug}/regions/save",
        "/studio/apps/{slug}/pwa/save",
        "/studio/apps/{slug}/attributes/save",
        "/admin/incubator/start",
        "/admin/incubator/{id:guid}/answer",
        "/admin/incubator/{id:guid}/analyze",
        "/admin/incubator/restart",
    };
}
