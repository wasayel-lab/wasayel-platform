using ACommerce.Kit.Auth;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Offers;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Support;
using ACommerce.Kit.Tenants;
using ACommerce.Kit.Theme;
using ACommerce.Platform.Shared;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Listings;
using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using Marten;
using Npgsql;

namespace ACommerce.V1.App.Seed;

/// <summary>
/// <para><b>بَذرَةٌ مُخَصَّصَةٌ لِلبَوّابَة البايتيَّة — لا لِلمُنتَج.</b>
/// لَقطَةُ الأَساس <c>tests/characterization/appearance/i18n-baseline</c>
/// مُثَبَّتَةٌ على ‏128 صَفحَة، و‏<b>‏32 مِنها تَفتَرِض مُستَأجِرَين لا
/// وُجودَ لَهُما في الإنتاج ولا في هذا الفَرع</b>: <c>theme-demo</c>
/// (مَتجَرُ بُرهانِ الهُوِيَّة البَصَرِيَّة) و<c>owner-test</c> (مَتجَرُ
/// المِلكِيَّة). فَما كانَ يَفشَل على <c>HEAD</c> نَفسِه كانَ يَفشَل
/// <b>بِالبَيانات لا بِالكود</b> — وأَداةُ التَحَقُّق الَّتي تَتَّهِم
/// الشيفرَةَ بِنَقصِ بيئَةٍ أَداةٌ تَكذِب (القاعِدَة ١٠).</para>
///
/// <para><b>ولِماذا بَذرَةٌ ثالِثَةٌ ولَيسَت سَطراً في
/// <see cref="TestDataSeeder"/></b>: تِلكَ تَكتُب في المُستَأجِرينَ
/// <b>الحَقيقِيّين</b> (<c>ashare</c>, <c>ejar</c>, <c>order</c>) —
/// وأَحَدُهُم عَرضُ المُستَثمِرين. وهذِه تَكتُب في مُستَأجِرَي اللَقطَة
/// وَحدَهُما، و<b>لا تَمَسّ صَفّاً واحِداً</b> خارِجَهُما. والدَعوى
/// مَقيسَةٌ لا مَوعودَة: <see cref="SnapshotAsync"/> يَعُدّ كُلّ جَدوَل
/// <c>mt_doc_*</c> بِمُستَأجِرِه قَبلَ البَذرِ وبَعدَه، ويَرمي إن
/// تَحَرَّكَ عَدّادُ مُستَأجِرٍ آخَر.</para>
///
/// <para><b>وكُلُّ قيمَةٍ هُنا حَتمِيَّة</b>: مُعَرِّفاتٌ ثابِتَة،
/// وطَوابِعُ زَمَنٍ ثابِتَة، وأَسماءُ نَفاذٍ <b>مُشتَقَّةٌ</b> مِن رَقم
/// الهُوِيَّة (<see cref="NafathNames"/>) لا مَكتوبَة. فَإقلاعانِ
/// مُتَتالِيانِ يُعطِيانِ نَفسَ البايتات — وهذا شَرطُ بَوّابَةٍ تُقارِن
/// بِـ<c>cmp</c> لا بِالعَين. ولا <c>DateTime.UtcNow</c> في هذا المِلَفّ
/// إطلاقاً؛ نَفسُ سَبَبِ <see cref="IncubatorSampleSeeder"/> و
/// <see cref="CartSampleSeeder"/>.</para>
///
/// <code>
/// export APPEARANCE_BASELINE_SEED=1
/// dotnet run --project apps/V1.App --urls=http://localhost:5050
/// </code>
///
/// <para><b>وخارِجَ التَطوير تَرفُض صَراحَةً</b> — لا تَصمُت. مُستَأجِرا
/// عَيِّنَةٍ في قاعِدَةِ إنتاجٍ يَظهَرانِ في صَفحَةِ الهُبوط لِكُلّ
/// زائِر، فَالصَمتُ هُنا أَسوَأُ مِن الاستِثناء.</para>
///
/// <para><b>وحَذفُها سَطران</b>:
/// <c>delete from platform.mt_doc_tenant where id in ('theme-demo','owner-test')</c>
/// ثُمَّ حَذفُ صُفوفِ المُستَأجِرَينِ مِن بَقِيَّةِ الجَداوِل بِـ
/// <c>tenant_id in (…)</c>.</para>
/// </summary>
public static class AppearanceBaselineSeeder
{
    public const string EnvVar = "APPEARANCE_BASELINE_SEED";

    /// <summary>المُستَأجِرانِ المَأذونُ الكِتابَةُ فيهِما — وما عَداهُما
    /// يُقاس ويُشتَرَط ثَباتُه.</summary>
    public const string ThemeDemo = "theme-demo";
    public const string OwnerTest = "owner-test";

    // ── طَوابِعُ زَمَنٍ ثابِتَة (كَما في اللَقطَة المُثَبَّتَة) ────────
    private static readonly DateTime ThemeDemoCreatedAt = new(2026, 8, 11, 19, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OwnerTestCreatedAt = new(2026, 8, 16, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ThemeDocsWrittenAt = new(2026, 8, 11, 20, 11, 0, DateTimeKind.Utc);

    // ── مُعَرِّفاتٌ ثابِتَة ───────────────────────────────────────────
    //
    // ‏`ff8e1748…` و`94877b94…` و`1d2e3d6e…` و`e70fc5cb…` **لَيسَت
    // مُختارَةً هُنا**: هي ما تَحمِلُه لَقطَةُ الأَساس نَفسُها في
    // ‏`user-listing-own.html` و`theme-demo-listing-offers.html` وعَناوينِ
    // ‏`capture-appearance.sh`. فَالبَذرَةُ تُعيدُ إنتاجَ ما ثُبِّتَ، لا
    // تَختَرِعُ بَديلاً يُجبِرُ اللَقطَةَ على التَحَرُّك.
    public static readonly Guid ListingOwnedByCaptureUser =
        Guid.Parse("ff8e1748-b98a-4ad0-9da0-10ac616eaf9e");
    public static readonly Guid ListingAcceptingOffers =
        Guid.Parse("94877b94-abb5-4a8b-8494-be8ef678cc59");

    /// <summary>صاحِبُ جَلسَةِ مِلَفّ <c>user</c> في أَداةِ اللَقطَة —
    /// ومالِكُ <see cref="ListingOwnedByCaptureUser"/>.</summary>
    public static readonly Guid CaptureUserId =
        Guid.Parse("1d2e3d6e-0e10-4875-96e8-52136c4896b6");

    /// <summary>هُوِيَّةُ نَفاذِ ذلك المِلَفّ — تُصَدَّر لِتُوضَع في
    /// <c>WSL_CAPTURE_USER_NID</c>. لَيسَت سِرّاً: مُحاكي نَفاذ يوافِق
    /// أَيَّ رَقمٍ في التَطوير.</summary>
    public const string CaptureUserNationalId = "1052001001";

    /// <summary>مالِكُ الإعلانِ المَفتوحِ لِلعُروض — طَرَفٌ آخَر عَمداً،
    /// فَيُصَيَّر فَرعُ «قَدِّم عَرضاً» لِمِلَفّ <c>user</c> بَدَلَ فَرعِ
    /// المالِك.</summary>
    public static readonly Guid OffersListingOwnerId =
        Guid.Parse("e70fc5cb-06fa-4827-ba55-1f5244dac97f");

    private const string OffersListingOwnerNid = "1052001002";

    /// <summary>تَذكِرَةُ <c>owner-test</c> الوَحيدَة. <b>وَثيقَةٌ بِلا
    /// مَجرى أَحداث عَمداً</b> — وهذا بِعَينِه ما تُثَبِّتُه اللَقطَة:
    /// ‏`studio-app-tickets` يَسرُدُها (يَقرَأ الوَثيقَة)، و
    /// ‏`studio-app-ticket-missing` يَقول «التَذكَرَة غَير مَوجودَة»
    /// لِنَفسِ المُعَرِّف (يَقرَأ المَجرى). الصَفحَتانِ طَرَفا فَرقٍ
    /// حَقيقيّ في الشيفرَة، ولَو بُذِرَ المَجرى لَاختَفى أَحَدُ
    /// الطَرَفَين.</summary>
    public static readonly Guid OwnerTestTicketId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static readonly Guid OwnerTestTicketReplyId =
        Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static readonly Guid ThemeDemoOfferId =
        Guid.Parse("5a3e1d00-0000-4000-8000-00000000f001");

    /// <summary>هُوِيّاتُ مُستَخدِمي <c>owner-test</c> الثَلاثَة — كَما
    /// تَطبَعُها <c>admin-tenant-users.html</c> حَرفاً
    /// (<c>NID-1000000001</c>…). والأَسماءُ لا تُكتَب: يَشتَقُّها
    /// <see cref="NafathNames"/> مِن هذِه الأَرقامِ نَفسِها.</summary>
    private static readonly (string Nid, Guid Id, string ActiveRole)[] OwnerTestUsers =
    {
        ("1000000001", Guid.Parse("5a3e1d00-0000-4000-8000-00000000a001"), "customer"),
        ("1033333333", Guid.Parse("5a3e1d00-0000-4000-8000-00000000a002"), ""),
        ("1011111111", Guid.Parse("5a3e1d00-0000-4000-8000-00000000a003"), ""),
    };

    /// <summary>ثَمانِيَةُ مُستَخدِمين — العَدَدُ الَّذي تَطبَعُه
    /// <c>user-manage.html</c> («‏8 مُستَخدِم»). الأَوَّلانِ مُعَرَّفانِ
    /// بِاللَقطَة، والسِتَّةُ الباقونَ حَشوُ عَدَدٍ لا يُعرَضُ اسمُ
    /// أَحَدِهِم في أَيّ صَفحَةٍ مُلتَقَطَة.</summary>
    private static readonly (string Nid, Guid Id)[] ThemeDemoUsers = new[]
    {
        (CaptureUserNationalId,  CaptureUserId),
        (OffersListingOwnerNid,  OffersListingOwnerId),
        ("1052001003", Guid.Parse("5a3e1d00-0000-4000-8000-00000000b003")),
        ("1052001004", Guid.Parse("5a3e1d00-0000-4000-8000-00000000b004")),
        ("1052001005", Guid.Parse("5a3e1d00-0000-4000-8000-00000000b005")),
        ("1052001006", Guid.Parse("5a3e1d00-0000-4000-8000-00000000b006")),
        ("1052001007", Guid.Parse("5a3e1d00-0000-4000-8000-00000000b007")),
        ("1052001008", Guid.Parse("5a3e1d00-0000-4000-8000-00000000b008")),
    };

    /// <summary>الحُزَمُ الثَلاثُ بِتَرتيبِ تَطبيقِها — والأَخيرَةُ هي
    /// المَبثوثَة، لِأَنّ <see cref="TenantThemeSet.FromDocuments"/>
    /// يُغَلِّبُ آخِرَ قَرار. والثَواني (‏08/10/12) مِن اللَقطَة
    /// نَفسِها.</summary>
    private static readonly (string Slug, int DecidedSecond)[] ThemePacks =
    {
        ("layl_ramliy", 8),
        ("azraq_iftiradi", 10),
        ("akhdar_alwaha", 12),
    };

    /// <summary>كاتِبُ وَثائِقِ الثيم كَما تَطبَعُها الصَفحَة. نَصٌّ
    /// مُخَزَّنٌ لا مُستَخدِمٌ يُبحَث عَنه — ‏`0599999999` كانَ هاتِفَ
    /// مُشرِفِ المَنصَّة يَومَ ثُبِّتَت اللَقطَة، ولا وُجودَ لَه اليَوم.
    /// وإعادَةُ كِتابَتِه هُنا أَصدَقُ مِن تَزويرِ حاضِرٍ بِاسمِ مُشرِفٍ
    /// آخَر.</summary>
    private const string ThemeDocsAuthor = "صاحِب المَشروع · 0599999999";

    // ═══════════════════════════════════════════════════════════════════

    public static async Task RunAsync(IServiceProvider services, IWebHostEnvironment env)
    {
        if (!env.IsDevelopment())
            throw new InvalidOperationException(
                $"{EnvVar}=1 خارِجَ بيئَة التَطوير — مَرفوض. " +
                "هذِه بَذرَةُ لَقطَةٍ تُنشِئ مُستَأجِرَي عَيِّنَةٍ يَظهَرانِ " +
                "لِكُلّ زائِر في صَفحَة الهُبوط.");

        var store = services.GetRequiredService<IDocumentStore>();
        var connStr = services.GetRequiredService<IConfiguration>()
                          .GetConnectionString("Postgres")
                      ?? throw new InvalidOperationException("Postgres connection string missing");

        var before = await SnapshotAsync(connStr);

        await SeedThemeDemoAsync(store);
        await SeedOwnerTestAsync(store);

        var after = await SnapshotAsync(connStr);
        var outsiders = AssertOnlyBaselineTenantsMoved(before, after);

        Console.WriteLine("[AppearanceSeed] ✅ مُستَأجِرا اللَقطَة جاهِزان — " +
                          $"وقيسَ {outsiders} عَدّاداً لِمُستَأجِرين آخَرين، " +
                          "لَم يَتَحَرَّك مِنها واحِد.");
        Console.WriteLine($"[AppearanceSeed] WSL_CAPTURE_USER_NID={CaptureUserNationalId} " +
                          $"WSL_CAPTURE_USER_TENANT={ThemeDemo}");
    }

    // ── theme-demo ────────────────────────────────────────────────────

    private static async Task SeedThemeDemoAsync(IDocumentStore store)
    {
        await using var global = store.LightweightSession();
        var tenant = await global.LoadAsync<Tenant>(ThemeDemo);
        if (tenant is null)
        {
            global.Store(new Tenant
            {
                Id         = ThemeDemo,
                Name       = "مَتجَر التَّجرِبَة",
                BrandColor = "#1D4ED8",
                City       = "الرِّياض",
                TagLine    = "بُرهان الهُوِيَّة البَصَرِيَّة",
                // ‏نَفاذ — وهي القَناةُ الَّتي يَدخُل بِها مِلَفُّ `user`
                // في أَداةِ اللَقطَة، وبِها وَحدَها يُصَيَّر
                // ‏`NafathLoginForm` في `theme-demo-login*.html`.
                AuthChannel = "nafath",
                // ‏**بِلا أَدوار عَمداً**: `RolePermissions.Has` تُعيد
                // ‏`true` لِمُستَأجِرٍ بِصِفر دَور، وعَلَيه يَقوم فَرعُ
                // «لَوحَة الإداريّ» في `user-manage.html` — وهو الفَرعُ
                // المُقابِلُ لِـ«لا صَلاحِيَّة» في `member-manage.html`
                // على `ejar` ذي الأَدوار.
                Roles      = new(),
                // ‏فِئَتانِ بِـ`Kind` واحِد (فارِغ) — فَيُصَيَّر فَرعُ
                // الشَبَكَة المُسَطَّحَة لا فَرعُ الشَجَرَة، ويُعطي
                // ‏`PatternProfileResolver.PatternOf` نَمَطَ
                // ‏`Marketplace` (سَلَّة + هيرو «اطلُب مِن مَقاهيكَ
                // المُفَضَّلَة») كَما في اللَقطَة.
                Categories = new()
                {
                    new Category { Slug = "general",  Label = "عام",      Icon = "🏠", Kind = "", SortOrder = 0 },
                    new Category { Slug = "services", Label = "خَدَمات", Icon = "🏠", Kind = "", SortOrder = 1 },
                },
                CreatedAt = ThemeDemoCreatedAt,
            });
            await global.SaveChangesAsync();
            Console.WriteLine($"[AppearanceSeed] أُنشِئَ المُستَأجِر «{ThemeDemo}».");
        }

        await EnsureOwnershipAsync(store, ThemeDemo);
        await EnsureThemePacksAsync(store, ThemeDemo);

        await using var s = store.LightweightSession(ThemeDemo);

        foreach (var (nid, id) in ThemeDemoUsers)
            await EnsureNafathUserAsync(s, ThemeDemo, id, nid, activeRole: "");

        // ‏`ff8e1748…` — إعلانُ مِلَفّ `user`. عُنوانُه ووَصفُه
        // «الأَخير» لِأَنّ اللَقطَةَ ثَبَّتَت الحالَةَ بَعدَ تَحريرٍ
        // بِالوَكيل؛ يُبذَر مُحَرَّراً لا مُنشَأً ثُمَّ مُحَرَّراً —
        // فَالمَقروءُ هو التَجميعَة لا سِجِلُّ خُطُواتِها.
        await EnsureListingAsync(s, ThemeDemo, new ListingSeed(
            Id: ListingOwnedByCaptureUser,
            Title: "العُنوانُ الأَخير",
            Description: "وَصفٌ أَخير",
            Price: 2400m,
            Category: "general",
            City: "جُدَّة",
            District: "الحَمراء",
            Attributes: new() { [ListingEditService.OwnerAttribute] = CaptureUserId.ToString() },
            CreatedAt: new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc),
            Views: 18));

        // ‏`94877b94…` — مَفتوحٌ لِلعُروض. و`accepts_offers=true`
        // يُخرِجُه مِن واجِهَةِ المَتجَر بِـ
        // ‏`StorefrontQueries.IsTripRequest` — ولِذلك تَعرِض
        // ‏`theme-demo-portal.html` بِطاقَةً واحِدَة بَينَما تَعُدّ
        // ‏`user-manage.html` إعلانَين. الفَرقُ سُلوكُ شيفرَةٍ قائِم، لا
        // نَقصُ بَذر.
        await EnsureListingAsync(s, ThemeDemo, new ListingSeed(
            Id: ListingAcceptingOffers,
            Title: "تَوصيلَة اختِبار العُروض",
            Description: "إعلان لِبُرهان دَورَة العُروض",
            Price: 0m,
            Category: "general",
            City: "الرِياض",
            District: "",
            Attributes: new()
            {
                [ListingEditService.OwnerAttribute]         = OffersListingOwnerId.ToString(),
                [ListingEditService.AcceptsOffersAttribute] = "true",
            },
            CreatedAt: new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc),
            Views: 2));

        // عَرضٌ واحِدٌ مُعَلَّق — «‏1 عَرض قَيد المُراجَعَة» في
        // ‏`user-manage.html`.
        //
        // **وعَلى `ff8e1748…` لا على المَفتوحِ لِلعُروض، وذلك مَقيس**:
        // ‏`TenantListingDetail` لا يُصَيِّر قائِمَةَ العُروضِ إلّا
        // لِإعلانٍ بِـ`accepts_offers`، ولَقطَةُ الأَساس تُظهِر تِلكَ
        // القائِمَةَ **فارِغَة** في `theme-demo-listing-offers.html` و
        // ‏`user-listing-offers.html` بَينَما تَعُدّ `user-manage.html`
        // عَرضاً واحِداً. فَالعَرضُ المُثَبَّتُ لَيسَ عَلى ذلك الإعلان.
        // (جُرِّبَ العَكسُ أَوَّلاً فَأَحمَرَّت الصَفحَتان — والقِياسُ
        // هُوَ الَّذي نَقَلَه، لا التَخمين.)
        if (await s.Events.FetchStreamStateAsync(ThemeDemoOfferId) is null)
        {
            var at = new DateTime(2026, 8, 16, 9, 0, 0, DateTimeKind.Utc);
            s.Events.StartStream<Offer>(ThemeDemoOfferId, new OfferSubmitted(
                Id: ThemeDemoOfferId,
                ListingId: ListingOwnedByCaptureUser,
                OffererId: ThemeDemoUsers[2].Id,
                OffererName: NafathNames.For(ThemeDemoUsers[2].Nid),
                Price: 0m,
                Message: null,
                Lat: 0, Lng: 0,
                ExpiresAt: at.AddDays(365),
                At: at));
        }

        await s.SaveChangesAsync();

        await EnsureRegionsAsync(store, ThemeDemo, "الرِياض\nجُدَّة");
    }

    // ── owner-test ────────────────────────────────────────────────────

    private static async Task SeedOwnerTestAsync(IDocumentStore store)
    {
        await using var global = store.LightweightSession();
        var tenant = await global.LoadAsync<Tenant>(OwnerTest);
        if (tenant is null)
        {
            global.Store(new Tenant
            {
                Id         = OwnerTest,
                Name       = "مَتجَر المِلكِيَّة",
                BrandColor = "#1D4ED8",
                // ‏بِلا مَدينَةٍ ولا سَطرِ تَعريف — و`studio-app.html`
                // تَطبَع «‏—» و«بِلا شِعار» لِذلك بِعَينِه.
                City       = "",
                TagLine    = "",
                AuthChannel = "nafath",
                Categories = new()
                {
                    new Category { Slug = "general", Label = "عام", Icon = "🏠", Kind = "", SortOrder = 0 },
                },
                // أَربَعَةُ أَدوار — وهي الَّتي تَسرُدُها
                // ‏`studio-app-pwa.html` بِمَساراتِها
                // (`/owner-test/r/customer/` …).
                Roles = new[] { "customer", "vendor", "broker", "organizer" }
                    .Select((slug, i) =>
                    {
                        var t = RoleCatalog.Find(slug);
                        if (t is null) return null;
                        var role = RoleCatalog.InstantiateRole(t, sortOrder: i);
                        // ‏«افتراضي» عَلى `customer` — الشِعارُ الَّذي
                        // تَطبَعُه `admin-tenant-roles.html` ويَختارُه
                        // زِرُّ الراديو المُؤَشَّر.
                        role.IsDefault = slug == "customer";
                        return role;
                    })
                    .OfType<Role>()
                    .ToList(),
                CreatedAt = OwnerTestCreatedAt,
            });
            await global.SaveChangesAsync();
            Console.WriteLine($"[AppearanceSeed] أُنشِئَ المُستَأجِر «{OwnerTest}».");
        }

        await EnsureOwnershipAsync(store, OwnerTest);

        await using var s = store.LightweightSession(OwnerTest);

        foreach (var (nid, id, role) in OwnerTestUsers)
            await EnsureNafathUserAsync(s, OwnerTest, id, nid, role);

        if (await s.LoadAsync<Ticket>(OwnerTestTicketId) is null)
        {
            // ‏`CreatedAt` = `default` قَصداً: اللَقطَةُ تَطبَع
            // ‏«‏0001-01-01 00:00». وَثيقَةٌ تُخَزَّن مُباشَرَةً بِلا
            // مَجرى — راجِع <see cref="OwnerTestTicketId"/>.
            s.Store(new Ticket
            {
                Id         = OwnerTestTicketId,
                AuthorId   = OwnerTestUsers[0].Id,
                AuthorName = "",
                Subject    = "",
                Body       = "",
                Status     = "open",
                CreatedAt  = default,
                UpdatedAt  = default,
                Replies =
                {
                    new Reply
                    {
                        Id = OwnerTestTicketReplyId,
                        AuthorName = "",
                        FromStaff = false,
                        Body = "",
                        At = default,
                    }
                }
            });
        }

        await EnsureAuthoredRolesAsync(s);

        await s.SaveChangesAsync();

        await EnsureRegionsAsync(store, OwnerTest, "الرِياض > العُلَيا، النَخيل\nجُدَّة");
    }

    // ── لَبِنات مُشتَرَكَة ────────────────────────────────────────────

    /// <summary>
    /// <para>وَثيقَتا دَورٍ <b>مُؤَلَّفَتان</b> في <c>owner-test</c> —
    /// وهُما ما تَسرُدُه <c>admin-tenant-roles.html</c> تَحتَ «أَدوار
    /// مُؤَلَّفَة لِهذا المَتجَر (‏2)». إحداهُما <b>مُعتَمَدَة</b>
    /// والأُخرى <b>مَرفوضَة</b>، والزَوجُ مَقصود: الحالَتانِ فَرعانِ
    /// مُختَلِفانِ في الشاشَة، وواحِدَةٌ مِنهُما تُصَيِّرُ نِصفَ
    /// القِسم.</para>
    ///
    /// <para><b>ولا تُضافانِ إلى <c>Tenant.Roles</c></b>: اللَقطَةُ
    /// تَقول «المَفعَّلَة حاليّاً (‏4)» و«‏4 أَدوار» في
    /// <c>admin-home</c> و«‏13 دَوراً» في صَفحَة الهُبوط — أَي أَنّ
    /// المُؤَلَّفَ يُسرَد ولا يُفَعَّل. الفَصلُ في الشيفرَة قائِمٌ
    /// أَصلاً؛ هذا السَطرُ يَذكُرُه لِئَلّا يُظَنَّ سَهواً.</para>
    /// </summary>
    private static async Task EnsureAuthoredRolesAsync(IDocumentSession s)
    {
        var docs = new (string Slug, string Status, string By, DateTime At, string DecidedBy)[]
        {
            ("mandoob", TenantRoleStatuses.Approved, "proof-harness",
                new DateTime(2026, 8, 11, 14, 54, 0, DateTimeKind.Utc), "proof-harness"),
            ("khayyat", TenantRoleStatuses.Rejected, "agent:define_role",
                new DateTime(2026, 8, 15, 15, 50, 0, DateTimeKind.Utc), "live-proof-cleanup"),
        };

        foreach (var (slug, status, by, at, decidedBy) in docs)
        {
            if (await s.LoadAsync<TenantRoleDefinition>(slug) is not null) continue;
            s.Store(new TenantRoleDefinition
            {
                Id             = slug,
                Slug           = slug,
                DefinitionJson = AuthoredRoleJson(slug),
                Status         = status,
                CreatedBy      = by,
                CreatedAt      = at,
                DecidedBy      = decidedBy,
                DecidedAt      = at,
            });
        }
    }

    /// <summary>أَقَلُّ تَعريفٍ يَجتازُ
    /// <c>RoleDefinitionLoader.ParseDefinition</c> — نَفسُ شَكلِ
    /// <c>Definitions/*.role.json</c> المَضمونَة.</summary>
    private static string AuthoredRoleJson(string slug) => $$"""
        {
          "slug": "{{slug}}",
          "icon": "🧾",
          "homeRoute": "",
          "label": { "ar": "{{slug}}", "en": null },
          "description": { "ar": "دَورٌ مُؤَلَّفٌ — عَيِّنَةُ بِنيَة.", "en": null },
          "permissions": [ "listing.browse" ],
          "fields": [],
          "composition": {
            "home": "defaultHome",
            "createListing": "defaultCreateForm",
            "nav": "defaultNav",
            "explore": "defaultExplore",
            "publicProfile": null,
            "extras": []
          },
          "dealPatternAffinity": null
        }
        """;

    private sealed record ListingSeed(
        Guid Id, string Title, string Description, decimal Price,
        string Category, string City, string District,
        Dictionary<string, string> Attributes, DateTime CreatedAt, int Views);

    private static async Task EnsureListingAsync(
        IDocumentSession s, string slug, ListingSeed l)
    {
        if (await s.Events.FetchStreamStateAsync(l.Id) is not null) return;

        var events = new List<object>
        {
            new ListingCreated(l.Id, slug, l.Title, l.Description, l.Price,
                l.Category, l.City, l.District, l.Attributes, l.CreatedAt)
        };
        // عَدّادُ المُشاهَدَة حالَةٌ مَبنِيَّةٌ مِن أَحداث
        // (<c>Apply(ListingViewed)</c> يَزيدُ واحِداً)، فَالرَقمُ
        // المَطبوعُ في اللَقطَة يُعادُ بِعَدَدِ الأَحداثِ نَفسِه لا
        // بِكِتابَةِ حَقل.
        for (var i = 0; i < l.Views; i++)
            events.Add(new ListingViewed(l.Id, null, l.CreatedAt.AddMinutes(i + 1)));

        s.Events.StartStream<Listing>(l.Id, events.ToArray());
    }

    private static async Task EnsureNafathUserAsync(
        IDocumentSession s, string slug, Guid id, string nid, string activeRole)
    {
        if (await s.LoadAsync<User>(id) is not null) return;
        // نَفسُ شَكلِ ما يَكتُبُه <c>AuthHandlers.GetOrCreateUserAsync</c>
        // لِداخِلٍ بِنَفاذ حَرفاً: الهاتِفُ `NID-{nid}`، والاسمُ
        // مُشتَقٌّ لا مَكتوب. فَدُخولُ مِلَفّ `user` لاحِقاً يَجِدُ هذا
        // المُستَخدِمَ ولا يُنشِئُ ثانِياً.
        s.Store(new User
        {
            Id         = id,
            TenantSlug = slug,
            Phone      = $"NID-{nid}",
            NationalId = nid,
            FullName   = NafathNames.For(nid),
            ActiveRole = activeRole,
            CreatedAt  = ThemeDemoCreatedAt,
            UpdatedAt  = ThemeDemoCreatedAt,
        });
    }

    /// <summary>يُسنِدُ المُستَأجِرَ إلى صاحِبِ
    /// <c>PLATFORM_ADMIN_PHONE</c>، وإلّا إلى أَقدَمِ مُستَخدِمِ
    /// استوديو — نَفسُ اختِيارِ <see cref="IncubatorSampleSeeder"/>.
    /// وبِلا مالِكٍ صَريح كانَ <c>StudioOwnershipSeeder</c> سَيَتَبَنّاه
    /// عِندَ أَوَّلِ دُخول، فَيَختَلِفُ المالِكُ بِاختِلافِ تَرتيبِ
    /// الجَلَسات.</summary>
    private static async Task EnsureOwnershipAsync(IDocumentStore store, string slug)
    {
        await using var qs = store.QuerySession(StudioAuth.Tenant);
        var adminPhone = Environment.GetEnvironmentVariable(PlatformAdminSeeder.PhoneVar);
        StudioUser? owner = null;
        if (!string.IsNullOrWhiteSpace(adminPhone))
            owner = (await qs.Query<StudioUser>()
                .Where(u => u.Phone == adminPhone).Take(1).ToListAsync()).FirstOrDefault();
        owner ??= (await qs.Query<StudioUser>()
            .OrderBy(u => u.CreatedAt).Take(1).ToListAsync()).FirstOrDefault();
        if (owner is null)
        {
            Console.WriteLine($"[AppearanceSeed] لا مُستَخدِمَ استوديو — «{slug}» بِلا مالِك.");
            return;
        }

        await using var s = store.LightweightSession();
        var t = await s.LoadAsync<Tenant>(slug);
        if (t is null || t.OwnerUserId == owner.Id) return;
        t.OwnerUserId = owner.Id;
        s.Store(t);
        await s.SaveChangesAsync();
        Console.WriteLine($"[AppearanceSeed] «{slug}» صارَ مِلكَ {owner.Id}.");
    }

    private static async Task EnsureThemePacksAsync(IDocumentStore store, string slug)
    {
        await using var s = store.LightweightSession(slug);
        var dirty = false;
        var order = 0;
        foreach (var (packSlug, second) in ThemePacks)
        {
            // ‏**تَرتيبُ السَرد مِن `CreatedAt` لا مِن السلاج**:
            // ‏`TenantDefinitionService.ListAsync` يُرَتِّب
            // ‏`OrderBy(CreatedAt).ThenBy(Slug)`. فَطابَعٌ واحِدٌ
            // لِلثَلاثَة كانَ يَقلِبُ السَردَ إلى أَبجَديّ
            // (‏akhdar، azraq، layl) بَينَما تُثَبِّت اللَقطَةُ تَرتيبَ
            // التَطبيق. ثانِيَةٌ لِكُلٍّ تَكفي، ولا تُغَيِّرُ المَطبوع
            // (الصَفحَةُ تَطبَع `HH:mm`).
            var createdAt = ThemeDocsWrittenAt.AddSeconds(order++);
            if (await s.LoadAsync<TenantThemeDefinition>(packSlug) is not null) continue;
            var preset = ThemePresetCatalog.Find(packSlug);
            if (preset is null)
            {
                Console.Error.WriteLine($"[AppearanceSeed] لا حُزمَة «{packSlug}» في الكاتالوج.");
                continue;
            }
            // ‏`DefinitionJson` = نَصُّ الحُزمَة كَما هُوَ — نَفسُ ما
            // تَنسَخُه `TenantThemeService.ApplyPresetAsync`. لا يُبنى
            // نَصٌّ ثانٍ هُنا (القاعِدَة ٨).
            s.Store(new TenantThemeDefinition
            {
                Id             = preset.Slug,
                Slug           = preset.Slug,
                DefinitionJson = preset.Json,
                Status         = TenantThemeStatuses.Approved,
                CreatedBy      = ThemeDocsAuthor,
                CreatedAt      = createdAt,
                DecidedBy      = ThemeDocsAuthor,
                DecidedAt      = ThemeDocsWrittenAt.AddSeconds(second),
            });
            dirty = true;
        }
        if (dirty) await s.SaveChangesAsync();
    }

    /// <summary>يَكتُب مَناطِقَ الاكتِشاف بِـ
    /// <see cref="RegionsSaveService"/> نَفسِها — الشَكلُ مِن مَوضِعٍ
    /// واحِد — ثُمَّ <b>يُثَبِّتُ مُعَرِّفاتِها</b>: الأَصلُ يُوَلِّدُها
    /// بِـ<c>Guid.NewGuid()</c>، وذلك يَجعَل كُلَّ إقلاعٍ يَكتُب
    /// مُعَرِّفاتٍ جَديدَة. ولا تَظهَرُ في الوَسم اليَوم، لكِنّ بَذرَةً
    /// تَدَّعي الحَتمِيَّة لا تُبقي بابَ لا-حَتمِيَّةٍ مَفتوحاً.</summary>
    private static async Task EnsureRegionsAsync(IDocumentStore store, string slug, string raw)
    {
        await using var s = store.LightweightSession(slug);
        var existing = await s.Query<ImportedRecord>()
            .Where(x => x.Table == RegionsSaveService.Table).ToListAsync();
        if (existing.Count > 0) return;

        var (cities, code) = RegionsSaveService.Parse(raw);
        if (code is not null || cities is null)
        {
            Console.Error.WriteLine($"[AppearanceSeed] مَناطِق «{slug}» مَرفوضَة: {code}");
            return;
        }

        var records = RegionsSaveService.ToRecords(cities, ThemeDemoCreatedAt);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var n = 0;
        foreach (var r in records)
        {
            var old = (string)r.Data["Id"]!;
            map[old] = Deterministic(slug, ++n).ToString();
        }
        foreach (var r in records)
        {
            var oldId = (string)r.Data["Id"]!;
            var newId = map[oldId];
            r.Id = $"{RegionsSaveService.Table}/{newId}";
            r.SourceId = newId;
            r.Data["Id"] = newId;
            if (r.Data.TryGetValue("ParentId", out var p) && p is string ps && map.TryGetValue(ps, out var np))
                r.Data["ParentId"] = np;
            s.Store(r);
        }
        await s.SaveChangesAsync();
        Console.WriteLine($"[AppearanceSeed] مَناطِق «{slug}»: {records.Count} صَفّاً.");
    }

    /// <summary>مُعَرِّفٌ مُشتَقٌّ مِن السلاج والتَرتيب — ثابِتٌ عَبر
    /// الإقلاعات والمِنَصّات، بِلا <c>GetHashCode</c> (بَذرَتُه تَتَبَدَّل
    /// مَع كُلّ عَمَلِيَّة — نَفسُ عِلَّة <see cref="NafathNames"/>).</summary>
    private static Guid Deterministic(string slug, int n)
    {
        var h = 2166136261u;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes($"{slug}#{n}"))
        { h ^= b; h *= 16777619u; }
        var bytes = new byte[16];
        BitConverter.GetBytes(h).CopyTo(bytes, 0);
        BitConverter.GetBytes(0x5a3e1d00u).CopyTo(bytes, 4);
        BitConverter.GetBytes((uint)n).CopyTo(bytes, 8);
        bytes[12] = 0x77; bytes[13] = 0x51; bytes[14] = 0x00; bytes[15] = 0x0d;
        return new Guid(bytes);
    }

    // ── بُرهانُ عَدَمِ المَسّ ─────────────────────────────────────────

    /// <summary>عَدَدُ الصُفوفِ لِكُلّ (جَدوَل، مُستَأجِر) في مِخطَّط
    /// ‏<c>platform</c>. تُقرَأُ الجَداوِلُ مِن
    /// <c>information_schema</c> لا مِن قائِمَةٍ مَكتوبَة — فَجَدوَلٌ
    /// جَديدٌ يَدخُلُ القِياسَ بِلا تَعديلِ هذا المِلَفّ (القاعِدَة
    /// ١٠: أَداةٌ لا تَعُدُّ ما تَفحَصُه لا تُميَّزُ عَنِ العَمياء).</summary>
    private static async Task<Dictionary<string, long>> SnapshotAsync(string connStr)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        var tables = new List<(string Name, bool HasTenant)>();
        await using (var cmd = new NpgsqlCommand(
            """
            select table_name,
                   max(case when column_name = 'tenant_id' then 1 else 0 end) as has_tenant
            from information_schema.columns
            where table_schema = 'platform'
              and (table_name like 'mt\_doc\_%' or table_name in ('mt_events','mt_streams'))
            group by table_name
            order by table_name
            """, conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                tables.Add((r.GetString(0), r.GetInt32(1) == 1));

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (name, hasTenant) in tables)
        {
            var sql = hasTenant
                ? $"select coalesce(tenant_id, '(null)'), count(*) from platform.\"{name}\" group by 1"
                : $"select '(single)', count(*) from platform.\"{name}\"";
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                counts[$"{name}|{r.GetString(0)}"] = r.GetInt64(1);
        }
        return counts;
    }

    /// <summary>يَرمي إن تَحَرَّكَ عَدّادُ مُستَأجِرٍ خارِجَ
    /// <see cref="ThemeDemo"/> و<see cref="OwnerTest"/>. والجَدوَلُ
    /// أُحاديُّ الإيجار (<c>mt_doc_tenant</c>) يَنمو بِصَفَّينِ
    /// مَأذونَين، فَيُستَثنى بِفَرقِه لا بِاسمِه.</summary>
    private static int AssertOnlyBaselineTenantsMoved(
        Dictionary<string, long> before, Dictionary<string, long> after)
    {
        var drift = new List<string>();
        var outsiders = 0;
        foreach (var key in before.Keys.Union(after.Keys))
        {
            var tenantOf = key[(key.IndexOf('|') + 1)..];
            if (tenantOf is not (ThemeDemo or OwnerTest)) outsiders++;

            var b = before.GetValueOrDefault(key);
            var a = after.GetValueOrDefault(key);
            if (a == b) continue;

            var tenant = key[(key.IndexOf('|') + 1)..];
            if (tenant is ThemeDemo or OwnerTest) continue;

            // الجَدوَلُ العالَميّ: الزِيادَةُ المَأذونَة صَفّا
            // المُستَأجِرَين، ولا شَيءَ سِواها.
            if (key.StartsWith("mt_doc_tenant|", StringComparison.Ordinal) && a - b is > 0 and <= 2)
                continue;

            drift.Add($"{key}: {b} ← {a}");
        }

        if (drift.Count == 0) return outsiders;
        throw new InvalidOperationException(
            "[AppearanceSeed] البَذرَةُ مَسَّت مُستَأجِراً آخَر — أُوقِفَت:\n  " +
            string.Join("\n  ", drift));
    }
}
