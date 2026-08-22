using ACommerce.Templates.Customer.Marketplace.Services.Api;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>عَقدُ مِفتاح الـAPI — دَوالُّ نَقِيَّة، بِمُوجِبٍ وسالِبٍ
/// لِكُلّ رَمز خَرق</b> (القاعِدَة ٤). لا قاعِدَةَ بَيانات، ولا
/// وَقتَ إلّا مُمَرَّراً، ولا عَشوائيَّة — فَما يَخضَرُّ هُنا
/// يَخضَرُّ على أَيّ جِهاز.</para>
/// </summary>
public class ApiKeyContractTests
{
    // ─── مَعجَمُ النِطاقات ─────────────────────────────────────────────

    /// <summary><b>نِطاقانِ ولا ثالِث.</b> ونُمُوُّ القائِمَةِ قَرارٌ
    /// مَرئيّ: نِطاقٌ لا تَفحَصُه نُقطَةٌ حَيَّةٌ هو
    /// <c>AllowCustomPattern</c> مِن جَديد — يُباع ولا يُفرَض.</summary>
    [Fact]
    public void Exactly_two_scopes_and_they_are_these()
        => Assert.Equal(new[] { "deals:read", "deals:write" }, ApiScopeCatalog.All);

    [Fact]
    public void The_scope_vocabulary_is_distinct_and_ordinally_sorted()
    {
        Assert.Equal(ApiScopeCatalog.All.Distinct(StringComparer.Ordinal), ApiScopeCatalog.All);
        Assert.Equal(
            ApiScopeCatalog.All.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
            ApiScopeCatalog.All);
    }

    [Theory]
    [InlineData("deals:read")]
    [InlineData("deals:write")]
    public void Catalog_scopes_pass_the_composition_gate(string scope)
        => Assert.Equal(scope, ApiScopeCatalog.Require(scope));

    [Theory]
    [InlineData("deals")]           // ناقِص
    [InlineData("Deals:Read")]      // حالَةُ الحَرف تَهُمّ
    [InlineData("deals:read ")]     // مَسافَة لاحِقَة
    [InlineData("listings:write")]  // مَورِدٌ لَم يُكشَف بَعد
    [InlineData("*")]
    public void Anything_outside_the_scope_vocabulary_throws_at_composition(string scope)
    {
        Assert.False(ApiScopeCatalog.Contains(scope));
        var ex = Assert.Throws<ArgumentException>(() => ApiScopeCatalog.Require(scope));
        foreach (var s in ApiScopeCatalog.All) Assert.Contains(s, ex.Message);
    }

    // ─── بَوّابَةُ الإصدار: مُوجِب وسالِب لِكُلّ رَمز ───────────────────

    private static ApiKeyValidator.IssueRequest Valid(
        string name = "تَكامُلُ الناقِل",
        Guid? actor = null,
        string[]? scopes = null,
        int? days = 90)
        => new(name, actor ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
               "شَرِكَةُ النَقل", scopes ?? new[] { ApiScopeCatalog.DealsWrite }, days);

    [Fact]
    public void A_well_formed_issue_request_validates_clean()
    {
        Assert.Empty(ApiKeyValidator.Validate(Valid()));
        Assert.True(ApiKeyValidator.IsValid(Valid()));
    }

    /// <summary>وبِلا انتِهاء — <c>null</c> صَريحٌ لا خَطَأ: مِفتاحٌ
    /// دائِمٌ يُبطَل بِزِرِّه.</summary>
    [Fact]
    public void A_key_without_an_expiry_is_valid()
        => Assert.Empty(ApiKeyValidator.Validate(Valid(days: null)));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_empty_fires_on_blank(string name)
        => Assert.Contains("name_empty",
            ApiKeyValidator.Validate(Valid(name: name)).Select(v => v.Code));

    [Fact]
    public void Name_too_long_fires_past_sixty_characters()
        => Assert.Contains("name_too_long",
            ApiKeyValidator.Validate(Valid(name: new string('م', 61))).Select(v => v.Code));

    /// <summary><b>مِفتاحٌ بِلا فاعِلٍ لا يُحَرِّك شَيئاً</b> —
    /// و<c>DealPatternCatalog.DefaultActors</c> هُوَ البُرهان: لا
    /// مَرحَلَةَ واحِدَة مُسنَدَةٌ إلى <c>platform</c>.</summary>
    [Fact]
    public void Actor_required_fires_on_an_empty_guid()
        => Assert.Contains("actor_required",
            ApiKeyValidator.Validate(Valid(actor: Guid.Empty)).Select(v => v.Code));

    [Fact]
    public void The_reason_the_actor_is_required_is_measured_not_asserted()
        => Assert.DoesNotContain(
            ACommerce.Templates.Customer.Marketplace.Services.Deals.DealPatternCatalog.PlatformActor,
            ACommerce.Templates.Customer.Marketplace.Services.Deals.DealPatternCatalog.DefaultActors.Values);

    [Fact]
    public void Scopes_empty_fires_on_an_empty_list()
        => Assert.Contains("scopes_empty",
            ApiKeyValidator.Validate(Valid(scopes: Array.Empty<string>())).Select(v => v.Code));

    [Fact]
    public void Scope_out_of_vocabulary_fires_on_an_unknown_scope()
        => Assert.Contains("scope_out_of_vocabulary",
            ApiKeyValidator.Validate(Valid(scopes: new[] { "deals:read", "listings:*" }))
                .Select(v => v.Code));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Expiry_not_positive_fires_on_zero_or_negative(int days)
        => Assert.Contains("expiry_not_positive",
            ApiKeyValidator.Validate(Valid(days: days)).Select(v => v.Code));

    /// <summary>كُلُّ خَرقٍ يَحمِل رِسالَةً لِلمُراجِع — فَالرَمزُ
    /// لِلآلَة والرِسالَةُ لِلإنسان، ولا يُترَك أَحَدُهُما.</summary>
    [Fact]
    public void Every_violation_carries_an_arabic_message()
    {
        var all = ApiKeyValidator.Validate(new ApiKeyValidator.IssueRequest(
            "", Guid.Empty, "", Array.Empty<string>(), 0));

        Assert.True(all.Count >= 4, $"أَداة عَمياء: {all.Count} خَرقاً فَقَط.");
        foreach (var v in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(v.Code));
            Assert.True(v.MessageAr.Length > 10, $"«{v.Code}» بِرِسالَةٍ أَقصَرَ مِن أَن تُفيد.");
        }
    }

    // ─── شَكلُ المِفتاح المَعروض ────────────────────────────────────────

    [Fact]
    public void A_presented_key_parses_into_its_two_halves()
    {
        var keyId  = new string('a', ApiKeyFormat.KeyIdHexLength);
        var secret = new string('f', ApiKeyFormat.SecretHexLength);

        var parsed = ApiKeyValidator.ParsePresented($"wsl_{keyId}_{secret}");
        Assert.NotNull(parsed);
        Assert.Equal(keyId, parsed!.Value.KeyId);
        Assert.Equal(secret, parsed.Value.Secret);
    }

    /// <summary><b>ولِماذا ست عَشرِيّ لا <c>base64url</c></b>: الأَخيرُ
    /// يَحوي <c>_</c> — وهي الفاصِلَةُ نَفسُها. هذا الاختِبار يُثَبِّت
    /// أَنّ مِفتاحاً بِشَرطَةٍ سُفلِيَّةٍ زائِدَة لا يُقبَل، فَلا
    /// يُفَكَّك خَطَأً.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("wsl_short_secret")]
    [InlineData("bearer_aaaaaaaaaaaaaaaa_ffff")]
    [InlineData("wsl_AAAAAAAAAAAAAAAA_ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")]
    [InlineData("wsl_aaaaaaaaaaaaaaaa")]
    public void A_malformed_presented_key_parses_to_null(string? presented)
        => Assert.Null(ApiKeyValidator.ParsePresented(presented));

    [Fact]
    public void An_extra_underscore_does_not_parse()
        => Assert.Null(ApiKeyValidator.ParsePresented(
            $"wsl_{new string('a', ApiKeyFormat.KeyIdHexLength)}_{new string('f', ApiKeyFormat.SecretHexLength)}_x"));

    // ─── رَأسُ الاعتِماد ───────────────────────────────────────────────

    [Theory]
    [InlineData("Bearer wsl_x", "wsl_x")]
    [InlineData("bearer wsl_x", "wsl_x")]
    [InlineData("BEARER  wsl_x ", "wsl_x")]
    public void Bearer_is_read_case_insensitively(string header, string expected)
        => Assert.Equal(expected, ApiKeyService.BearerFrom(header));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wsl_x")]            // بِلا نَظام
    [InlineData("Basic wsl_x")]      // نِظامٌ آخَر
    [InlineData("Bearer")]
    [InlineData("Bearer   ")]
    public void Anything_that_is_not_a_bearer_header_reads_as_null(string? header)
        => Assert.Null(ApiKeyService.BearerFrom(header));

    // ─── التَجزِئَة ────────────────────────────────────────────────────

    /// <summary>‏<c>SHA-256</c> بِست عَشرِيٍّ صَغير — قيمَةٌ مَرجِعِيَّةٌ
    /// مَعروفَة، فَتَغَيُّرُ الخوارزمِيَّة يُحَمِّر بَدَلَ أَن
    /// يُبطِلَ كُلَّ مِفتاحٍ مُصدَرٍ صامِتاً.</summary>
    [Fact]
    public void The_hash_is_sha256_lowercase_hex()
    {
        Assert.Equal(
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            ApiKeyService.Sha256Hex("hello"));
        Assert.Equal(64, ApiKeyService.Sha256Hex("anything").Length);
    }

    /// <summary>والسِرُّ لا يُخَزَّن — الوَثيقَةُ لا تَحمِل حَقلاً
    /// لَه. فَحصٌ بِالانعِكاس لِأَنّ الدَعوى بِنيَوِيَّة.</summary>
    [Fact]
    public void The_document_has_no_field_that_could_hold_the_raw_secret()
    {
        var names = typeof(ApiKeyDocument).GetProperties().Select(p => p.Name).ToArray();
        Assert.Contains("SecretHash", names);
        Assert.DoesNotContain("Secret", names);
        Assert.DoesNotContain("Presented", names);
    }

    // ─── حالَةُ المِفتاح ───────────────────────────────────────────────

    [Fact]
    public void A_key_knows_the_scopes_it_carries()
    {
        var doc = new ApiKeyDocument { Scopes = { ApiScopeCatalog.DealsRead } };
        Assert.True(doc.HasScope(ApiScopeCatalog.DealsRead));
        Assert.False(doc.HasScope(ApiScopeCatalog.DealsWrite));
    }
}
