using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Gates;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>أَرضِيَّةُ الأَدوار: لا يولَدُ مَتجَرٌ بِصِفرِ أَدوارٍ مِن
/// مَسارِ العَميل.</b></para>
///
/// <para><b>الكِلفَةُ الَّتي كَتَبَت هذا المِلَفّ (‏2026-08-31)</b>: مَسارُ
/// العَميلِ (<c>/studio/begin</c> ← <c>/studio/s/{id}/build</c>) يَحفَظُ
/// <c>description</c> وَحدَه، فَ<c>Answers</c> تَبقى فارِغَةً تَماماً،
/// فَبَوّابَةُ العَدِّ في <c>FeasibilityAnalysisService.SaveAnswerAsync</c>
/// لا تُفتَحُ أَبَداً (‏1 مِن 7)، فَ<c>PatternMatcher</c> لا يُنادى،
/// فَ<c>SuggestedPattern</c> يَبقى <c>""</c>، فَ<c>RolesFor("")</c>
/// كانَت تُعيدُ <b>مَصفوفَةً فارِغَة</b> — ومَتجَرٌ بِصِفرِ أَدوارٍ
/// يَدخُلُ «الوَضعَ الموروث» في
/// <see cref="RolePermissions.Has"/>: <b>كُلُّ شَيءٍ مَسموحٌ لِكُلِّ
/// أَحَد</b>. قِيسَ حَيّاً: عُضوٌ سَجَّلَ رَقمَ هاتِفٍ للتَوِّ قَرَأَ
/// هَواتِفَ الأَعضاء، وأَعادَ كِتابَةَ هُوِيَّةِ المَتجَر، ثُمَّ أَعادَ
/// كِتابَةَ كَتالوجِ أَدوارِه — بَينَما المَجهولُ يُرَدُّ ‏403 على
/// النُقطَةِ نَفسِها. ومَتجَرانِ حَقيقِيّانِ في القاعِدَةِ كانا كَذلك.</para>
///
/// <para><b>ولِماذا خَمسُ فُرَصٍ للصُراخِ لَم تُستَعمَل</b>: الفَشَلُ
/// كانَ صامِتاً في كُلِّ حَلقَة — لا لوغ عِندَ تَخَطّي المُطابِق، ولا
/// تَحَقُّقَ في <c>/build</c>، ولا تَحذيرَ مِن <c>RolesFor</c>، ولا
/// اعتِراضَ عِندَ الكِتابَة، والشاشَةُ تَقولُ <c>?built=1</c>. لِذلك
/// الحارِسُ هُنا <b>فَحصٌ يَحمَرّ</b> لا تَعليقٌ يُطَمئِن (القاعِدَة ٢).</para>
/// </summary>
public class StudioBuildRoleFloorTests(ITestOutputHelper output)
{
    private const string Slug = "probe-store";

    /// <summary><c>DeriveSuggestion</c> نِصفٌ نَقِيٌّ بِلا I/O — لا
    /// يَلمَسُ <c>_store</c> بِحَرف، فَ<c>null!</c> هُنا تَوصيفٌ لِذلكَ
    /// لا حيلَةُ اختِبار.</summary>
    private static readonly TenantFromAnalysisFactory Factory = new(null!);

    /// <summary>الجَلسَةُ <b>كَما يَترُكُها مَسارُ العَميلِ حَرفاً</b>:
    /// وَصفٌ حُرٌّ في <c>ProjectDescription</c>، و<c>Answers</c> فارِغَةٌ
    /// تَماماً، و<c>SuggestedPattern</c> فارِغ.</summary>
    private static IncubatorSession ClientPathSession() => new()
    {
        Id = Guid.NewGuid(),
        OwnerUserId = Guid.NewGuid(),
        Status = IncubatorStatus.Completed,
        ProjectDescription = "مِنَصَّةٌ تَربِطُ أَصحابَ المَزارِعِ بِالمُشتَرين",
        // Answers = {} — وهذا هُوَ جِذرُ العَطَب، لا تَبسيطُ اختِبار.
        SuggestedPattern = "",
    };

    private static Tenant TenantBuiltFrom(string pattern) => new()
    {
        Id = Slug,
        Name = "مَتجَرُ الفَحص",
        OwnerUserId = Guid.NewGuid(),
        Roles = TenantFromAnalysisFactory.RolesFor(pattern).ToList(),
    };

    private static User MemberWith(string activeRole)
        => new() { Id = Guid.NewGuid(), TenantSlug = Slug, ActiveRole = activeRole };

    private static string[] SlugsOf(IEnumerable<Role> roles)
        => roles.Select(r => r.Slug).ToArray();

    // ─── (١) لا يولَدُ مَتجَرٌ بِصِفرِ أَدوارٍ مِن مَسارِ العَميل ──────

    [Fact]
    public void ClientPathStore_IsNeverBornWithoutRoles()
    {
        var session = ClientPathSession();

        // هذا بِعَينِه ما يُمَرَّرُ في
        // `MarketplaceTemplateExtensions.cs` عِندَ `/studio/s/{id}/build`:
        // `session.SuggestedPattern` كَما هُوَ، بِلا اشتِقاقٍ ولا تَحَقُّق.
        var roles = TenantFromAnalysisFactory.RolesFor(session.SuggestedPattern);

        Assert.NotEmpty(roles);
        Assert.Contains(roles, r => r.CatalogSlug == "tenant_admin");
        Assert.Contains(roles, r => r.IsDefault);
    }

    /// <summary>و«مَجهول» لَيسَ حالَةً أَخَفَّ مِن «فارِغ» — بَل أَوسَع:
    /// <c>"custom"</c> <b>مُخرَجٌ مُعلَنٌ لِـ<see cref="PatternMatcher"/></b>
    /// (السَطر الأَخير)، ويَبذُرُه <c>IncubatorSampleSeeder</c> حَرفِيّاً.
    /// فَحَتّى المَسارُ الإداريُّ <b>المُكتَمِل</b> كانَ يُنتِجُ مَتجَراً
    /// بِصِفرِ أَدوارٍ إن وَقَعَ عَلَيه.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("custom")]
    [InlineData("نَمَطٌ لا يَعرِفُه أَحَد")]
    public void UnknownOrEmptyPattern_StillYieldsRoles(string pattern)
    {
        var roles = TenantFromAnalysisFactory.RolesFor(pattern);
        Assert.NotEmpty(roles);
        Assert.Contains(roles, r => r.CatalogSlug == "tenant_admin");
    }

    /// <summary>والأَنماطُ المَعروفَةُ لا تَتَبَدَّل — الأَرضِيَّةُ
    /// تُضافُ تَحتَ الفَراغِ ولا تُزيحُ ما كانَ يَعمَل (تَوصيف).</summary>
    [Theory]
    [InlineData("ondemand", "rider", "driver", "tenant_admin")]
    [InlineData("marketplace", "customer", "vendor", "tenant_admin")]
    [InlineData("classifieds", "customer", "vendor", "tenant_admin")]
    [InlineData("rental", "customer", "host", "tenant_admin")]
    public void KnownPatterns_KeepTheirExactRoles(string pattern, params string[] expected)
        => Assert.Equal(expected, SlugsOf(TenantFromAnalysisFactory.RolesFor(pattern)));

    // ─── (٢) عُضوٌ عاديٌّ لا يَبلُغُ فِعلاً مَحجوزاً في مَتجَرٍ حَديث ──

    [Fact]
    public void PlainMember_CannotManage_StoreBuiltFromClientPath()
    {
        // العُضوُ كَما تَكتُبُه `/{slug}/auth/phone/verify` فِعلاً:
        // `ActiveRole` **فارِغ** (التَسكينُ التِلقائيُّ لا يَقَعُ إلّا
        // بِدَورٍ واحِدٍ بِالضَبط). وهذِه حَرفِيّاً حالَةُ المُهاجِمِ
        // المَقيسَة: قَرَأَ الهَواتِفَ وكَتَبَ الهُوِيَّةَ والأَدوار.
        var tenant = TenantBuiltFrom(new IncubatorSession().SuggestedPattern);
        Assert.False(TenantAdminGuard.HasTenantManage(tenant, MemberWith("")));
    }

    [Fact]
    public void PlainMember_OnDefaultRole_CannotManage_StoreBuiltFromClientPath()
    {
        var tenant = TenantBuiltFrom("");
        var defaultRole = tenant.Roles.First(r => r.IsDefault).Slug;
        // ولا يَكفي أَن يَفشَلَ الفارِغُ: الدَورُ الافتراضيُّ نَفسُه
        // يَجِبُ أَلّا يَحمِلَ `tenant.manage`.
        Assert.False(TenantAdminGuard.HasTenantManage(tenant, MemberWith(defaultRole)));
    }

    [Fact]
    public void TenantAdminRole_StillManages_StoreBuiltFromClientPath()
    {
        // والبابُ لا يُغلَقُ على أَهلِه: الدَورُ الإداريُّ يَمُرّ.
        var tenant = TenantBuiltFrom("");
        Assert.True(TenantAdminGuard.HasTenantManage(tenant, MemberWith("tenant_admin")));
    }

    // ─── (٣) الشاشَةُ لا تَعِدُ بِنَمَطٍ لا يُكتَب ────────────────────

    /// <summary>
    /// <para><b>التَناقُضُ بَينَ الوَعدِ والكِتابَةِ هُوَ العَطَبُ
    /// الأَصلِيّ.</b> <c>StudioStudy.razor</c> يَملأُ استِمارَةَ البِناءِ
    /// مِن <c>DeriveSuggestion</c> — وهي تَسقُطُ إلى <c>marketplace</c>
    /// عِندَ الفَراغ — بَينَما <c>/build</c> يُمَرِّرُ
    /// <c>session.SuggestedPattern</c> <b>خاماً</b>. فَالمُشتَقُّ
    /// للعَرضِ والمُمَرَّرُ للكِتابَةِ <b>قيمَتانِ مُختَلِفَتان</b>،
    /// والأَدوارُ تُكتَبُ مِن الثانِيَة.</para>
    ///
    /// <para>ولا يَفحَصُ هذا «هَل يُعرَضُ اسمُ النَمَطِ على الشاشَة؟» —
    /// لا يُعرَض، والقياسُ أَثبَتَه. يَفحَصُ ما هُوَ أَدَقّ: أَن يَكونَ
    /// <b>ما تُبنى عَلَيه الاستِمارَةُ هُوَ ما يُكتَب</b>.</para>
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("custom")]
    [InlineData("marketplace")]
    [InlineData("classifieds")]
    [InlineData("rental")]
    [InlineData("ondemand")]
    [InlineData("نَمَطٌ مَجهول")]
    public void ScreenPromise_EqualsWhatIsWritten(string stored)
    {
        var session = ClientPathSession();
        session.SuggestedPattern = stored;

        var promised = Factory.DeriveSuggestion(session).Pattern;  // ما تُبنى عَلَيه الاستِمارَة
        var written  = session.SuggestedPattern;                   // ما يُمَرَّرُ إلى CreateAsync

        Assert.Equal(SlugsOf(TenantFromAnalysisFactory.RolesFor(promised)),
                     SlugsOf(TenantFromAnalysisFactory.RolesFor(written)));
        Assert.NotEmpty(TenantFromAnalysisFactory.RolesFor(promised));
    }

    // ─── (٤) الفَحصُ الَّذي كانَ غائِباً: مُخرَجُ المُطابِقِ ⇐ أَدوار ──

    /// <summary>
    /// <para><b>لَو وُجِدَ هذا الفَحصُ لَسَقَطَ أَحمَرَ يَومَ كُتِبَ
    /// <c>"custom"</c>.</b> يَمُرُّ على <b>فَضاءِ الإجاباتِ المُغلَقِ
    /// كامِلاً</b> كَما يُعَرِّفُه <see cref="DiscoveryQuestionBank"/> —
    /// لا على قائِمَةِ أَنماطٍ مَنسوخَةٍ بِاليَد — فَكُلُّ مُخرَجٍ
    /// مُمكِنٍ لِلمُطابِقِ يَجِبُ أَن يُقابِلَه دَورٌ واحِدٌ على
    /// الأَقَلّ.</para>
    ///
    /// <para><b>ويَطبَعُ عَدَدَ ما فَحَصَه ويَفشَلُ إن كانَ صِفراً</b>
    /// (القاعِدَة ١٠): «صِفرُ مُخالَفَة» بِلا عَدّادٍ لا يُميَّزُ عَن
    /// أَداةٍ عَمياء.</para>
    /// </summary>
    [Fact]
    public void EveryPatternMatcherOutcome_MapsToAtLeastOneRole()
    {
        string[] Options(string id) => DiscoveryQuestionBank.Questions
            .First(q => q.Id == id).Options.Select(o => o.Value)
            .Concat(new[] { "" })   // وغِيابُ الإجابَةِ حالَةٌ مُمكِنَةٌ أَيضاً
            .ToArray();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var combos = 0;
        foreach (var offer in Options("offer"))
        foreach (var payment in Options("payment"))
        foreach (var realtime in Options("realtime"))
        {
            combos++;
            var answers = new Dictionary<string, string>
            {
                ["offer"] = offer, ["payment"] = payment, ["realtime"] = realtime
            };
            seen.Add(PatternMatcher.Match(answers).Pattern);
        }

        Assert.True(combos > 0, "صِفرُ تَوليفَةٍ مَفحوصَة — الأَداةُ عَمياء لا النِظامُ سَليم.");
        Assert.True(seen.Count > 0, "صِفرُ نَمَطٍ مَرصود — الأَداةُ عَمياء.");

        var orphans = seen.Where(p => TenantFromAnalysisFactory.RolesFor(p).Count == 0).ToArray();
        Assert.True(orphans.Length == 0,
            $"فُحِصَت {combos} تَوليفَةً فَأَعطَت {seen.Count} نَمَطاً، ومِنها "
          + $"{orphans.Length} بِصِفرِ أَدوار: {string.Join(", ", orphans)}");
    }

    // ─── (٥) الوَضعُ الموروثُ يَبقى — ومُستَهلِكُه يُسَمّى بِالاسم ────

    /// <summary>
    /// <para><b>تَوصيفٌ لا استِحسان</b>: <c>RolePermissions.Has</c> ما
    /// زالَت تَمنَحُ كُلَّ شَيءٍ لِمَتجَرٍ بِصِفرِ أَدوار. ولَم تُمَسّ
    /// <b>لِأَنَّ لَها مُستَهلِكاً حَيّاً مَقصوداً</b>:
    /// <c>AppearanceBaselineSeeder</c> يَبذُرُ <c>theme-demo</c> بِـ
    /// <c>Roles = new()</c> وتَعليقُه يَقولُ «<b>بِلا أَدوارٍ عَمداً</b>»
    /// ويُسَمّي هذا السَطرَ بِعَينِه سَنَداً لِفَرعِ «لَوحَة الإداريّ»
    /// في لَقطَةِ <c>user-manage.html</c>. وأَداةُ الوَكيلِ
    /// <c>set_roles</c> تَعرِضُ الوَضعَ نَفسَه نَصّاً: «اِترُكها فارِغَة
    /// لِنَمَط user-فَرد».</para>
    ///
    /// <para>فَالعِلاجُ <b>مَنعُ بُلوغِه مِن مَسارِ البِناء</b> لا
    /// حَذفُه — وحَذفُه قَرارٌ مُنتَجِيٌّ يَملِكُه صاحِبُ المَشروعِ
    /// وَحدَه.</para>
    /// <para><b>(‏2026-10-28، ‏ADR-040، قَرارُ المالِك د62 — قُلِبَ بِاسمِه)</b>: كانَ <c>LegacyMode_IsUntouched_AndItsConsumerIsNamed</c>
    /// بِـ<c>Assert.True(…, "tenant.manage")</c>. و«حَذفُه قَرارٌ مُنتَجِيٌّ يَملِكُه صاحِبُ المَشروع» أَعلاه — اتَّخَذَه: صِفرُ الأَدوارِ لا يُعطي
    /// الإدارَة. ومُستَهلِكُه المُسَمّى (<c>theme-demo</c> بِلا أَدوار) باقٍ لِفَرعِ الرَئيسِيَّةِ الافتِراضِيّ، وفَرعُ «لَوحَة الإداريّ» في
    /// <c>user-manage.html</c> صارَ «لا صَلاحِيَّة» (سِجِلُّ المالِك «أ» د62).</para>
    /// </summary>
    [Fact]
    public void LegacyMode_GrantsMemberPermissionsAndNoAdministration_AndItsConsumerIsNamed()
    {
        Assert.False(RolePermissions.Has(Array.Empty<Role>(), "", "tenant.manage"));
        Assert.True(RolePermissions.Has(Array.Empty<Role>(), "", "listing.create"));

        var seeder = File.ReadAllText(Path.Combine(
            ThemeZeroEquivalenceTests.RepoRoot,
            "apps", "V1.App", "Seed", "AppearanceBaselineSeeder.cs"));
        Assert.Contains("Roles      = new(),", seeder, StringComparison.Ordinal);
    }

    // ─── (٦) ولا نُقطَةَ إنشاءٍ حَيَّةٍ تَكتُبُ صِفرَ أَدوارٍ صامِتَةً ──

    /// <summary>نُقطَةُ إنشاءٍ تُنشِئُ مَتجَراً بِلا أَدوار، مُثَبَّتَةٌ
    /// بِسَبَبِها.</summary>
    private sealed record RolelessSite(string File, string WhyAr);

    /// <summary>
    /// <para><b>الاستِثناءُ الوَحيدُ المُثَبَّت</b> — ونُمُوُّ هذِه
    /// القائِمَةِ <b>قَرارٌ مَرئيٌّ في مُراجَعَة</b> لا نَتيجَةُ
    /// نِسيان.</para>
    /// </summary>
    private static readonly RolelessSite[] Pinned =
    {
        new("libs/templates/ACommerce.Templates.Customer.Marketplace/Services/AgentService.cs",
            "أَداةُ الوَكيل create_tenant. لا تُلمَسُ هُنا لِأَنّ «مَتجَرٌ بِلا أَدوار» "
          + "وَضعٌ مُنتَجِيٌّ مُعلَنٌ في نَصِّ أَداةِ set_roles نَفسِها («اِترُكها "
          + "فارِغَة لِنَمَط user-فَرد») — فَفَرضُ أَرضِيَّةٍ عَلَيها اختِراعُ قَرارِ "
          + "مُنتَجٍ لا إصلاحُ عَطَب (القاعِدَة ١٦). البابُ مَرصودٌ هُنا بِاسمِه "
          + "ومَوقوفٌ لِصاحِبِ المَشروع، لا مَنسِيّ."),
    };

    [Fact]
    public void EveryRuntimeTenantCreationSite_WritesRoles()
    {
        var root = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs");
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();

        var scanned = 0;
        var offenders = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"new Tenant\s*(?=\{)"))
            {
                var block = InitializerBlock(text, m.Index + m.Length);
                if (block is null) continue;
                scanned++;
                if (block.Contains("Roles", StringComparison.Ordinal)) continue;
                var rel = Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, file)
                              .Replace('\\', '/');
                if (Pinned.Any(p => p.File == rel)) continue;
                offenders.Add(rel);
            }
        }

        Assert.True(scanned > 0,
            "صِفرُ نُقطَةِ إنشاءٍ مَفحوصَة — الأَداةُ عَمياء لا المُستَودَعُ نَظيف.");
        Assert.True(offenders.Count == 0,
            $"فُحِصَت {scanned} نُقطَةَ إنشاءٍ في {files.Length} مِلَفّاً، ومِنها "
          + $"{offenders.Count} تَكتُبُ مَتجَراً بِلا أَدوار: {string.Join(", ", offenders)}");
    }

    // ─── (٧) أَرضِيَّةُ البائِع: في كُلِّ نَمَطٍ وكُلِّ صَفِّ بَذرٍ دَورٌ ذاتِيُّ المَنحِ يَحمِلُ listing.create ──

    /// <summary>
    /// <para><b>المُستَثنى بِاسمِه — <c>theme-demo</c></b>، ولِسَبَبِه لا بِالإحالَة: يَبذُرُه
    /// <c>AppearanceBaselineSeeder</c> بِـ<c>Roles = new()</c> <b>عَمداً</b> لِلَقطَةِ المَظهَر، فَهُوَ في
    /// «الوَضعِ المَوروث» الَّذي تَمنَحُ فيه <see cref="RolePermissions.Has"/> كُلَّ صَلاحِيَّةٍ لِكُلِّ مُسَجَّل —
    /// فَلا دَورَ فيه يُسأَلُ عَن <c>listing.create</c>، ولا يُقاسُ عَلَيه «أَرضِيَّةُ بائِع». وهُوَ لَيسَ صَفّاً في
    /// <c>TestDataSeeder.Plan</c> ولا نَمَطاً في <c>KnownPatterns</c>؛ يُسَمّى هُنا لِيَكونَ غِيابُه عَن العَدِّ قَراراً
    /// مَقروءاً لا سَهواً (‏ADR-032 §٦‑ج الصَفّ 4).</para>
    /// </summary>
    private const string ExcludedByName = "theme-demo";

    private const string SellerPermission = "listing.create";

    /// <summary>صُفوفُ <c>TestDataSeeder.Plan</c> كَما في المَصدَر — المَشروعُ لا يُرَجِّعُ <c>apps/V1.App</c>،
    /// فَتُقرَأُ الصُفوفُ مِن مِلَفِّها بِحَرفِها، والبَذرَةُ تُسَكِّنُ أَدوارَ كُلِّ صَفٍّ مِن <see cref="RoleCatalog"/>
    /// بِـ<see cref="RoleCatalog.InstantiateRole"/> — فَهِيَ هُنا بِعَينِها.</summary>
    /// <remarks><b>وكُلُّ صَفٍّ في الكُتلَةِ يُقرَأُ أَو يَحمَرّ</b> (‏2026-10-24 — مُراجَعَةُ الموجَة ٤، بَندُ E): المُحَلِّلُ لا يَعرِفُ إلّا
    /// <c>("slug", new[] { … })</c>، فَصَفٌّ يُكتَبُ تَعبيرَ مَجموعَةٍ (<c>("x", ["vendor"])</c>) أَو <c>new string[]</c> أَو بِأَسماءِ عَناصِر كانَ
    /// يُتَخَطّى بِصَمتٍ ويَخضَرُّ العَدُّ بِدونِه؛ وصَفٌّ مُعَلَّقٌ بِـ<c>//</c> كانَ يُعَدّ. فَتُنزَعُ التَعليقاتُ أَوَّلاً، وتُعَدُّ مَداخِلُ
    /// الكُتلَةِ عَدّاً مُستَقِلّاً (فَواصِلُ المُستَوى الأَعلى خارِجَ <c>( ) { } [ ]</c>)، ويُشتَرَطُ أَن يُساوِيَ المَقروءُ المَعدود.</remarks>
    private static IReadOnlyList<(string Slug, string[] Roles)> SeederPlanRows()
    {
        var source = File.ReadAllText(Path.Combine(
            ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.App", "Seed", "TestDataSeeder.cs"));
        var (rows, entries) = ParsePlan(source);
        Assert.True(entries == rows.Count,
            $"كُتلَةُ `Plan` فيها {entries} مَدخَلاً ويُقرَأُ مِنها {rows.Count} صَفّاً — صَفٌّ بِشَكلٍ لا يَعرِفُه المُحَلِّلُ لا يُتَخَطّى.");
        return rows;
    }

    /// <summary>صُفوفُ كُتلَةِ <c>Plan</c> المَقروءَةُ بِشَكلِ <c>("slug", new[] { … })</c>، وعَدَدُ مَداخِلِها مَعدوداً بِمَعزِلٍ عَن الشَكل —
    /// بَعدَ نَزعِ التَعليقات.</summary>
    internal static (IReadOnlyList<(string Slug, string[] Roles)> Rows, int Entries) ParsePlan(string source)
    {
        var code = WriteEndpointGuardTests.StripComments(source);
        var plan = Regex.Match(code, @"\bPlan\s*=\s*\{(?<rows>.*?)\};", RegexOptions.Singleline);
        Assert.True(plan.Success, "لا كُتلَةَ `Plan` في TestDataSeeder.cs — الأَداةُ عَمياء.");
        var block = plan.Groups["rows"].Value;
        var rows = Regex.Matches(block,
                @"\(""(?<slug>[^""]+)"",\s*new\[\]\s*\{(?<roles>[^}]*)\}\)")
            .Select(m => (m.Groups["slug"].Value,
                Regex.Matches(m.Groups["roles"].Value, @"""(?<r>[^""]+)""").Select(r => r.Groups["r"].Value).ToArray()))
            .ToList();

        var entries = 0;
        var depth = 0;
        var current = new System.Text.StringBuilder();
        foreach (var c in block + ",")
        {
            if (c is '(' or '{' or '[') depth++;
            else if (c is ')' or '}' or ']') depth--;
            if (depth == 0 && c == ',')
            {
                if (current.ToString().Trim().Length > 0) entries++;
                current.Clear();
            }
            else current.Append(c);
        }
        return (rows, entries);
    }

    /// <summary><b>حَقنُ عَيب — عَدُّ صُفوفِ البَذرَةِ يُحمِرُّ فِعلاً</b> (‏2026-10-24): كُتلَةٌ بِشَكلِ <c>TestDataSeeder.Plan</c> تُحقَنُ فيها
    /// صُفوفٌ بِأَشكالٍ لا يَقرَؤُها المُحَلِّل — تَعبيرُ مَجموعَة، و<c>new string[]</c>، وأَسماءُ عَناصِر — فَيَختَلِفُ المَعدودُ عَن المَقروء؛
    /// وصَفٌّ مُعَلَّقٌ لا يُعَدُّ ولا يُقرَأ، والفاصِلَةُ الأَخيرَةُ لا تَصنَعُ مَدخَلاً.</summary>
    [Fact]
    public void The_seeder_row_count_can_actually_go_red()
    {
        const string Live = """
            public static readonly (string slug, string[] roles)[] Plan =
            {
                ("ashare", new[] { "customer", "host", "vendor" }),
                ("ejar",   new[] { "customer", "host", "vendor" }),
                ("order",  new[] { "customer", "vendor", "driver" }),
                ("injez",  new[] { "rider", "driver" }),
            };
            """;
        string With(string extra)
        {
            Assert.EndsWith("\n};", Live, StringComparison.Ordinal);
            return Live[..^2] + "    " + extra + "\n};";
        }

        var accepted = new (string Name, string Source, int Rows)[]
        {
            ("الحَيّ", Live, 4),
            ("صَفٌّ مُعَلَّق", With("// (\"ghost\", new[] { \"customer\" }),"), 4),
            ("صَفٌّ خامِسٌ بِالشَكلِ نَفسِه", With("(\"fifth\", new[] { \"customer\", \"vendor\" }),"), 5),
        };
        var rejected = new (string Name, string Source)[]
        {
            ("تَعبيرُ مَجموعَة", With("(\"fifth\", [\"customer\", \"vendor\"]),")),
            ("new string[]", With("(\"fifth\", new string[] { \"customer\", \"vendor\" }),")),
            ("أَسماءُ عَناصِر", With("(slug: \"fifth\", roles: new[] { \"customer\" }),")),
            ("ثابِتٌ بِاسمِه", With("(FifthSlug, new[] { \"customer\" }),")),
        };

        var wrong = new List<string>();
        foreach (var (name, source, expected) in accepted)
        {
            var (rows, entries) = ParsePlan(source);
            if (rows.Count != expected || entries != rows.Count) wrong.Add($"رُفِضَ حَيٌّ «{name}»: {entries} مَدخَلاً، {rows.Count} صَفّاً");
        }
        foreach (var (name, source) in rejected)
        {
            var (rows, entries) = ParsePlan(source);
            output.WriteLine($"«{name}»: {entries} مَدخَلاً، {rows.Count} صَفّاً");
            if (entries == rows.Count) wrong.Add($"قُبِلَ «{name}»: {entries} = {rows.Count}");
        }
        Assert.True(wrong.Count == 0,
            $"حُقِنَ {rejected.Length} شَكلاً و{accepted.Length} حَيّاً، وأَخطَأَ العَدُّ في {wrong.Count}:\n  " + string.Join("\n  ", wrong));
    }

    /// <summary>
    /// <para><b>تَوصيفٌ لا حارِس</b> (‏ADR-032 §٦‑ج الصَفّ 4): أَرضِيَّةُ <c>ق‑٦</c> — «أَيُّ عُضوٍ يُسَكِّنُ نَفسَه
    /// بائِعاً» — قائِمَةٌ اليَومَ بِالبِنيَةِ وَحدَها: <c>RolesFor</c> لِكُلِّ نَمَطٍ في <c>KnownPatterns</c>، وكُلُّ صَفٍّ في
    /// <c>TestDataSeeder.Plan</c>، فيه دَورٌ تُعرِضُه <see cref="SelfGrantPolicy.SelfGrantable"/> ويَحمِلُ
    /// <c>listing.create</c>. فَإن خَرَجَ نَمَطٌ أَو صَفٌّ بِلا بائِعٍ ذاتِيٍّ حَمِرَ هُنا بِاسمِه.</para>
    /// <para><b>ولا يَمنَعُ المالِكَ مِن إلغاءِ تَحديدِ <c>vendor</c></b> في <c>roles/save</c> — يَصِفُ ما يَبنيه
    /// الكودُ ويَبذُرُه، لا ما يَختارُه صاحِبُ المَتجَرِ بَعدَه. ويَطبَعُ عَدَدَ ما فَحَصَه ويَفشَلُ عَلى الصِفر
    /// (القاعِدَة ١٠).</para>
    /// </summary>
    [Fact]
    public void EveryKnownPatternAndEverySeederRow_HasASelfGrantableRoleCarryingListingCreate()
    {
        var subjects = new List<(string Name, IReadOnlyList<Role> Roles)>();
        foreach (var pattern in TenantFromAnalysisFactory.KnownPatterns)
            subjects.Add(($"نَمَط:{pattern}", TenantFromAnalysisFactory.RolesFor(pattern)));

        var rows = SeederPlanRows();
        foreach (var (slug, roleSlugs) in rows)
        {
            var roles = roleSlugs
                .Select((r, i) => RoleCatalog.Find(r) is { } t ? RoleCatalog.InstantiateRole(t, i) : null)
                .OfType<Role>()
                .ToList();
            Assert.Equal(roleSlugs.Length, roles.Count);   // ‏كُلُّ سلاجٍ في البَذرَةِ قالِبٌ في الكاتالوج
            subjects.Add(($"بَذرَة:{slug}", roles));
        }

        Assert.True(TenantFromAnalysisFactory.KnownPatterns.Count > 0, "صِفرُ نَمَطٍ مَفحوص — الأَداةُ عَمياء.");
        Assert.True(rows.Count > 0, "صِفرُ صَفِّ بَذرٍ مَقروء — الأَداةُ عَمياء.");
        Assert.DoesNotContain(rows, r => r.Slug == ExcludedByName);   // ‏الاستِثناءُ لا يُخفي صَفّاً مَعدوداً

        var bare = subjects
            .Where(s => !SelfGrantPolicy.SelfGrantable(s.Roles).Any(r => r.Permissions.Contains(SellerPermission)))
            .Select(s => s.Name)
            .ToList();

        foreach (var (name, roles) in subjects)
            output.WriteLine($"{name}: " + string.Join(", ", SelfGrantPolicy.SelfGrantable(roles)
                .Where(r => r.Permissions.Contains(SellerPermission)).Select(r => r.Slug)));
        output.WriteLine($"فُحِصَ {subjects.Count} — مُستَثنًى بِاسمِه: {ExcludedByName} — صُفوفُ البَذرَة: {rows.Count} (= مَداخِلِ كُتلَةِ Plan)");

        Assert.True(bare.Count == 0,
            $"فُحِصَ {subjects.Count} (‏{TenantFromAnalysisFactory.KnownPatterns.Count} نَمَطاً و{rows.Count} صَفَّ بَذر؛ "
          + $"«{ExcludedByName}» مُستَثنًى بِاسمِه)، ومِنها {bare.Count} بِلا دَورٍ ذاتِيِّ المَنحِ يَحمِلُ "
          + $"{SellerPermission}: {string.Join(", ", bare)}");
    }

    /// <summary><b>وسَبَبُ الاستِثناءِ قائِمٌ ما دامَ مَكتوباً</b>: <c>theme-demo</c> يُبذَرُ بِصِفرِ أَدوار — فَإن
    /// كَسَبَ أَدواراً يَوماً سَقَطَ السَبَبُ وحَمِرَ هذا الصَفّ، فَيُرفَعُ الاستِثناءُ ويُعَدُّ كَغَيرِه.</summary>
    [Fact]
    public void TheNamedExclusion_ThemeDemo_IsStillSeededWithoutRoles()
    {
        var seeder = File.ReadAllText(Path.Combine(
            ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.App", "Seed", "AppearanceBaselineSeeder.cs"));
        Assert.Contains($"public const string ThemeDemo = \"{ExcludedByName}\";", seeder, StringComparison.Ordinal);

        var at = seeder.IndexOf("Id         = ThemeDemo,", StringComparison.Ordinal);
        Assert.True(at > 0, "لا تَهيئَةَ لِـtheme-demo في AppearanceBaselineSeeder.cs — الأَداةُ عَمياء.");
        var block = InitializerBlock(seeder, seeder.LastIndexOf('{', at));
        Assert.NotNull(block);
        Assert.Contains("Roles      = new(),", block, StringComparison.Ordinal);
    }

    /// <summary>يُعيدُ نَصَّ كُتلَةِ التَهيئَةِ <c>{ … }</c> بِمُوازَنَةِ
    /// الأَقواس — لا بِعَدَدِ أَسطُرٍ مُخَمَّن.</summary>
    private static string? InitializerBlock(string text, int braceIndex)
    {
        if (braceIndex >= text.Length || text[braceIndex] != '{') return null;
        var depth = 0;
        for (var i = braceIndex; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0)
                return text[braceIndex..(i + 1)];
        }
        return null;
    }
}
