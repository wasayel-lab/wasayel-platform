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

    [Fact]
    public void CreateTenant_WithoutCategories_Fails()
    {
        var result = AgentToolValidator.Validate("create_tenant",
            """{"slug":"demo","name":"مَتجَر","color":"#1d4ed8","channel":"phone"}""");
        AssertFails(result);
    }

    [Fact]
    public void CreateTenant_ChannelEmail_Fails()
    {
        var result = AgentToolValidator.Validate("create_tenant",
            """{"slug":"demo","name":"مَتجَر","color":"#1d4ed8","channel":"email","categories":[{"slug":"cars","label":"سَيّارات"}]}""");
        AssertFails(result);
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

    private static void AssertFails(AgentToolValidationResult result)
    {
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors); // أَخطاء مَقروءَة، لا فَشَل صامِت
    }
}
