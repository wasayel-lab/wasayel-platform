using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services.Export;
using ACommerce.Templates.Customer.Marketplace.Services.Providers;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الخِزانَة — الخَتمُ والفَكُّ في الأَعمِدَةِ المَحفورَة (‏ADR-032 §٢‑هـ الخُطوَة 4) ═══
//
// **المَقيسُ قَبل (‏2026-10-12)**: أَعمِدَةُ الظَرفِ `Nonce`/`Cipher`/`Tag`/`KekVersion`/`Aad`
// بِصِفرِ كاتِب، و`StoredValue.Explicit` تَرمي لِلسِرّ — مُثَبَّتٌ في
// `TenantProviderZeroEquivalenceTests` ولا يُمَسُّ هُنا حَرفاً (القاعِدَة ٣).
//
// **وما يُقاسُ هُنا، بِأَسمائِه**: الذَهابُ والإياب؛ ومِفتاحٌ آخَرُ يَفشَل؛ وسِياقٌ آخَرُ
// يَفشَل — ولَو نُسِخَ الظَرفُ بِعَمودِه إلى مُستَأجِرٍ آخَر؛ وعَمودٌ مُعبَثٌ بِه يَفشَل؛
// والرَقمُ العابِرُ لا يَتَكَرَّر؛ و`vault_unavailable` بِموجِبٍ وسالِب (القاعِدَة ٤)؛
// والذَيل؛ وأَنّ **لا وَجهَ** — `ToString` ولا الصورَةُ ولا JSON ولا رِسالَةُ خَطَإٍ
// ولا سَطرُ التَدقيقِ ولا صَفُّ التَصدير — يَحمِلُ نافِذَةً مِن أَربَعَةِ مَحارِفَ مِن
// السِرِّ إلّا ذَيلَه المُعلَن. وكُلُّ فاحِصٍ يَطبَعُ ما عَدَّه ويَحمَرُّ عِندَ الصِفر
// (القاعِدَة ١٠).
public class ProviderVaultTests(ITestOutputHelper output)
{
    /// <summary>سِرٌّ بِشَكلِ مِفتاحِ بَوّابَة — نَفسُ سِرِّ <c>ProviderSecrecyTests</c>
    /// حَرفاً، فَالذَيلُ «58e6» واحِدٌ في المِلَفَّين.</summary>
    private const string Secret = "sk_live_9f3b7c2d41aa58e6";

    private const string Aad = "t|payments|secret_key";

    private static string NewKek() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static ProviderVault NewVault() => ProviderVault.FromKek(NewKek());

    private static StoredValue SealSecret(ProviderVault vault, string clear = Secret, string aad = Aad)
        => StoredValue.Sealed(vault, CredentialKinds.SecretKey, clear, aad);

    private static IEnumerable<string> Windows(string s, int n)
    {
        for (var i = 0; i + n <= s.Length; i++) yield return s.Substring(i, n);
    }

    /// <summary>نَوافِذُ السِرِّ الَّتي لا يَجوزُ أَن تَظهَر — كُلُّها إلّا الذَيلَ المُعلَن.</summary>
    private static string[] ForbiddenWindows(string clear) =>
        Windows(clear, 4).Where(w => w != clear[^4..]).Distinct().ToArray();

    // ═══ ١) الذَهابُ والإياب، والأَعمِدَةُ الَّتي تُملَأ ══════════════════════

    [Fact]
    public void A_sealed_secret_round_trips_through_its_own_context()
    {
        var vault = NewVault();
        var sealedValue = SealSecret(vault);

        Assert.Equal(Secret, sealedValue.Unseal(vault, Aad));
        // ومَرَّةً ثانِيَةً بِالقيمَةِ نَفسِها — الفَكُّ لا يُتلِفُ الظَرف.
        Assert.Equal(Secret, sealedValue.Unseal(vault, Aad));
    }

    [Fact]
    public void Sealing_fills_the_declared_envelope_columns_and_leaves_Plain_empty()
    {
        var v = SealSecret(NewVault());

        Assert.Equal(CredentialKinds.SecretKey, v.Kind);
        Assert.Null(v.Plain);
        Assert.True(v.IsSealed);
        Assert.Equal(ProviderVault.KekVersion, v.KekVersion);
        Assert.Equal(1, v.KekVersion);
        Assert.Equal(Aad, v.Aad);

        // ‏96 بِتّاً لِلرَقمِ العابِر، و‏128 لِلعَلامَة، والمُعَمّى بِطولِ النَصّ.
        Assert.Equal(12, Convert.FromBase64String(v.Nonce!).Length);
        Assert.Equal(16, Convert.FromBase64String(v.Tag!).Length);
        Assert.Equal(Encoding.UTF8.GetByteCount(Secret), Convert.FromBase64String(v.Cipher!).Length);
        Assert.NotEqual(Secret, v.Cipher);
    }

    // ═══ ٢) ما يَفشَلُ — ولا يُعيدُ شَيئاً ════════════════════════════════════

    [Fact]
    public void A_wrong_kek_fails_to_unseal()
    {
        var sealedValue = SealSecret(NewVault());
        var other = NewVault();

        Assert.True(other.IsAvailable);
        Assert.ThrowsAny<CryptographicException>(() => sealedValue.Unseal(other, Aad));
    }

    [Theory]
    [InlineData("other-tenant|payments|secret_key")]
    [InlineData("t|delivery|secret_key")]
    [InlineData("t|payments|other_field")]
    [InlineData("t|payments|secret_key|00000000-0000-0000-0000-000000000001")]
    public void A_different_aad_fails_to_unseal(string otherAad)
    {
        var vault = NewVault();
        var sealedValue = SealSecret(vault);

        Assert.ThrowsAny<CryptographicException>(() => sealedValue.Unseal(vault, otherAad));
    }

    /// <summary><b>النَسخُ بِالعَمود</b>: ظَرفٌ نُسِخَ مِن مُستَأجِرٍ إلى آخَرَ وأُعيدَ
    /// عَمودُ <c>Aad</c> فيه لِيُطابِقَ سِياقَ الثاني — يَعبُرُ فَحصَ المُطابَقَة،
    /// ويَسقُطُ عِندَ العَلامَة. أَي أَنّ الرِباطَ تَشفيرِيٌّ لا مُقارَنَةُ نَصّ.</summary>
    [Fact]
    public void A_sealed_value_moved_to_another_tenant_with_its_aad_column_rewritten_fails_to_unseal()
    {
        var vault = NewVault();
        var original = SealSecret(vault, aad: "tenant-a|payments|secret_key");

        var moved = new StoredValue
        {
            Kind = original.Kind, Nonce = original.Nonce, Cipher = original.Cipher, Tag = original.Tag,
            KekVersion = original.KekVersion, Tail4 = original.Tail4,
            Aad = "tenant-b|payments|secret_key",
        };

        Assert.ThrowsAny<CryptographicException>(() => moved.Unseal(vault, "tenant-b|payments|secret_key"));
    }

    [Theory]
    [InlineData("Tag")]
    [InlineData("Cipher")]
    [InlineData("Nonce")]
    public void A_tampered_envelope_column_fails_to_unseal(string column)
    {
        var vault = NewVault();
        var v = SealSecret(vault);

        static string Flip(string b64)
        {
            var bytes = Convert.FromBase64String(b64);
            bytes[0] ^= 0x01;
            return Convert.ToBase64String(bytes);
        }

        switch (column)
        {
            case "Tag":    v.Tag = Flip(v.Tag!); break;
            case "Cipher": v.Cipher = Flip(v.Cipher!); break;
            case "Nonce":  v.Nonce = Flip(v.Nonce!); break;
        }

        Assert.ThrowsAny<CryptographicException>(() => v.Unseal(vault, Aad));
    }

    [Fact]
    public void A_column_that_is_not_base64_or_of_the_wrong_length_fails_as_a_broken_envelope()
    {
        var vault = NewVault();

        var notBase64 = SealSecret(vault);
        notBase64.Tag = "not base64 !!";
        Assert.ThrowsAny<CryptographicException>(() => notBase64.Unseal(vault, Aad));

        var shortNonce = SealSecret(vault);
        shortNonce.Nonce = Convert.ToBase64String(new byte[8]);
        Assert.ThrowsAny<CryptographicException>(() => shortNonce.Unseal(vault, Aad));
    }

    [Fact]
    public void Two_seals_of_the_same_value_never_share_a_nonce()
    {
        var vault = NewVault();
        var seals = Enumerable.Range(0, 64).Select(_ => SealSecret(vault)).ToArray();

        var nonces = seals.Select(s => s.Nonce).Distinct(StringComparer.Ordinal).Count();
        var ciphers = seals.Select(s => s.Cipher).Distinct(StringComparer.Ordinal).Count();
        output.WriteLine($"خُتِمَت القيمَةُ نَفسُها {seals.Length} مَرَّة: {nonces} رَقماً عابِراً، {ciphers} مُعَمّىً.");

        Assert.True(seals.Length == 64, "أَداةٌ عَمياء: صِفرُ خَتمٍ مَقيس.");
        Assert.Equal(seals.Length, nonces);
        Assert.Equal(seals.Length, ciphers);
        Assert.All(seals, s => Assert.Equal(Secret, s.Unseal(vault, Aad)));
    }

    [Fact]
    public void Unseal_refuses_a_value_that_is_not_sealed_and_a_kek_version_it_does_not_hold()
    {
        var vault = NewVault();

        var plain = StoredValue.Explicit(CredentialKinds.HostedLink, "https://moyasar.com/i/1");
        Assert.False(plain.IsSealed);
        Assert.Throws<InvalidOperationException>(() => plain.Unseal(vault, Aad));

        // ‏`KekVersion` يَحمِلُ الدَوَرانَ مِن اليَوم (‏ADR-012 §هـ)، والخِزانَةُ تَفُكُّ
        // نُسخَتَها وَحدَها — وغَيرُها يُرَدُّ بِاسمِه لا بِخَطَإِ تَشفيرٍ مُبهَم.
        var rotated = SealSecret(vault);
        rotated.KekVersion = 2;
        var ex = Assert.Throws<InvalidOperationException>(() => rotated.Unseal(vault, Aad));
        Assert.Contains("2", ex.Message, StringComparison.Ordinal);
    }

    // ═══ ٣) `vault_unavailable` — موجِبٌ وسالِب (القاعِدَة ٤) ══════════════════

    public static IEnumerable<object?[]> AbsentOrMalformedKeks()
    {
        yield return new object?[] { null };
        yield return new object?[] { "" };
        yield return new object?[] { "   " };
        yield return new object?[] { "\n" };
        yield return new object?[] { "not base64 at all !!" };
        yield return new object?[] { Convert.ToBase64String(new byte[16]) };   // ‏AES-128 — لا يُقبَل
        yield return new object?[] { Convert.ToBase64String(new byte[31]) };
        yield return new object?[] { Convert.ToBase64String(new byte[33]) };
    }

    [Theory]
    [MemberData(nameof(AbsentOrMalformedKeks))]
    public void vault_unavailable_fires_for_an_absent_or_malformed_kek(string? kek)
    {
        var v = Assert.Single(ProviderVault.Validate(kek));
        Assert.Equal(ProviderVault.Unavailable, v.Code);
        Assert.Equal("vault_unavailable", v.Code);
        Assert.Contains(ProviderVault.KekEnvironmentVariable, v.MessageAr, StringComparison.Ordinal);
        Assert.False(ProviderVault.IsValid(kek));

        var vault = ProviderVault.FromKek(kek);
        Assert.False(vault.IsAvailable);
        Assert.Equal(ProviderVault.Unavailable, Assert.Single(vault.Violations).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]        // مُخرَجُ `openssl rand -base64 32` يَنتَهي بِسَطرٍ جَديد
    [InlineData("  \r\n")]
    public void vault_unavailable_does_not_fire_for_a_32_byte_base64_kek(string trailing)
    {
        var kek = NewKek() + trailing;

        Assert.Empty(ProviderVault.Validate(kek));
        Assert.True(ProviderVault.IsValid(kek));

        var vault = ProviderVault.FromKek(kek);
        Assert.True(vault.IsAvailable);
        Assert.Empty(vault.Violations);
        Assert.Equal(Secret, SealSecret(vault).Unseal(vault, Aad));
    }

    [Fact]
    public void A_wrong_length_kek_is_reported_by_its_measured_length_not_its_value()
    {
        var kek = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var v = Assert.Single(ProviderVault.Validate(kek));

        Assert.Contains("16", v.MessageAr, StringComparison.Ordinal);
        Assert.Contains("32", v.MessageAr, StringComparison.Ordinal);
        Assert.DoesNotContain(kek, v.MessageAr, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unavailable_vault_seals_nothing_and_opens_nothing()
    {
        var none = ProviderVault.FromKek(null);
        Assert.False(none.IsAvailable);

        var seal = Assert.Throws<InvalidOperationException>(() => SealSecret(none));
        Assert.Contains(ProviderVault.Unavailable, seal.Message, StringComparison.Ordinal);

        var sealedElsewhere = SealSecret(NewVault());
        var open = Assert.Throws<InvalidOperationException>(() => sealedElsewhere.Unseal(none, Aad));
        Assert.Contains(ProviderVault.Unavailable, open.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_violation_vocabulary_is_one_code()
    {
        Assert.Equal(new[] { "vault_unavailable" }, ProviderVault.Codes.ToArray());
        Assert.Equal("Providers__Kek__v1", ProviderVault.KekEnvironmentVariable);
        Assert.Equal("Providers:Kek:v1", ProviderVault.KekConfigKey);
    }

    // ═══ ٤) الذَيل — لِلعَرضِ وَحدَه، بِقاعِدَةِ القِناعِ نَفسِها ══════════════════

    [Fact]
    public void Tail4_keeps_the_last_four_characters_for_display_only()
    {
        var v = SealSecret(NewVault());

        Assert.Equal("58e6", v.Tail4);
        Assert.Equal(ProviderSecrecy.Mask + "58e6", v.Censored);

        // الصورَةُ نَفسُها الَّتي كانَت سَتُعرَضُ لَو خُزِّنَ صَريحاً — بِلا فَكّ.
        Assert.Equal(ProviderSecrecy.Censor(CredentialKinds.SecretKey, Secret), v.Censored);
    }

    [Fact]
    public void A_secret_no_longer_than_its_tail_keeps_no_tail_and_shows_the_mask_alone()
    {
        var vault = NewVault();

        var four = SealSecret(vault, clear: "abcd");
        Assert.Null(four.Tail4);
        Assert.Equal(ProviderSecrecy.Mask, four.Censored);
        Assert.Equal("abcd", four.Unseal(vault, Aad));

        Assert.Equal("bcde", SealSecret(vault, clear: "abcde").Tail4);
    }

    // ═══ ٥) ما تَختِمُه الخِزانَة، وما يَبقى صَريحاً أَو مَرفوضاً ════════════════

    [Fact]
    public void The_vault_seals_secret_key_and_nothing_else_today()
    {
        var vault = NewVault();
        var kinds = CredentialKinds.All.Concat(CredentialKinds.NotYetInVocabulary)
            .Distinct(StringComparer.Ordinal).ToArray();
        Assert.Equal(9, kinds.Length);

        var sealable = 0;
        foreach (var k in kinds)
        {
            Assert.Equal(k == CredentialKinds.SecretKey, ProviderVault.Seals(k));
            if (ProviderVault.Seals(k)) { sealable++; continue; }

            Assert.Throws<InvalidOperationException>(
                () => StoredValue.Sealed(vault, k, "value", Aad));
        }

        output.WriteLine($"فُحِصَ {kinds.Length} نَوعاً؛ تَختِمُ الخِزانَةُ {sealable}.");
        Assert.Equal(1, sealable);
    }

    /// <summary><b>الفَشَلُ المُغلَقُ القائِمُ يَبقى — قَبلَ دُخولِ <c>secret_key</c>
    /// المَعجَمَ وبَعدَه</b>: كُلُّ نَوعٍ يُشبِهُ السِرَّ تَرُدُّه <c>Explicit</c>، أَيّاً
    /// كانَت قائِمَتُه. الصَفُّ القائِمُ في <c>TenantProviderZeroEquivalenceTests</c>
    /// يَدورُ عَلى <c>NotYetInVocabulary</c>، فَيَخرُجُ مِنه <c>secret_key</c> يَومَ يَدخُلُ
    /// المَعجَم (‏ADR-032 الخُطوَة 5) — وهذا الصَفُّ يُثَبِّتُه بِاسمِه قَبلَ ذلكَ اليَوم.</summary>
    [Fact]
    public void Explicit_still_refuses_every_secret_like_kind_in_or_out_of_the_vocabulary()
    {
        var kinds = CredentialKinds.All.Concat(CredentialKinds.NotYetInVocabulary)
            .Distinct(StringComparer.Ordinal).Where(CredentialKinds.IsSecretLike).ToArray();

        output.WriteLine($"أَنواعٌ تُشبِهُ السِرَّ: {kinds.Length} — {string.Join("، ", kinds)}");
        Assert.True(kinds.Length == 5, $"أَداةٌ عَمياء: {kinds.Length} نَوعاً سِرِّيّاً — والمَقيس ٥.");

        foreach (var k in kinds)
            Assert.ThrowsAny<Exception>(() => StoredValue.Explicit(k, Secret));

        Assert.ThrowsAny<Exception>(() => StoredValue.Explicit(CredentialKinds.SecretKey, Secret));
    }

    // ═══ ٦) السِياق — شَكلانِ مِن دالَّةٍ واحِدَةٍ لا تَعرِفُ أَيَّهُما ══════════

    [Fact]
    public void AadOf_builds_both_known_shapes_without_knowing_either()
    {
        // رَبطُ المَتجَر (‏ADR-032 الخُطوَة 4).
        Assert.Equal("t|payments|secret_key",
            ProviderVault.AadOf("t", ProviderCapabilities.Payments, "secret_key"));

        // مَدخَلُ البائِع (‏ADR-033 §٢‑ج‑٢ — القاعِدَة 8 هُناك).
        const string owner = "3f2b8c1e-0000-4000-8000-000000000042";
        var vendor = ProviderVault.AadOf("t", ProviderCapabilities.Delivery, "refresh_token", owner);
        Assert.Equal($"t|delivery|refresh_token|{owner}", vendor);

        // والسِياقانِ لا يَتَبادَلان: المَختومُ بِأَحَدِهِما لا يُفَكُّ بِالآخَر.
        var vault = NewVault();
        var forVendor = SealSecret(vault, aad: vendor);
        Assert.Equal(Secret, forVendor.Unseal(vault, vendor));
        Assert.ThrowsAny<CryptographicException>(
            () => forVendor.Unseal(vault, ProviderVault.AadOf("t", ProviderCapabilities.Delivery, "refresh_token")));
    }

    [Theory]
    [InlineData("a|b", "c")]
    [InlineData("a", "")]
    [InlineData("", "b")]
    public void AadOf_refuses_a_part_that_would_make_the_context_ambiguous(string first, string second)
        => Assert.Throws<ArgumentException>(() => ProviderVault.AadOf(first, second));

    // ═══ ٧) لا وَجهَ يَحمِلُ النَصَّ الصَريح ══════════════════════════════════

    [Fact]
    public void No_face_of_a_sealed_secret_carries_its_clear_text()
    {
        var vault = NewVault();
        var sealedValue = SealSecret(vault);
        var forbidden = ForbiddenWindows(Secret);
        Assert.True(forbidden.Length >= 15, $"أَداةٌ عَمياء: {forbidden.Length} نافِذَةً مَمنوعَة.");

        var binding = new TenantProviderBinding
        {
            Id = ProviderCapabilities.Payments,
            Slug = ProviderCapabilities.Payments,
            TenantSlug = "t",
            ProviderSlug = "gateway_with_a_secret_key",
            Status = TenantProviderBinding.StatusActive,
            Values = { ["secret_key"] = sealedValue },
        };

        // الوُجوهُ — كُلُّ نَصٍّ قَد يَبلُغُ شاشَةً أَو لوغاً أَو مِلَفّاً.
        var faces = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["StoredValue.ToString"] = sealedValue.ToString(),
            ["StoredValue.Censored"] = sealedValue.Censored,
            ["ProviderVault.ToString"] = vault.ToString(),
            ["ProviderVault.json"] = JsonSerializer.Serialize(vault),
            ["StoredValue.json — بِلا الأَعمِدَةِ العَشوائِيَّة"] = WithoutRandomColumns(JsonSerializer.Serialize(sealedValue)),
            ["ProviderBindSurface.AuditLine"] = ProviderBindSurface.AuditLine(binding),
            ["TenantExportRedaction — صَفُّ التَصدير"] = TenantExportRedaction
                .Apply("TenantProviderBinding", TenantExportRedaction.ToJson(binding))!
                .ToJsonString(TenantExportRedaction.Json),
            ["خَطَأُ سِياقٍ آخَر"] = Record.Exception(() => sealedValue.Unseal(vault, "x|y|z"))!.Message,
            ["خَطَأُ مِفتاحٍ آخَر"] = Record.Exception(() => sealedValue.Unseal(NewVault(), Aad))!.Message,
            ["خَطَأُ خِزانَةٍ غائِبَة"] = Record.Exception(() => sealedValue.Unseal(ProviderVault.FromKek(null), Aad))!.Message,
            ["رَفضُ الخَتمِ بِلا خِزانَة"] = Record.Exception(() => SealSecret(ProviderVault.FromKek(null)))!.Message,
            ["رَفضُ Explicit"] = Record.Exception(() => StoredValue.Explicit(CredentialKinds.SecretKey, Secret))!.Message,
        };

        var leaks = new List<string>();
        foreach (var (face, text) in faces)
            foreach (var w in forbidden)
                if (text.Contains(w, StringComparison.Ordinal))
                    leaks.Add($"{face}: «{w}» في «{text}»");

        // والأَعمِدَةُ العَشوائِيَّةُ تُقاسُ بِالبايت لا بِالنافِذَة: النَصُّ الصَريحُ لا يَقَعُ
        // في المُعَمّى مُتَّصِلاً — وإلّا فَلا تَشفير.
        var plain = Encoding.UTF8.GetBytes(Secret);
        foreach (var column in new[] { sealedValue.Cipher!, sealedValue.Nonce!, sealedValue.Tag! })
            if (Convert.FromBase64String(column).AsSpan().IndexOf(plain) >= 0 ||
                column.Contains(Secret, StringComparison.Ordinal))
                leaks.Add($"عَمودُ ظَرفٍ يَحمِلُ النَصَّ الصَريح: «{column}»");

        output.WriteLine($"فُحِصَ {faces.Count} وَجهاً و3 أَعمِدَةٍ بِـ{forbidden.Length} نافِذَة.");
        Assert.True(faces.Count >= 12, $"أَداةٌ عَمياء: {faces.Count} وَجهاً مَفحوصاً.");
        Assert.True(leaks.Count == 0, "سِرٌّ يَظهَرُ في وَجهٍ مُعَتَّم:\n  " + string.Join("\n  ", leaks));

        // والوَجهُ المَعروضُ هُوَ الذَيلُ المُعلَنُ بَعدَ القِناع — لا أَكثَر.
        Assert.Equal($"{CredentialKinds.SecretKey}:{ProviderSecrecy.Mask}58e6", sealedValue.ToString());
    }

    [Fact]
    public void No_face_of_the_vault_carries_its_master_key()
    {
        var kek = NewKek();
        var vault = ProviderVault.FromKek(kek);
        var forbidden = Windows(kek, 6).Distinct().ToArray();

        var faces = new[]
        {
            vault.ToString(),
            JsonSerializer.Serialize(vault),
            string.Join(" ", vault.Violations.Select(v => v.MessageAr)),
            ProviderVault.FromKek(kek[..^4]).ToString(),
            string.Join(" ", ProviderVault.Validate(kek[..^4]).Select(v => v.MessageAr)),
        };

        var leaks = faces.SelectMany(f => forbidden.Where(w => f.Contains(w, StringComparison.Ordinal))
            .Select(w => $"«{w}» في «{f}»")).ToArray();

        output.WriteLine($"فُحِصَ {faces.Length} وُجوهٍ بِـ{forbidden.Length} نافِذَة مِن المِفتاحِ الرَئيس.");
        Assert.True(forbidden.Length > 30, "أَداةٌ عَمياء: نَوافِذُ المِفتاحِ لَم تُبنَ.");
        Assert.True(leaks.Length == 0, "المِفتاحُ الرَئيسُ يَظهَرُ في وَجه:\n  " + string.Join("\n  ", leaks));
        Assert.Equal("ProviderVault(kek=v1)", vault.ToString());
        Assert.Equal("ProviderVault(vault_unavailable)", ProviderVault.FromKek(null).ToString());
    }

    /// <summary>JSON القيمَةِ المُخَزَّنَةِ بِلا أَعمِدَتِها العَشوائِيَّة (‏base64 لِبايتاتٍ
    /// عَشوائِيَّة قَد تَحمِلُ أَيَّ نافِذَةٍ مُصادَفَةً) — وتِلكَ تُقاسُ بِالبايت.</summary>
    private static string WithoutRandomColumns(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        Assert.True(node.ContainsKey("Cipher") && node.ContainsKey("Nonce") && node.ContainsKey("Tag"),
            "أَداةٌ عَمياء: أَعمِدَةُ الظَرفِ غائِبَةٌ عَن JSON — اسمُها تَغَيَّر.");
        node.Remove("Cipher");
        node.Remove("Nonce");
        node.Remove("Tag");
        return node.ToJsonString();
    }
}
