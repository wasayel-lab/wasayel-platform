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

        // ─── المَوجَة ٤: سَطرانِ يُضافانِ بِقَرارٍ مَرئيّ، وهذا سَبَبُهُما ──
        // نُقطَتا تَحريرِ الإعلان وحَذفِه. وأَخذُهُما المَخزَنَ **لَيسَ
        // إهمالاً بَل الشَكل الوَحيد المُمكِن اليَوم**: حَقنُ
        // `IDocumentSession` في نُقطَة Minimal API يُعطي جَلسَةً بِلا
        // مُستَأجِر — الرَبطُ المَقيس في `LiveOutboxTenantProofTests`
        // يَقَع على نِقاط **Wolverine.Http** عَبر
        // `opts.TenantId.IsRouteArgumentNamed("slug")`، ولا يَمُرّ بِـ
        // Minimal API. ووَثائِقُنا كُلُّها `AllDocumentsAreMultiTenanted`،
        // فَجَلسَةٌ بِلا سلاج تَكتُب في `*DEFAULT*` صامِتَةً.
        //
        // والمَنطِقُ خَرَجَ رَغمَ ذلك: `ListingEditService` تَأخُذ
        // الجَلسَةَ ولا تَفتَحُها ولا تُودِع، فَما بَقِيَ في الجِسم
        // فَتحُ الجَلسَة وقِراءَةُ النَموذَج والعَرض. ويَومَ تُرَحَّل
        // النِقاطُ إلى Wolverine.Http يُرفَع السَطرانِ بِلا لَمس
        // الخِدمَة.
        "/{slug}/listings/{id:guid}/edit",
        "/{slug}/listings/{id:guid}/delete",
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

        // ─── المَوجَة ٧: نُقطَةٌ تُضاف بِقَرارٍ مَرئيّ، وهذا سَبَبُها ────
        // تَصفيرُ عَدّاد غَير المَقروء كانَ يَقَع في `ChatRoom.razor`
        // داخِلَ طَلَب `GET` — كِتابَةٌ في مَسار عَرض، يَرفَعُها الزاحِفُ
        // والجالِبُ المُسبَق وأَداةُ التَحَقُّق. فَخَرَجَ إلى أَمرٍ صَريح.
        // **والصافي نَقص لا زيادَة**: خَرَجَت كِتابَةٌ مِن خارِج
        // المُعامَلَة (سِجِلُّ الطَبَقَة ٨) ودَخَلَت نُقطَةً مَحروسَة،
        // وثَمَنُها سَطرٌ هُنا. والشَكلُ المِثاليّ — نُقطَةُ
        // Wolverine.Http بِجَلسَةٍ مَحقونَة — قَرارٌ لَه ADR ومَوجَة.
        "/{slug}/chats/{conversationId:guid}/read",
        "/{slug}/notifications/read",
        "/{slug}/listings/{id:guid}/view",
        "/admin/tenants/create",
        "/admin/tenants/{slug}/users/{userId:guid}/grant-admin",
        "/admin/tenants/{slug}/users/{userId:guid}/revoke-admin",
        "/admin/tenants/{slug}/roles/save",
        "/admin/tenants/{slug}/roles/definitions/{roleSlug}/decide",

        // ─── سَطرٌ رُفِع بِقَرارٍ مَرئيّ (‏2026-08-23) ──────────────────
        // كانَ هُنا `/admin/tenants/{slug}/subscriptions/{reference}/decide`
        // — نُقطَةُ قَرارِ طَلَبِ اشتِراكٍ في باقَةٍ بِسِعر. حُذِفَت مَعَ
        // الدَورَة كُلِّها: قَرارُ المالِك «لا تَسمَح لِلتاجِر بِاستِلام
        // حَوالات»، فَالباقَةُ المَدفوعَةُ لِمُستَخدِمِ المَتجَرِ لا
        // تُباع أَصلاً، ولا طَلَبَ يُقَرَّر. والمَسارُ يُرفَع مِن هُنا
        // لِأَنّ هذا المِلَفَّ يُثَبِّت **ما هُوَ قائِم**؛ ومَسارٌ
        // مُثَبَّتٌ لا وُجودَ لَه يُحمِرّ بِحَقّ.
        // ─── ثَلاثَةٌ تُضاف بِقَرارٍ مَرئيّ (‏2026-08-23، ‏ADR-003) ─────
        // باقَةُ المُستَأجِر وإعداداتُ المَنَصَّة **وَثيقَتانِ
        // `SingleTenanted`**، ونُقاطُهُما تَسأَل `PlatformAdminGuard`
        // أَوَّلاً — وهُوَ يَأخُذ `IDocumentStore` بِتَوقيعِه. فَحَقنُ
        // `IDocumentSession` كانَ سَيُعطي جَلسَةً ثانِيَةً إلى جِوارِ
        // جَلسَةِ الحارِس لا بَدَلاً مِنها. نَفسُ شَكلِ كُلّ نُقاط
        // `/admin/…` قَبلَها بِلا استِثناء.
        "/admin/tenants/{slug}/plan/save",
        "/admin/tenants/{slug}/plan/status",
        "/admin/transfer/save",
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

        // ─── مَوجَةُ سَطح الـAPI: سَطرانِ يُضافانِ بِقَرارٍ مَرئيّ ─────
        //
        // نُقطَتا إصدارِ مِفتاح الـAPI وإبطالِه مِن الاستوديو.
        // وأَخذُهُما المَخزَنَ لَه سَبَبٌ واحِدٌ مَقيس: **حارِسُ
        // المِلكِيَّة نَفسُه** — `StudioOwnsAsync(store, auth, slug)`
        // — يَقبَل `IDocumentStore`، ويَقرَأُ بِه وَثيقَةَ `Tenant`
        // وهي `SingleTenanted` أَي بِجَلسَةٍ **بِلا سلاج**. وهذا
        // بِعَينِه ما يَفعَلُه كُلُّ إخوَتِهِما الاثنَي عَشَرَ تَحتَ
        // `/studio/apps/{slug}/…` أَعلاه.
        //
        // **والبَديلُ كانَ أَسوَأَ ومَقيس**: تَعريفٌ ثانٍ لِلمِلكِيَّة
        // يَقرَأُ المُستَأجِرَ مِن خِدمَةِ استِعلام — أَي **قَرارُ
        // تَخويلٍ بِتَعريفَين** يَنجَرِفان، وهو عَينُ العَطَب الَّذي
        // كَتَبَ القاعِدَة ٨. فَالسَطرانِ هُنا ثَمَنُ **تَعريفٍ
        // واحِد**، ويُرفَعانِ يَومَ يُرَحَّل الاثنا عَشَرَ مَعاً.
        //
        // **ولا يَمَسُّ هذا سَطحَ الـAPI**: نِقاطُ `/api/v1/…`
        // الأَربَع **لا تَذكُر المَخزَنَ ولا الجَلسَة** — وذاكَ
        // مَفروضٌ بِفاحِصٍ ساكِنٍ لا بِنِيَّة (الطَبَقَة ٩).
        "/studio/apps/{slug}/keys/create",
        "/studio/apps/{slug}/keys/{keyId}/revoke",
    };
}
