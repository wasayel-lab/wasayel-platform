using ACommerce.Templates.Customer.Marketplace.Services;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── اختِبارات بَوّابَة مُصادَقَة المُخَطَّط ─────────────────────────────
// بِذرَة T3 مِن TESTING-PROTOCOL §3: المُصادِق يَعمَل بِلا قاعِدَة
// بَيانات — حُمولَة صالِحَة دُنيا لِكُلّ أَداة تَمُرّ، وكُلّ خَرق
// لِلكاتالوج المُغلَق (دَور مُختَرَع، نَوع خاصيَّة غَير مُعتَمَد،
// قَناة غَير مَعروفَة…) يُرفَض قَبل أَيّ تَنفيذ.

public class AgentToolValidatorTests
{
    // ── حُمولات صالِحَة دُنيا — واحِدَة لِكُلّ أَداة مِن السَّبع ──
    public static TheoryData<string, string> MinimalValidPayloads => new()
    {
        {
            "create_tenant",
            """{"slug":"demo","name":"مَتجَر تَجريبيّ","color":"#1d4ed8","channel":"phone","categories":[{"slug":"cars","label":"سَيّارات"}]}"""
        },
        {
            "set_categories",
            """{"slug":"demo","categories":[{"slug":"cars","label":"سَيّارات","icon":"🚗","kind":"listing"}]}"""
        },
        { "set_branding", """{"slug":"demo"}""" },
        {
            "set_regions",
            """{"slug":"demo","cities":[{"name":"الرِّياض","districts":["المَلَز","العُلَيّا"]}]}"""
        },
        {
            "set_roles",
            """{"slug":"demo","roles":["customer","vendor"],"default_role":"customer"}"""
        },
        {
            "set_attributes",
            """{"slug":"demo","scope_id":"00000000-0000-0000-0000-000000000f01","definitions":[{"code":"color","name":"اللَّون","type":"Text"}]}"""
        },
        { "set_pwa", """{"slug":"demo","role":"customer"}""" }
    };

    [Theory]
    [MemberData(nameof(MinimalValidPayloads))]
    public void MinimalValidPayload_Passes(string tool, string json)
    {
        var result = AgentToolValidator.Validate(tool, json);
        Assert.True(result.IsValid,
            $"حُمولَة «{tool}» الصّالِحَة رُفِضَت: {string.Join(" | ", result.Errors)}");
        Assert.Empty(result.Errors);
    }

    // ── حالات الفَشَل الإلزاميَّة ──

    [Fact]
    public void SetRoles_RoleOutsideClosedEnum_Fails()
    {
        var result = AgentToolValidator.Validate("set_roles",
            """{"slug":"demo","roles":["wizard"]}""");
        AssertFails(result);
    }

    // ── تَعداد set_roles مُشتَقّ مِن الكاتالوج لا مَنسوخ ──────────────
    // كانَ التَّعداد مَصفوفَة أَسماء مَكتوبَة في المُخَطَّط، ومَعَها وَصف
    // عَرَبيّ يُكَرِّرُها وتَعليق يُكَرِّرُها ثالِثَةً — وكانَ التَّعليق
    // ناقِصاً دَوراً (rider) فِعلاً، وهو أَثَر النَّسخ لا مُصادَفَة.
    // صارَ يُشتَقّ مِن RoleCatalog.All، وهذانِ الاختِبارانِ يَحرُسانِ
    // الطَرَفَين: أَنّ **كُلّ** ما في الكاتالوج يَمُرّ، وأَنّ ما ليسَ
    // فيه لا يَمُرّ. فَدَور يُؤَلَّف مِلَفّاً يَصير مَقبولاً بِلا لَمس
    // سَطر هُنا، ودَور مَحذوف يَصير مَرفوضاً بِلا لَمس سَطر هُنا.

    [Fact]
    public void SetRoles_AcceptsEveryCatalogSlug()
    {
        var all = ACommerce.Kit.Roles.RoleCatalog.All.Select(t => t.Slug).ToArray();
        Assert.NotEmpty(all);

        // كُلّ الكاتالوج دُفعَةً واحِدَة — وهو أَقوى مِن دَور دَور:
        // يُثبِت أَنّ التَّعداد يَسَعُهُم جَميعاً لا أَنّ كُلّاً مِنهُم
        // يُوافِق تَعداداً قَد يَكون أَوسَع.
        var json = "{\"slug\":\"demo\",\"roles\":["
                 + string.Join(",", all.Select(s => $"\"{s}\"")) + "]}";
        var result = AgentToolValidator.Validate("set_roles", json);
        Assert.True(result.IsValid,
            $"سُلِّم الكاتالوج كامِلاً فَرُفِض: {string.Join(" | ", result.Errors)}");
    }

    [Fact]
    public void CreateTenant_WithoutCategories_Fails()
    {
        var result = AgentToolValidator.Validate("create_tenant",
            """{"slug":"demo","name":"مَتجَر","color":"#1d4ed8","channel":"phone"}""");
        AssertFails(result);
    }

    // كانَ هذا الاختِبار يَحرُس رَفض "email" — ومُنذُ مَوجَة قَناة البَريد
    // صارَت قيمَةً صالِحَة، فَانتَقَلَ الحَرَس إلى قيمَة أُخرى خارِج
    // التَعداد. الغَرَض نَفسه: إثبات أَنّ التَعداد مُغلَق فِعلاً لا
    // مُجَرَّد وَصف.
    [Fact]
    public void CreateTenant_ChannelOutsideClosedEnum_Fails()
    {
        var result = AgentToolValidator.Validate("create_tenant",
            """{"slug":"demo","name":"مَتجَر","color":"#1d4ed8","channel":"telegram","categories":[{"slug":"cars","label":"سَيّارات"}]}""");
        AssertFails(result);
    }

    [Fact]
    public void SetBranding_ChannelOutsideClosedEnum_Fails()
    {
        var result = AgentToolValidator.Validate("set_branding",
            """{"slug":"demo","channel":"telegram"}""");
        AssertFails(result);
    }

    // ── قَناة البَريد: قيمَة صالِحَة في الأَداتَين مَعاً ──
    // الحارِس المُقابِل لِلاختِبار السالِب أَعلاه: لَو سَقَطَت "email" مِن
    // أَحَد المُخَطَّطَين لَسَقَطَ هذا فَوراً.

    [Fact]
    public void CreateTenant_ChannelEmail_Passes()
    {
        var result = AgentToolValidator.Validate("create_tenant",
            """{"slug":"demo","name":"مَتجَر","color":"#1d4ed8","channel":"email","categories":[{"slug":"cars","label":"سَيّارات"}]}""");
        Assert.True(result.IsValid,
            $"قَناة البَريد صالِحَة بَعد مَوجَة المُصادَقَة البَريديَّة: {string.Join(" | ", result.Errors)}");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void SetBranding_ChannelEmail_Passes()
    {
        var result = AgentToolValidator.Validate("set_branding",
            """{"slug":"demo","channel":"email"}""");
        Assert.True(result.IsValid,
            $"تَحويل قَناة مَتجَر قائِم إلى البَريد يَجِب أَن يَمُرّ: {string.Join(" | ", result.Errors)}");
    }

    [Fact]
    public void SetAttributes_UnsupportedType_Fails()
    {
        var result = AgentToolValidator.Validate("set_attributes",
            """{"slug":"demo","scope_id":"00000000-0000-0000-0000-000000000f01","definitions":[{"code":"c","name":"لَون","type":"Color"}]}""");
        AssertFails(result);
    }

    [Fact]
    public void UnknownToolName_Fails()
    {
        var result = AgentToolValidator.Validate("make_coffee", """{"slug":"demo"}""");
        AssertFails(result);
    }

    [Fact]
    public void MalformedJson_Fails()
    {
        var result = AgentToolValidator.Validate("set_branding", "{ this is not json");
        AssertFails(result);
    }

    // ── قُيود رُمِّزَت رَسميّاً في المُخَطَّطات (2026-08-09):
    //    نَمَط الـ slug، نَمَط اللَّون hex، وحَدّ حَجم الأَيقونَة ──

    [Fact]
    public void CreateTenant_SlugWithSpaces_Fails()
    {
        var result = AgentToolValidator.Validate("create_tenant",
            """{"slug":"bad slug!","name":"مَتجَر","color":"#1d4ed8","channel":"phone","categories":[{"slug":"cars","label":"سَيّارات"}]}""");
        AssertFails(result);
    }

    [Fact]
    public void CreateTenant_ColorNotHex_Fails()
    {
        var result = AgentToolValidator.Validate("create_tenant",
            """{"slug":"demo","name":"مَتجَر","color":"blue","channel":"phone","categories":[{"slug":"cars","label":"سَيّارات"}]}""");
        AssertFails(result);
    }

    [Fact]
    public void SetBranding_ColorMalformedHex_Fails()
    {
        var result = AgentToolValidator.Validate("set_branding",
            """{"slug":"demo","color":"#GGGGGG"}""");
        AssertFails(result);
    }

    [Fact]
    public void SetPwa_IconExceedsEncodedSizeLimit_Fails()
    {
        var oversized = "data:image/png;base64," + new string('A', 360_000);
        var result = AgentToolValidator.Validate("set_pwa",
            $$"""{"slug":"demo","role":"customer","pwa_icon_url":"{{oversized}}"}""");
        AssertFails(result);
    }

    [Fact]
    public void SetPwa_EmptyIcon_DeleteSemantics_StillPasses()
    {
        var result = AgentToolValidator.Validate("set_pwa",
            """{"slug":"demo","role":"customer","pwa_icon_url":""}""");
        Assert.True(result.IsValid,
            $"سِلسِلَة فارِغَة = حَذف — يَجِب أَن تَمُرّ: {string.Join(" | ", result.Errors)}");
    }

    private static void AssertFails(AgentToolValidationResult result)
    {
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors); // أَخطاء مَقروءَة، لا فَشَل صامِت
    }
}
