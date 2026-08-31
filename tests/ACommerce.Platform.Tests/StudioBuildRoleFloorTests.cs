using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

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
public class StudioBuildRoleFloorTests
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
    /// </summary>
    [Fact]
    public void LegacyMode_IsUntouched_AndItsConsumerIsNamed()
    {
        Assert.True(RolePermissions.Has(Array.Empty<Role>(), "", "tenant.manage"));

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
