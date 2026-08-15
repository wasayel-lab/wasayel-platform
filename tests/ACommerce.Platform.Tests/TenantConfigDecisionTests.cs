using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>دَوالّ القَرار النَقِيَّة — تُنادى بِلا قاعِدَةِ بَيانات
/// وبِلا خادِم.</b> وهذا هُوَ الرِبح الحَقيقيّ مِن إخراج المَنطِق مِن
/// أَجسام النِقاط: قَبلَ اليَوم، لِفَحص «هَل يُرفَض لَونٌ بِلا
/// <c>#</c>؟» كانَ يَلزَم إقلاعُ مُضيفٍ وجَلسَةُ مُشرِف
/// و<c>curl</c>. الآنَ سَطر.</para>
///
/// <para><b>ولِكُلّ رَمز خَرقٍ اختِبارٌ مُوجَبٌ وسالِب</b> (القاعِدَة ٤):
/// المُوجَب يُثبِت أَنّ الرَمز يَقَع حَيثُ يَجِب، والسالِب أَنَّه لا
/// يَقَع حَيثُ لا يَجِب — ومُصادِقٌ يَرُدّ الرَمزَ دائِماً يَعبُر
/// نِصفَ الاختِبار وَحدَه.</para>
/// </summary>
public class TenantConfigDecisionTests
{
    // ─── الهُوِيَّة البَصَرِيَّة ────────────────────────────────────

    private static BrandingSaveRequest Branding(
        string name = "مَتجَر", string color = "#1A2B3C", string? channel = null) =>
        new(name, "شِعار", "الرِياض", color, channel);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Branding_rejects_a_blank_name(string name) =>
        Assert.Equal(TenantConfigCodes.NameRequired,
            BrandingSaveService.WhyInvalid(Branding(name: name)));

    [Theory]
    [InlineData("1A2B3C")]     // بِلا #
    [InlineData("#1A2B3")]     // خَمسَة
    [InlineData("#1A2B3CD")]   // سَبعَة
    [InlineData("#GGHHII")]    // خارِج السِتَّ عَشرَة
    [InlineData("")]
    public void Branding_rejects_a_colour_that_is_not_six_hex_digits(string color) =>
        Assert.Equal(TenantConfigCodes.ColorInvalid,
            BrandingSaveService.WhyInvalid(Branding(color: color)));

    [Theory]
    [InlineData("#1a2b3c")]
    [InlineData("#ABCDEF")]
    [InlineData("  #123456  ")]   // التَشذيب مِن الخِدمَة لا مِن السَطح
    public void Branding_accepts_a_valid_request(string color) =>
        Assert.Null(BrandingSaveService.WhyInvalid(Branding(color: color)));

    /// <summary><b>والاسم يَسبِق اللَون</b> — لِأَنّ نَموذَجاً بِلا اسم
    /// ولَونٍ فاسِد كانَ يُعطي <c>name_required</c> في المَسارَين قَبلَ
    /// التَوحيد، فَلا يَتَغَيَّر ما يَراه المُستَخدِم.</summary>
    [Fact]
    public void Branding_reports_the_missing_name_before_the_bad_colour() =>
        Assert.Equal(TenantConfigCodes.NameRequired,
            BrandingSaveService.WhyInvalid(Branding(name: "", color: "nope")));

    // ─── الفِئات ───────────────────────────────────────────────────

    [Theory]
    [InlineData("sale")]                 // عَمودٌ واحِد
    [InlineData("| اسم")]                // slug فارِغ
    [InlineData("sale |")]               // تَسمِيَة فارِغَة
    public void Categories_reject_a_row_that_is_not_two_columns(string raw) =>
        Assert.Equal(TenantConfigCodes.BadFormat, CategoriesSaveService.Parse(raw).Code);

    [Theory]
    [InlineData("")]
    [InlineData("\n\n   \n")]
    public void Categories_reject_an_input_with_no_row_at_all(string raw) =>
        Assert.Equal(TenantConfigCodes.Empty, CategoriesSaveService.Parse(raw).Code);

    /// <summary><b>الأَيقونَةُ الافتِراضِيَّة حُسِمَت</b>: 🏷️ لا 🏠.
    /// وثَلاثَةُ مَداخِل تُعطيها: عَمودانِ فَقَط، وعَمودٌ ثالِثٌ
    /// فارِغ، وعَمودٌ ثالِثٌ بِمَسافاتٍ وَحدَها.</summary>
    [Theory]
    [InlineData("sale | لِلبَيع")]
    [InlineData("sale | لِلبَيع |")]
    [InlineData("sale | لِلبَيع |   | rent")]
    public void Categories_default_the_icon_to_the_tag_not_the_house(string raw)
    {
        var (cats, code) = CategoriesSaveService.Parse(raw);
        Assert.Null(code);
        Assert.Equal(CategoriesSaveService.DefaultIcon, cats![0].Icon);
        Assert.Equal("🏷️", cats[0].Icon);
    }

    [Fact]
    public void Categories_keep_an_explicit_icon_and_normalise_slug_and_kind()
    {
        var (cats, code) = CategoriesSaveService.Parse("  SALE | لِلبَيع | 🚚 | RENT  \n\n  cars | سَيّارات  ");
        Assert.Null(code);
        Assert.Equal(2, cats!.Count);

        Assert.Equal("sale", cats[0].Slug);          // مُصَغَّر
        Assert.Equal("لِلبَيع", cats[0].Label);
        Assert.Equal("🚚", cats[0].Icon);            // الصَريحُ يَبقى
        Assert.Equal("rent", cats[0].Kind);          // مُصَغَّر
        Assert.Equal(0, cats[0].SortOrder);

        // التَشذيب: سَطرٌ بِمَسافَةٍ بادِئَة لا يُنتِج slug بِفَراغ.
        Assert.Equal("cars", cats[1].Slug);
        Assert.Equal(1, cats[1].SortOrder);
    }

    // ─── الأَدوار ──────────────────────────────────────────────────

    /// <summary>
    /// <para><b>البُرهان الَّذي كَتَبَ حَسمَ الأَدوار.</b> دَورٌ
    /// قائِمٌ رُفِعَت لَه أَيقونَةُ PWA واسمٌ مُخَصَّص، ثُمَّ حُفِظَت
    /// صَفحَةُ الأَدوار بِنَفس الاختِيار: كانَ سُلوك
    /// <c>/studio</c> يَمحو الاثنَين، وسُلوك <c>/admin</c>
    /// يُبقيهِما. والآنَ تَعريفٌ واحِد، وهذا الاختِبار هُوَ الَّذي
    /// يَمنَع عَودَةَ المَحو.</para>
    /// </summary>
    [Fact]
    public void Roles_keep_the_owners_customisation_when_the_same_role_is_saved_again()
    {
        var tmpl = ACommerce.Kit.Roles.RoleCatalog.All[0];

        var existing = ACommerce.Kit.Roles.RoleCatalog.InstantiateRole(tmpl, 7);
        existing.Label = "تَسمِيَتي";
        existing.Icon = "🐪";
        existing.PwaName = "تَطبيقي";
        existing.PwaIconDataUrl = "data:image/png;base64,AAA";

        var composed = RolesSaveService.Compose(
            new[] { existing }, new[] { tmpl.Slug }, tmpl.Slug);

        var role = Assert.Single(composed);
        Assert.Equal("تَسمِيَتي", role.Label);
        Assert.Equal("🐪", role.Icon);
        Assert.Equal("تَطبيقي", role.PwaName);
        Assert.Equal("data:image/png;base64,AAA", role.PwaIconDataUrl);

        // وما يَملِكُه الكاتالوج يُحَدَّث — وذلك مَقصود.
        Assert.Equal(tmpl.Permissions.ToList(), role.Permissions);
        Assert.Equal(tmpl.HomeRoute, role.HomeRoute);
        Assert.Equal(0, role.SortOrder);
        Assert.True(role.IsDefault);
    }

    /// <summary>التَرتيبُ تَرتيبُ الكاتالوج لا تَرتيبُ الاختِيار —
    /// فَنَفسُ المُدخَل يُعطي نَفسَ <c>SortOrder</c> مِن أَيّ
    /// سَطح.</summary>
    [Fact]
    public void Roles_are_ordered_by_the_catalogue_not_by_the_form()
    {
        var all = ACommerce.Kit.Roles.RoleCatalog.All;
        Assert.True(all.Count >= 3, "أَداة عَمياء: الكاتالوج أَصغَر مِن أَن يُرَتَّب.");

        var reversed = new[] { all[2].Slug, all[0].Slug };
        var composed = RolesSaveService.Compose(Array.Empty<ACommerce.Kit.Roles.Role>(), reversed, null);

        Assert.Equal(new[] { all[0].Slug, all[2].Slug }, composed.Select(r => r.Slug));
        Assert.Equal(new[] { 0, 1 }, composed.Select(r => r.SortOrder));
        Assert.All(composed, r => Assert.False(r.IsDefault));
    }

    /// <summary>ودَورٌ لَم يُختَر يَخرُج — نَفسُ سُلوك المَسارَين
    /// قَبلَ التَوحيد، ولَم يُضَف رَفضٌ لِاختِيارٍ فارِغ لِأَنَّه
    /// لَم يَكُن.</summary>
    [Fact]
    public void Roles_that_were_not_selected_are_dropped()
    {
        var all = ACommerce.Kit.Roles.RoleCatalog.All;
        var existing = all.Take(2).Select((t, i) => ACommerce.Kit.Roles.RoleCatalog.InstantiateRole(t, i)).ToArray();

        Assert.Empty(RolesSaveService.Compose(existing, Array.Empty<string>(), null));
        Assert.Single(RolesSaveService.Compose(existing, new[] { all[1].Slug }, null));
    }

    // ─── المَناطِق ─────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Regions_reject_an_empty_input(string raw) =>
        Assert.Equal(TenantConfigCodes.Empty, RegionsSaveService.Parse(raw).Code);

    [Theory]
    [InlineData("> حَيّ")]
    [InlineData("  > حَيّ١، حَيّ٢")]
    public void Regions_reject_a_district_line_with_no_city(string raw) =>
        Assert.Equal(TenantConfigCodes.BadFormat, RegionsSaveService.Parse(raw).Code);

    [Fact]
    public void Regions_parse_cities_with_and_without_districts()
    {
        var (cities, code) = RegionsSaveService.Parse(
            "الرِياض > العُلَيا، النَخيل\n\n  جُدَّة  \nالدَمّام > الشاطِئ,الفَيصَلِيَّة");

        Assert.Null(code);
        Assert.Equal(3, cities!.Count);
        Assert.Equal(new[] { "العُلَيا", "النَخيل" }, cities[0].Districts);
        Assert.Equal("جُدَّة", cities[1].Name);
        Assert.Empty(cities[1].Districts);
        // الفاصِلَةُ العَرَبِيَّة واللاتينِيَّة كِلتاهُما تَفصِلان.
        Assert.Equal(new[] { "الشاطِئ", "الفَيصَلِيَّة" }, cities[2].Districts);
    }

    /// <summary>
    /// <para><b>البُرهان الَّذي كَتَبَ الشَكل الجامِع.</b> صَفحَتا
    /// القِراءَة تَقرَآنِ مَفاتيحَ مُختَلِفَة — الإدارَةُ تُفَهرِس
    /// بِـ<c>SourceId</c>، والاستوديو بِـ<c>Data["Id"]</c> ويُرَتِّب
    /// بِـ<c>Data["SortOrder"]</c>. وهذا الاختِبار يُثَبِّت أَنّ
    /// السِجِلّ الواحِد يُرضي <b>كِلتَيهِما</b> — فَلا حاجَةَ إلى
    /// تَعديل صَفحَةِ قِراءَةٍ واحِدَة، وهذا هُوَ ما جَعَلَ الحَسمَ
    /// «شَكلٌ جامِع» لا «سَطحٌ يَغلِب».</para>
    /// </summary>
    [Fact]
    public void Regions_records_satisfy_both_existing_readers()
    {
        var (cities, _) = RegionsSaveService.Parse("الرِياض > العُلَيا، النَخيل\nجُدَّة");
        var recs = RegionsSaveService.ToRecords(cities!, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(4, recs.Count);                       // مَدينَتان + حَيّان

        foreach (var r in recs)
        {
            Assert.Equal("DiscoveryRegions", r.Table);
            Assert.StartsWith("DiscoveryRegions/", r.Id);

            // قارِئُ الإدارَة: يُفَهرِس بِـSourceId، ويَقرَأ
            // Name/ParentId/Level مِن Data.
            Assert.False(string.IsNullOrEmpty(r.SourceId));
            Assert.True(Guid.TryParse(r.SourceId, out _));
            Assert.True(r.Data.ContainsKey("Name"));
            Assert.True(r.Data.ContainsKey("ParentId"));
            Assert.True(int.TryParse(r.Data["Level"]?.ToString(), out var level));
            Assert.InRange(level, 1, 2);

            // قارِئُ الاستوديو: يُفَهرِس بِـData["Id"] ويُرَتِّب
            // بِـData["SortOrder"].
            Assert.True(Guid.TryParse(r.Data["Id"]?.ToString(), out _));
            Assert.True(int.TryParse(r.Data["SortOrder"]?.ToString(), out _));

            // والمِفتاحانِ يُشيرانِ إلى نَفس المُعَرِّف — وإلّا
            // فَهرَسَ القارِئانِ سِجِلَّين مُختَلِفَين.
            Assert.Equal(r.SourceId, r.Data["Id"]?.ToString());
        }

        var citiesOut = recs.Where(r => r.Data["Level"]!.ToString() == "1").ToList();
        var districts = recs.Where(r => r.Data["Level"]!.ToString() == "2").ToList();
        Assert.Equal(2, citiesOut.Count);
        Assert.Equal(2, districts.Count);

        // ‏ParentId فارِغٌ لِلمَدينَة (الإدارَة تَقرَؤُه فارِغاً،
        // والاستوديو Guid.Empty)، ويُطابِق مُعَرِّفَ المَدينَة لِلحَيّ.
        Assert.All(citiesOut, c => Assert.Null(c.Data["ParentId"]));
        Assert.All(districts, d => Assert.Equal(citiesOut[0].SourceId, d.Data["ParentId"]?.ToString()));

        // والتَرتيبُ داخِلَ المَدينَة يَبدَأ مِن صِفر لِكُلّ مَدينَة.
        Assert.Equal(new[] { "0", "1" }, districts.Select(d => d.Data["SortOrder"]!.ToString()));
        Assert.Equal(new[] { "0", "1" }, citiesOut.Select(c => c.Data["SortOrder"]!.ToString()));
    }

    // ─── أَيقونات PWA ──────────────────────────────────────────────

    private static UploadedIcon Icon(string type, long length) =>
        new(type, length, _ => Task.FromResult(Array.Empty<byte>()));

    [Fact]
    public void Pwa_rejects_an_icon_above_the_ceiling() =>
        Assert.Equal(TenantConfigCodes.IconTooLarge,
            PwaSaveService.WhyIconRejected(Icon("image/png", PwaSaveService.MaxIconBytes + 1)));

    [Fact]
    public void Pwa_accepts_an_icon_exactly_at_the_ceiling() =>
        Assert.Null(PwaSaveService.WhyIconRejected(Icon("image/png", PwaSaveService.MaxIconBytes)));

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/gif")]
    [InlineData("text/html")]
    [InlineData("")]
    public void Pwa_rejects_a_content_type_outside_the_allowed_set(string type) =>
        Assert.Equal(TenantConfigCodes.IconBadType,
            PwaSaveService.WhyIconRejected(Icon(type, 1024)));

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/svg+xml")]
    [InlineData("IMAGE/WEBP")]   // الحالَةُ تُطَبَّع
    public void Pwa_accepts_the_three_allowed_types(string type) =>
        Assert.Null(PwaSaveService.WhyIconRejected(Icon(type, 1024)));

    /// <summary>الحَجمُ يُفحَص قَبلَ النَوع — وذلك تَرتيبُ النُقطَتَين
    /// قَبلَ التَوحيد، فَلا يَتَغَيَّر ما يَراه المُستَخدِم.</summary>
    [Fact]
    public void Pwa_reports_the_size_before_the_type() =>
        Assert.Equal(TenantConfigCodes.IconTooLarge,
            PwaSaveService.WhyIconRejected(Icon("image/gif", PwaSaveService.MaxIconBytes + 1)));

    // ─── الخَصائِص ─────────────────────────────────────────────────

    [Theory]
    [InlineData("code | الاسم | text")]               // ثَلاثَةُ أَعمِدَة
    [InlineData(" | الاسم | text | req")]             // رَمزٌ فارِغ
    [InlineData("code |  | text | req")]              // اسمٌ فارِغ
    [InlineData("code | الاسم |  | req")]             // نَوعٌ فارِغ
    [InlineData("code | الاسم | select | req | بِلا_مُساواة")]
    public void Attributes_reject_a_malformed_row(string raw) =>
        Assert.Equal(TenantConfigCodes.BadFormat, AttributesSaveService.Parse(raw).Code);

    [Fact]
    public void Attributes_parse_options_and_the_required_flag()
    {
        var (rows, code) = AttributesSaveService.Parse(
            "  rooms | الغُرَف | number | req  \n" +
            "kind | النَوع | select | opt | villa=فِلّا، apt=شَقَّة\n\n");

        Assert.Null(code);
        Assert.Equal(2, rows!.Count);

        Assert.Equal("rooms", rows[0].Code);
        Assert.True(rows[0].IsRequired);
        Assert.Empty(rows[0].Options);

        Assert.False(rows[1].IsRequired);
        Assert.Equal(new[] { ("villa", "فِلّا"), ("apt", "شَقَّة") }, rows[1].Options);
    }

    /// <summary>ونَصٌّ فارِغٌ لَيسَ خَطَأً هُنا — هُوَ «امحُ خَصائِص
    /// هذا النِطاق»، وذلك سُلوكُ المَسارَين قَبلَ التَوحيد. الرَفضُ
    /// الوَحيد غِيابُ النِطاق.</summary>
    [Fact]
    public void Attributes_treat_an_empty_text_as_clearing_the_scope()
    {
        var (rows, code) = AttributesSaveService.Parse("");
        Assert.Null(code);
        Assert.Empty(rows!);
    }
}
