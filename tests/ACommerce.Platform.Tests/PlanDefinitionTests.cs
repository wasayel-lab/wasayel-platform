using ACommerce.Kit.Subscriptions;
using ACommerce.Platform.Flows;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>الباقَة تَعريفاً كَبَيانات</b> — بَوّابَتُها بِرُموز خَرق
/// ثابِتَة، ولِكُلّ رَمز <b>اختِبار مُوجِب واختِبار سالِب</b>
/// (القاعِدَة ٤). ومَعَها بُرهان التَكافُؤ الصِفريّ: الطَبَقَة تُضاف
/// ولا تُبَدِّل.</para>
/// </summary>
public class PlanDefinitionTests
{
    private static PlanDefinition Green(
        string slug = "tajer", decimal price = 49m, int quota = 10, int days = 30)
        => new(slug,
               new PlanText("تاجِر", "Merchant"),
               new PlanText("عَشَرَة إعلانات شَهرِيّاً"),
               price, quota, days);

    // ─── المُوجِب ─────────────────────────────────────────────────────

    [Fact]
    public void A_sound_definition_validates_clean()
    {
        Assert.Empty(PlanDefinitionValidator.Validate(Green()));
        Assert.True(PlanDefinitionValidator.IsValid(Green()));
        Assert.True(PlanDefinitionValidator.IsValidTenantDefinition(Green()));
    }

    /// <summary>الحِصَّة صِفراً مَسموحَة — باقَة عَرضٍ بِلا نَشر شَيءٌ
    /// مَعقول. والسالِب وَحدَه خَرق.</summary>
    [Fact]
    public void Zero_quota_and_zero_price_are_allowed()
    {
        Assert.Empty(PlanDefinitionValidator.Validate(Green(quota: 0, price: 0m)));
    }

    // ─── السالِب: رَمزٌ رَمزٌ ─────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void slug_empty(string slug)
        => Assert.Contains("slug_empty", Codes(Green(slug: slug)));

    [Theory]
    [InlineData("Tajer")]      // حَرف كَبير
    [InlineData("1tajer")]     // يَبدَأ بِرَقَم
    [InlineData("tajer-pro")]  // شَرطَة
    [InlineData("تاجر")]        // عَرَبيّ
    public void slug_pattern(string slug)
        => Assert.Contains("slug_pattern", Codes(Green(slug: slug)));

    [Fact]
    public void localized_arabic_missing_on_label_and_on_description()
    {
        Assert.Contains("localized_arabic_missing",
            Codes(Green() with { Label = new PlanText("") }));
        Assert.Contains("localized_arabic_missing",
            Codes(Green() with { Description = new PlanText("  ") }));
    }

    [Fact]
    public void price_negative()
        => Assert.Contains("price_negative", Codes(Green(price: -1m)));

    [Fact]
    public void quota_negative()
        => Assert.Contains("quota_negative", Codes(Green(quota: -1)));

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void period_not_positive(int days)
        => Assert.Contains("period_not_positive", Codes(Green(days: days)));

    [Fact]
    public void period_too_long()
        => Assert.Contains("period_too_long",
            Codes(Green(days: PlanDefinitionValidator.MaxDaysPeriod + 1)));

    /// <summary>وحَدُّ السَقف نَفسُه مَسموح — الشَرط <c>&gt;</c> لا
    /// <c>&gt;=</c>، وهذا يُثَبِّتُه.</summary>
    [Fact]
    public void The_ceiling_itself_is_allowed()
        => Assert.Empty(PlanDefinitionValidator.Validate(
            Green(days: PlanDefinitionValidator.MaxDaysPeriod)));

    /// <summary><b>لا تُظَلَّل باقَةٌ مَبذورَة</b> — ولا يَقَع هذا الخَرق
    /// إلّا في بَوّابَة المُستَأجِر، فَباقات البَذر نَفسُها تَمُرّ مِن
    /// <c>Validate</c> عِندَ الإقلاع ولَو كانَ الفَحص فيها لَرَفَضَت
    /// كُلٌّ مِنها نَفسَها.</summary>
    [Theory]
    [InlineData("free")]
    [InlineData("basic")]
    [InlineData("pro")]
    public void slug_shadows_seeded_plan_fires_only_at_the_tenant_gate(string slug)
    {
        Assert.DoesNotContain("slug_shadows_seeded_plan", Codes(Green(slug: slug)));
        Assert.Contains("slug_shadows_seeded_plan", TenantCodes(Green(slug: slug)));
    }

    // ─── القِراءَة والكِتابَة — ذَهاباً وإياباً ────────────────────────

    [Fact]
    public void A_definition_survives_a_round_trip_through_json()
    {
        var round = PlanDefinitionLoader.ParseDefinition(PlanDefinitionLoader.ToJson(Green()));
        Assert.Equal(Green(), round);
    }

    /// <summary>واللُغَة الثانِيَة <b>تَبقى مُخَزَّنَة</b>: الاختِيار
    /// يَقَع عِندَ التَصيير لا عِندَ التَسَلسُل — ولَو وَقَعَ عِندَ
    /// التَسَلسُل لَفُقِدَت.</summary>
    [Fact]
    public void The_second_language_survives_serialisation()
    {
        var round = PlanDefinitionLoader.ParseDefinition(PlanDefinitionLoader.ToJson(Green()));
        Assert.Equal("Merchant", round.Label.En);
        Assert.Equal("تاجِر", round.Label.Current);
    }

    // ─── المَعجَم: لا حالات رابِعَة ───────────────────────────────────

    /// <summary>حالات الباقَة هي حالات الاعتِماد نَفسُها — <b>مَوضِعاً
    /// واحِداً</b>، لا نُسخَة ثالِثَة تَنحَرِف.</summary>
    [Fact]
    public void Plan_statuses_are_the_one_shared_approval_vocabulary()
    {
        Assert.Equal(ApprovalFlow.All, TenantPlanStatuses.All);
        Assert.Same(ApprovalFlow.All,  TenantPlanStatuses.All);
        Assert.Equal(ApprovalFlow.All, ACommerce.Kit.Roles.TenantRoleStatuses.All);
        Assert.Same(TenantPlanStatuses.All, ACommerce.Kit.Roles.TenantRoleStatuses.All);
    }

    /// <summary>ووَثيقَةُ الباقَة تَحمِل الشَكل المُشتَرَك — كَوَثيقَتَي
    /// الدَور والمَظهَر.</summary>
    [Fact]
    public void All_three_tenant_documents_share_one_shape()
    {
        Assert.True(typeof(ITenantDefinitionDocument)
            .IsAssignableFrom(typeof(TenantPlanDefinition)));
        Assert.True(typeof(ITenantDefinitionDocument)
            .IsAssignableFrom(typeof(ACommerce.Kit.Roles.TenantRoleDefinition)));
        Assert.True(typeof(ITenantDefinitionDocument)
            .IsAssignableFrom(typeof(ACommerce.Kit.Theme.TenantThemeDefinition)));
    }

    // ─── التَكافُؤ الصِفريّ ───────────────────────────────────────────

    /// <summary>
    /// <para><b>مُستَأجِر بِلا تَعريف واحِد يُعطي نَفس المَرجِع</b> — لا
    /// نُسخَةً مُتَساوِيَة. أَي أَنّ صَفحَة الباقات لِكُلّ مُستَأجِر
    /// قائِم اليَوم لا تَمُرّ بِسَطر مَنطِق واحِد إضافيّ.</para>
    /// </summary>
    [Fact]
    public void Zero_equivalence_returns_the_very_same_reference()
    {
        var stored = new[] { new Plan { Id = "free", Price = 0, ListingsQuota = 1 } };
        var merged = TenantPlanSet.Platform.Merge(stored);
        Assert.Same(stored, merged);
    }

    /// <summary>ولَقطَةٌ مِن صِفر وَثيقَة هي لَقطَة المَنصَّة
    /// بِعَينِها.</summary>
    [Fact]
    public void No_documents_gives_the_platform_snapshot()
    {
        Assert.Same(TenantPlanSet.Platform,
            TenantPlanSet.FromDocuments(null, Array.Empty<TenantPlanDefinition>()));
        Assert.Empty(TenantPlanSet.FromDocuments("t", Array.Empty<TenantPlanDefinition>()).Authored);
    }

    /// <summary>والمُعتَمَد وَحدَه يُقرَأ — المُعَلَّق والمَرفوض لا
    /// يَبلُغانِ سَطحاً.</summary>
    [Fact]
    public void Only_approved_documents_are_read()
    {
        var docs = new[]
        {
            Doc("alfa",  TenantPlanStatuses.Approved),
            Doc("beta",  TenantPlanStatuses.Pending),
            Doc("gamma", TenantPlanStatuses.Rejected),
        };

        var set = TenantPlanSet.FromDocuments("t", docs);
        Assert.Equal(new[] { "alfa" }, set.Authored.Select(p => p.Slug).ToArray());
    }

    /// <summary>ووَثيقَةٌ فاسِدَة تُتَجاهَل ولا تُفشِل الطَلَب — حِزام
    /// أَمان ثانٍ لِوَثيقَة كُتِبَت بِيَد أَو نَجَت مِن تَرحيل.</summary>
    [Fact]
    public void A_corrupt_document_is_ignored_not_thrown()
    {
        var docs = new[]
        {
            new TenantPlanDefinition
            {
                Id = "bad", Slug = "bad", Status = TenantPlanStatuses.Approved,
                DefinitionJson = "{ not json"
            },
            Doc("alfa", TenantPlanStatuses.Approved),
        };

        var set = TenantPlanSet.FromDocuments("t", docs);
        Assert.Equal(new[] { "alfa" }, set.Authored.Select(p => p.Slug).ToArray());
    }

    /// <summary>ووَثيقَةٌ مُعتَمَدَة لكِنَّها لا تَجتاز المُصادَقَة
    /// تُتَجاهَل كَذلك — حِصَّةٌ سالِبَة لا تَصير رَصيداً.</summary>
    [Fact]
    public void An_approved_but_invalid_document_is_ignored()
    {
        var bad = PlanDefinitionLoader.ToJson(Green(slug: "salib", quota: -5));
        var docs = new[]
        {
            new TenantPlanDefinition
            {
                Id = "salib", Slug = "salib",
                Status = TenantPlanStatuses.Approved, DefinitionJson = bad
            },
        };

        Assert.Empty(TenantPlanSet.FromDocuments("t", docs).Authored);
    }

    /// <summary>والتَعريف يُضاف فَوق المُخَزَّن ولا يُظَلِّلُه: سلاجٌ
    /// مَوجود يَفوز لِلوَثيقَة الحَيَّة.</summary>
    [Fact]
    public void Authored_plans_are_added_above_and_never_shadow()
    {
        var docs  = new[] { Doc("alfa", TenantPlanStatuses.Approved),
                            Doc("free", TenantPlanStatuses.Approved) };
        var set   = TenantPlanSet.FromDocuments("t", docs);
        var stored = new[] { new Plan { Id = "free", Name = "المُخَزَّنَة", Price = 0 } };

        var merged = set.Merge(stored);

        Assert.Equal(new[] { "free", "alfa" }, merged.Select(p => p.Id).ToArray());
        Assert.Equal("المُخَزَّنَة", merged.Single(p => p.Id == "free").Name);
    }

    // ─── شَرط الاستِخراج: ثَلاثَة مُستَهلِكين ─────────────────────────

    /// <summary>
    /// <para><b>القاعِدَة ١ مَفروضَةً لا مَوصوفَة</b>: القالِب المُشتَرَك
    /// لَه <b>ثَلاثَة</b> مُستَهلِكين في وَقت التَشغيل — الأَدوار
    /// والمَظهَر والباقات. كانَ اثنَين قَبل هذه المَوجَة، والاستِخراج
    /// حينَها كانَ سَيَكون تَجريداً يَسبِق مُستَهلِكَه — أَي العَطَب
    /// الَّذي تُعالِجُه هذه المَوجَة نَفسُها.</para>
    ///
    /// <para><b>ونُقصانُهُم يُحَمِّر</b>: لَو حُذِفَ مُستَهلِك صارَ
    /// القالِبُ تَجريداً لِاثنَين — وذلك قَرار يَستَحِقّ نَظَرَ إنسان،
    /// لا تَعديلاً يَمُرّ.</para>
    /// </summary>
    [Fact]
    public void The_shared_template_has_exactly_three_runtime_consumers()
    {
        var baseType = typeof(ACommerce.Templates.Customer.Marketplace.Services
            .TenantDefinitionService<,>);

        var consumers = typeof(ACommerce.Templates.Customer.Marketplace.Services.TenantPlanService)
            .Assembly.GetTypes()
            .Where(t => !t.IsAbstract && Derives(t, baseType))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "TenantPlanService", "TenantRoleService", "TenantThemeService" },
            consumers);
    }

    private static bool Derives(Type t, Type openGeneric)
    {
        for (var b = t.BaseType; b is not null; b = b.BaseType)
            if (b.IsGenericType && b.GetGenericTypeDefinition() == openGeneric)
                return true;
        return false;
    }

    // ─── أَدَوات ──────────────────────────────────────────────────────

    private static TenantPlanDefinition Doc(string slug, string status) => new()
    {
        Id = slug, Slug = slug, Status = status,
        DefinitionJson = PlanDefinitionLoader.ToJson(Green(slug: slug))
    };

    private static IEnumerable<string> Codes(PlanDefinition d)
        => PlanDefinitionValidator.Validate(d).Select(v => v.Code);

    private static IEnumerable<string> TenantCodes(PlanDefinition d)
        => PlanDefinitionValidator.ValidateTenantDefinition(d).Select(v => v.Code);
}
