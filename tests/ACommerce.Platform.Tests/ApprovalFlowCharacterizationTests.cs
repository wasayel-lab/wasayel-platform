using ACommerce.Kit.Roles;
using ACommerce.Kit.Theme;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── تَوصيف مَسارات القَرار الثَلاثَة (Characterization) ────────────────
// يُكتَب هذا المِلَفّ ويَخضَرّ **قَبل** أَن يُبَدَّل حَرف، ثُمَّ **لا
// يُمَسّ سَطر واحِد مِنه** بَعدَ التَبديل. مُرورُه عَلى الحالَتَين هو
// بُرهان أَنّ التَوحيد لَم يُغَيِّر سُلوكاً — لا أَنّ الجَديد يُطابِق
// نَصّاً كَتَبتُه أَنا.
//
// نَفس مَنهَجِيَّة DealsPolicyCharacterizationTests حَرفاً.
//
// **والدَعوى المَفحوصَة مَحدودَة بِدِقَّة**: مَعجَم الحالات وسُلوك
// الالتِقاط (مَن يُقرَأ ومَن يُهمَل ومَن يَغلِب). أَمّا الكِتابَة
// (ProposeAsync/DecideAsync) فَتَحتاج Marten حَيّاً، ولا قاعِدَة
// بَيانات في هذه الحُزمَة — فَحارِسُها هو أَنّ الدالَّتَين **لَم
// تُمَسّا** في هذه المَوجَة، والـ diff يُبَرهِن ذلِك.

public class ApprovalFlowCharacterizationTests
{
    // ─── ١) مَعجَم الحالات — الثابِت المُشتَرَك ─────────────────────────

    /// <summary>الأَدوار والمَظهَر: ثَلاث حالات بِنَفس المَفاتيح
    /// وبِنَفس التَرتيب. هذا هو التَطابُق الَّذي يُبَرِّر التَوحيد.</summary>
    [Fact]
    public void Role_and_theme_vocabularies_are_identical()
    {
        Assert.Equal(new[] { "pending", "approved", "rejected" }, TenantRoleStatuses.All);
        Assert.Equal(new[] { "pending", "approved", "rejected" }, TenantThemeStatuses.All);
        Assert.Equal(TenantRoleStatuses.All, TenantThemeStatuses.All);

        Assert.Equal("pending",  TenantRoleStatuses.Pending);
        Assert.Equal("approved", TenantRoleStatuses.Approved);
        Assert.Equal("rejected", TenantRoleStatuses.Rejected);
        Assert.Equal("pending",  TenantThemeStatuses.Pending);
        Assert.Equal("approved", TenantThemeStatuses.Approved);
        Assert.Equal("rejected", TenantThemeStatuses.Rejected);
    }

    /// <summary>العُضوِيَّة <b>حَسّاسَة لِلحالَة</b> (Ordinal) في
    /// المَعجَمَين — وهذا سُلوك قائِم يُثَبَّت لِئَلّا يَنقَلِب إلى
    /// مُقارَنَة مُتَساهِلَة عِندَ التَوحيد.</summary>
    [Theory]
    [InlineData("pending",  true)]
    [InlineData("approved", true)]
    [InlineData("rejected", true)]
    [InlineData("Approved", false)]
    [InlineData("APPROVED", false)]
    [InlineData("applied",  false)]
    [InlineData("",         false)]
    [InlineData("draft",    false)]
    public void Membership_is_ordinal_in_both(string probe, bool expected)
    {
        Assert.Equal(expected, TenantRoleStatuses.Contains(probe));
        Assert.Equal(expected, TenantThemeStatuses.Contains(probe));
    }

    /// <summary>الافتِراضيّ عِندَ الإنشاء <c>pending</c> في
    /// الوَثيقَتَين — لا حالَة أُخرى تُكتَب مِن مُنشِئ.</summary>
    [Fact]
    public void New_documents_default_to_pending()
    {
        Assert.Equal("pending", new TenantRoleDefinition().Status);
        Assert.Equal("pending", new TenantThemeDefinition().Status);
    }

    // ─── ٢) الالتِقاط — المَقروء واحِد فَقَط ────────────────────────────

    private static TenantRoleDefinition RoleDoc(string slug, string status, string json) =>
        new() { Id = slug, Slug = slug, Status = status, DefinitionJson = json,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };

    /// <summary>تَعريف دَور سَليم أَدنى — يَجتاز
    /// <c>RoleDefinitionValidator.ValidateTenantDefinition</c> بِنَفس
    /// شَكل مِلَفّات <c>Definitions/*.role.json</c>.</summary>
    private static string RoleJson(string slug) => $$"""
    {
      "slug": "{{slug}}",
      "icon": "🧪",
      "homeRoute": "",
      "label": { "ar": "دَور تَجريبيّ", "en": null },
      "description": { "ar": "دَور لِلتَوصيف لا لِلإنتاج.", "en": null },
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

    /// <summary><b>المُعَلَّق والمَرفوض لا يَبلُغان أَيّ سَطح</b> —
    /// وهذا هو العَقد المُعلَن في وَثيقَتَي الحالات. يُثَبَّت هُنا
    /// بِالمَسار لا بِالتَعليق.</summary>
    [Fact]
    public void Only_approved_role_documents_are_picked_up()
    {
        var set = TenantRoleSet.FromDocuments("probe", new[]
        {
            RoleDoc("alpha", TenantRoleStatuses.Pending,  RoleJson("alpha")),
            RoleDoc("beta",  TenantRoleStatuses.Rejected, RoleJson("beta")),
            RoleDoc("gamma", TenantRoleStatuses.Approved, RoleJson("gamma")),
        });

        Assert.Equal(new[] { "gamma" }, set.TenantAuthored.Select(d => d.Slug).ToArray());
    }

    /// <summary>حالَة خارِج المَعجَم لا تُقرَأ أَيضاً — المُرَشِّح
    /// تَطابُق حَرفيّ مَع <c>approved</c>، لا «ليسَ مَرفوضاً».</summary>
    [Fact]
    public void Role_documents_with_out_of_vocabulary_status_are_ignored()
    {
        var set = TenantRoleSet.FromDocuments("probe", new[]
        {
            RoleDoc("alpha", "Approved", RoleJson("alpha")),   // حَرف كَبير
            RoleDoc("beta",  "applied",  RoleJson("beta")),    // مِن مَعجَم الوَكيل
        });

        Assert.Empty(set.TenantAuthored);
    }

    /// <summary>بِلا وَثيقَة واحِدَة: <b>نَفس المَرجِع</b> لا نُسخَة —
    /// التَكافُؤ الصِفريّ بِالهُوِيَّة. (مَحروس أَصلاً في
    /// TenantRoleZeroEquivalenceTests؛ يُعاد هُنا لِأَنّ التَوحيد
    /// يَمَسّ نَفس المَسار.)</summary>
    [Fact]
    public void No_role_documents_yields_the_platform_snapshot()
    {
        Assert.Same(TenantRoleSet.Platform,
            TenantRoleSet.FromDocuments(null, Array.Empty<TenantRoleDefinition>()));
        Assert.Same(RoleCatalog.Definitions,
            TenantRoleSet.FromDocuments("probe", Array.Empty<TenantRoleDefinition>()).Definitions);
    }

    private static TenantThemeDefinition ThemeDoc(
        string slug, string status, string json, DateTime? decidedAt = null) =>
        new() { Id = slug, Slug = slug, Status = status, DefinitionJson = json,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                DecidedAt = decidedAt };

    private static string ThemeJson(string slug) => $$"""
    {
      "slug": "{{slug}}",
      "label": { "ar": "مَظهَر تَجريبيّ", "en": null },
      "tokens": { "color.primary": "#123456" }
    }
    """;

    [Fact]
    public void Only_approved_theme_documents_are_picked_up()
    {
        var set = TenantThemeSet.FromDocuments("probe", new[]
        {
            ThemeDoc("alpha", TenantThemeStatuses.Pending,  ThemeJson("alpha")),
            ThemeDoc("beta",  TenantThemeStatuses.Rejected, ThemeJson("beta")),
        });

        Assert.Null(set.TenantAuthored);
    }

    /// <summary><b>آخِر مُعتَمَد بِتاريخ القَرار يَغلِب</b> — قاعِدَة
    /// مُعلَنَة في وَثيقَة المَظهَر، ولا مُقابِل لَها في الأَدوار (هُناك
    /// تُجمَع كُلُّها). فَرق سُلوكيّ حَقيقيّ بَينَ التَدَفُّقَين
    /// المُتَطابِقَي المَعجَم — يُثَبَّت هُنا لِأَنّ التَوحيد يَجِب
    /// أَلّا يَمَسَّه.</summary>
    [Fact]
    public void Latest_decided_approved_theme_wins()
    {
        var set = TenantThemeSet.FromDocuments("probe", new[]
        {
            ThemeDoc("old", TenantThemeStatuses.Approved, ThemeJson("old"),
                new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
            ThemeDoc("new", TenantThemeStatuses.Approved, ThemeJson("new"),
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)),
        });

        Assert.NotNull(set.TenantAuthored);
        Assert.Equal("new", set.TenantAuthored!.Slug);
    }

    [Fact]
    public void No_theme_documents_yields_the_platform_snapshot()
    {
        Assert.Same(TenantThemeSet.Platform,
            TenantThemeSet.FromDocuments(null, Array.Empty<TenantThemeDefinition>()));
        Assert.Same(ThemeCatalog.Default,
            TenantThemeSet.FromDocuments("probe", Array.Empty<TenantThemeDefinition>()).Theme);
    }

    // ─── ٣) الثالِث ليسَ مِنهُما ────────────────────────────────────────

    /// <summary>
    /// <para>مَعجَم أَداة الوَكيل <b>أَربَعَة لا ثَلاثَة</b>، وحالَة
    /// نَجاحِه <c>applied</c> لا <c>approved</c>. لا مَعجَم مُغلَق
    /// يُعلِنُه في الكود — ولِذلِك تُكتَب القيَم هُنا حَرفِيّاً كَما
    /// تَظهَر في <c>AgentService.cs:35</c> وفي المَواضِع الَّتي
    /// تَكتُبُها.</para>
    ///
    /// <para><b>وهذا الاختِبار هو الحارِس الَّذي يَمنَع ضَمَّه</b>:
    /// لَو وُحِّدَ الثَلاثَة عَلى مَعجَم الأَدوار لَصارَ
    /// <c>applied</c> هو <c>approved</c> — تَغيير سُلوك في وَثائِق
    /// مُخَزَّنَة قائِمَة، لا إعادَة تَنظيم.</para>
    /// </summary>
    [Fact]
    public void Agent_tool_vocabulary_is_not_the_approval_vocabulary()
    {
        var agentStatuses = new[] { "pending", "applied", "rejected", "error" };

        Assert.NotEqual(TenantRoleStatuses.All, agentStatuses);
        Assert.Equal(4, agentStatuses.Length);
        Assert.Contains("applied", agentStatuses);
        Assert.DoesNotContain("approved", agentStatuses);

        // ولا واحِدَة مِن حالَتَيه الزائِدَتَين مَقبولَة في مَعجَم
        // الاعتِماد — فَالضَمّ كانَ سَيَحتاج تَوسيعَ المَعجَم لِلجَميع.
        Assert.False(TenantRoleStatuses.Contains("applied"));
        Assert.False(TenantRoleStatuses.Contains("error"));
        Assert.False(TenantThemeStatuses.Contains("applied"));
        Assert.False(TenantThemeStatuses.Contains("error"));
    }
}
