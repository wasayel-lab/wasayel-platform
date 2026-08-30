using ACommerce.Platform.Providers;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ اختِبارُ العَتامَة — «السِرُّ لا يُعرَض بَعدَ حِفظِه أَبَداً» ═══
//
// **ولا يُقاس بِالعَينِ ولا بِـ`Assert.NotEqual`**: القِناعُ الَّذي
// يَحذِف مِحرَفاً واحِداً يَمُرّ عَلى «غَير مُتَساوِيَين» ويَبقى
// تَسريباً. فَالمِعيارُ هُنا **كُلُّ نافِذَةٍ مِن أَربَعَةِ مَحارِف**
// مِن السِرِّ الخام: أَيُّ واحِدَةٍ تَظهَر في المُخرَجِ = فَشَل —
// إلّا ذَيلَ `secret_key` وَحدَه، وهُوَ مُعلَنٌ لا مُتَسامَحٌ عَنه.
public class ProviderSecrecyTests
{
    private const string Raw = "sk_live_9f3b7c2d41aa58e6";

    private static IEnumerable<string> Windows(string s, int n)
    {
        for (var i = 0; i + n <= s.Length; i++) yield return s.Substring(i, n);
    }

    // ─── ١. المَسحُ الشامِل — كُلُّ نَوعٍ في التَصنيفِ التِسعيّ ──────

    [Fact]
    public void No_non_displayable_kind_ever_leaks_a_four_character_window()
    {
        var allKinds = CredentialKinds.All
            .Concat(CredentialKinds.NotYetInVocabulary)
            .Distinct(StringComparer.Ordinal).ToArray();

        Assert.Equal(9, allKinds.Length);

        var checkedKinds = 0;
        var leaks = new List<string>();

        foreach (var kind in allKinds.Where(k => !ProviderSecrecy.IsDisplayable(k)))
        {
            checkedKinds++;
            var shown = ProviderSecrecy.Censor(kind, Raw);

            // الذَيلُ المُعلَنُ وَحدَه مَسموح.
            var allowed = ProviderSecrecy.ShowsTail(kind)
                ? Raw[^ProviderSecrecy.TailLength..]
                : null;

            foreach (var w in Windows(Raw, 4))
            {
                if (allowed is not null && w == allowed) continue;
                if (shown.Contains(w, StringComparison.Ordinal))
                    leaks.Add($"{kind}: «{w}» ظَهَرَ في «{shown}»");
            }
        }

        // ما لا يُعرَض سِتَّةٌ: none (لا شَيءَ لَه)، والخَمسَةُ
        // الَّتي تَحمِلُ سِرّاً.
        Assert.True(checkedKinds == 6,
            $"أَداة عَمياء: فُحِصَ {checkedKinds} نَوعاً لا يُعرَض — والمَقيس ٦.");
        Assert.True(leaks.Count == 0,
            "سِرٌّ يَظهَر بَعدَ حِفظِه:\n  " + string.Join("\n  ", leaks));
    }

    [Fact]
    public void The_four_displayable_kinds_are_shown_whole_and_that_is_the_contract()
    {
        var displayable = CredentialKinds.All
            .Concat(CredentialKinds.NotYetInVocabulary)
            .Where(ProviderSecrecy.IsDisplayable).ToArray();

        Assert.Equal(
            new[] { CredentialKinds.HostedLink, CredentialKinds.PublishedKey,
                    CredentialKinds.DelegatedGrant },
            displayable.OrderBy(k => CredentialKinds.Rank(k)).ToArray());

        foreach (var k in displayable)
            Assert.Equal(Raw, ProviderSecrecy.Censor(k, Raw));

        // و`none` لا شَيءَ لَه أَصلاً.
        Assert.Equal("", ProviderSecrecy.Censor(CredentialKinds.None, Raw));
    }

    [Fact]
    public void secret_key_shows_exactly_its_last_four_and_nothing_more()
    {
        var shown = ProviderSecrecy.Censor(CredentialKinds.SecretKey, Raw);
        Assert.EndsWith("58e6", shown, StringComparison.Ordinal);
        Assert.StartsWith(ProviderSecrecy.Mask, shown, StringComparison.Ordinal);
        Assert.Equal(ProviderSecrecy.Mask.Length + 4, shown.Length);

        // وسِرٌّ قَصيرٌ لا يُعرَض مِنه شَيء — ذَيلُ أَربَعَةٍ مِن
        // خَمسَةٍ هُوَ السِرُّ نَفسُه تَقريباً.
        Assert.Equal(ProviderSecrecy.Mask,
            ProviderSecrecy.Censor(CredentialKinds.SecretKey, "abcd"));
    }

    [Fact]
    public void The_mask_length_never_tells_the_secret_length()
    {
        var shortSecret = ProviderSecrecy.Censor(CredentialKinds.SharedSecret, "ab");
        var longSecret = ProviderSecrecy.Censor(CredentialKinds.SharedSecret, new string('x', 512));
        Assert.Equal(shortSecret, longSecret);
    }

    [Fact]
    public void An_empty_or_missing_value_censors_to_empty_not_to_a_mask()
    {
        foreach (var k in CredentialKinds.All)
        {
            Assert.Equal("", ProviderSecrecy.Censor(k, null));
            Assert.Equal("", ProviderSecrecy.Censor(k, ""));
        }
    }

    // ─── ٢. حَقلا التَدقيق — مُعَتَّمانِ بِالبِناء ───────────────────

    [Fact]
    public void The_audit_snapshot_carries_the_censored_shape_only()
    {
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["invoice_url"] = CredentialKinds.HostedLink,
            ["webhook_secret"] = CredentialKinds.SharedSecret,
        };

        var line = ProviderSecrecy.ForAudit(
            "moyasar_hosted", TenantProviderBinding.StatusActive, kinds,
            code => code == "invoice_url" ? "https://moyasar.com/i/inv_1" : Raw);

        Assert.Contains("provider=moyasar_hosted", line, StringComparison.Ordinal);
        Assert.Contains("status=active", line, StringComparison.Ordinal);
        Assert.Contains("invoice_url=https://moyasar.com/i/inv_1", line, StringComparison.Ordinal);

        foreach (var w in Windows(Raw, 4))
            Assert.DoesNotContain(w, line, StringComparison.Ordinal);

        Assert.Contains($"webhook_secret={ProviderSecrecy.Mask}", line, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_binding_still_produces_a_readable_audit_line()
    {
        var line = ProviderSecrecy.ForAudit(
            null, TenantProviderBinding.StatusRevoked,
            Array.Empty<KeyValuePair<string, string>>(), _ => null);

        Assert.Contains("provider=-", line, StringComparison.Ordinal);
        Assert.Contains("status=revoked", line, StringComparison.Ordinal);
    }

    // ─── ٣. الصورَةُ الَّتي تَراها الشاشَة ───────────────────────────

    [Fact]
    public void The_stored_value_exposes_only_its_censored_face()
    {
        var v = StoredValue.Explicit(CredentialKinds.HostedLink, "https://moyasar.com/i/1");
        Assert.Equal("https://moyasar.com/i/1", v.Censored);

        // ونَوعٌ لا يُعرَض — ولَو كُتِبَ صَريحاً بِالالتِفافِ عَلى
        // المَصنَع، الصورَةُ المَعروضَةُ تَبقى قِناعاً.
        var smuggled = new StoredValue { Kind = CredentialKinds.SecretKey, Plain = Raw };
        Assert.Equal(ProviderSecrecy.Mask + "58e6", smuggled.Censored);
        foreach (var w in Windows(Raw[..^4], 4))
            Assert.DoesNotContain(w, smuggled.Censored, StringComparison.Ordinal);
    }
}
