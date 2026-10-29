using System.Text.Json;
using System.Text.RegularExpressions;
using ACommerce.Kit.Files;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ سِياسَةُ الخُصوصِيَّةِ تُسَمّي كُلَّ جِهَةٍ بِاسمِها وفِئَتِها — والأَسماءُ مِن المَوضِعِ الَّذي يَختارُها ═══
//
// قَرارُ المالِك د20 «أ» (‏2026-10-29): «تُسمّى كلّ الجهات بأسمائها وفئاتها … واسمُ مُزَوِّدِ النَموذَجِ ومُزَوِّدِ الرَسائِلِ يُقرَآنِ مِن الإعدادِ
// نَفسِه الَّذي يَختارُهُما فَلا تَكذِبُ السِياسَةُ إن بُدِّلا … وسَطرٌ لِلاستِضافَةِ وقاعِدَةِ البَيانات (لا يَراهُما اختِبارُ الجَردِ لِأَنَّه يَعُدُّ
// عُمَلاءَ الشَبَكَةِ الصادِرينَ وَحدَهُم، فَيُضافُ لَه صَفٌّ يَحرُسُه)». **المَقيسُ قَبلَه**: سَبعَةُ سُطور، و`share_otp` فِئَةٌ بِلا اسم، و`share_payment`
// يُسَمّي الاشتِراكَ وَحدَه، وخَمسَةُ عُمَلاءَ صادِرينَ مُثَبَّتونَ غَيرَ مُسَمَّين (`PrivacyShareInventoryTests`)، ولا نَصَّ عَن الاستِضافَةِ ولا عَن
// المُعالَجَةِ خارِجَ المَملَكَة. وكُلُّ صَفٍّ هُنا يَربِطُ ما يَقولُه النَصُّ بِطَرَفٍ مَقيسٍ في الكود أَو النَشر — فَمَن بَدَّلَ الطَرَفَ ولَم يُبَدِّل
// النَصَّ يَحمَرّ.
public class PrivacyRecipientsTests(ITestOutputHelper output)
{
    private static string Ar(string key) => LocaleCatalog.Find(LocaleCatalog.Arabic, key)
        ?? throw new Xunit.Sdk.XunitException($"«{key}» بِلا قيمَةٍ عَرَبِيَّة.");
    private static string En(string key) => LocaleCatalog.Find("en", key)
        ?? throw new Xunit.Sdk.XunitException($"«{key}» بِلا قيمَةٍ إنجليزِيَّة.");

    // ─── مَن يَستَلِمُ رَمزَ الدُخول — مِن القَنَواتِ المُسَجَّلَة ───────────────────────────────────

    [Fact]
    public void The_sign_in_senders_are_the_registered_real_channels_by_name()
        => Assert.Equal(new[] { "Twilio", "Brevo" },
            PrivacyRecipients.SignInSenders([("Twilio", false), ("Brevo", false)], smtpHost: null));

    [Fact]
    public void A_development_stub_is_not_named_because_it_sends_to_no_one()
        => Assert.Empty(PrivacyRecipients.SignInSenders([("MockSms", true), ("MockEmail", true)], smtpHost: "smtp.example.com"));

    [Theory]
    [InlineData("smtp.example.com", "smtp.example.com")]
    [InlineData("  mail.example.org ", "mail.example.org")]
    [InlineData(null, "SMTP")]
    public void Smtp_is_named_by_the_configured_host_not_by_the_protocol(string? host, string expected)
        => Assert.Equal(new[] { expected }, PrivacyRecipients.SignInSenders([("Smtp", false)], host));

    [Fact]
    public void An_unknown_channel_is_named_verbatim_and_a_repeat_is_named_once()
        => Assert.Equal(new[] { "Unifonic" },
            PrivacyRecipients.SignInSenders([("Unifonic", false), ("unifonic", false), ("", false)], smtpHost: null));

    /// <summary>وما يَقرَؤُه الجَدوَلُ هُوَ ما تُعلِنُه القَنَواتُ وما يَربِطُه <c>Program.cs</c> — اسمُ SMTP ومِفتاحُ مُضيفِه.</summary>
    [Fact]
    public void The_smtp_name_and_host_key_are_the_ones_the_channel_and_the_host_declare()
    {
        var smtp = UpgradePromptPriceTests.Code("libs", "kits", "Auth", "ACommerce.Kit.Auth.Providers.Smtp", "SmtpEmailChannel.cs");
        var program = UpgradePromptPriceTests.Code("apps", "V1.App", "Program.cs");
        Assert.Contains($"public string ChannelName => \"{PrivacyRecipients.SmtpChannelName}\";", smtp, StringComparison.Ordinal);
        Assert.Contains("public string Host { get; set; }", smtp, StringComparison.Ordinal);
        Assert.Equal("Auth:Email:Host", PrivacyRecipients.SmtpHostKey);
        var smtpCase = program.IndexOf("case AuthChannelProvider.Smtp:", StringComparison.Ordinal);
        Assert.True(smtpCase > 0);
        Assert.Contains("GetSection(\"Auth:Email\").Bind(opts)", program[smtpCase..(smtpCase + 300)], StringComparison.Ordinal);
    }

    // ─── مُزَوِّدُ نَموذَجِ اللُغَة — مِن الخَلفِيّاتِ نَفسِها ──────────────────────────────────────────

    [Fact]
    public void Two_agents_on_one_provider_name_it_once_and_two_providers_name_both()
    {
        Assert.Equal(new[] { "Anthropic" }, PrivacyRecipients.LanguageModels(["anthropic", "anthropic"]));
        Assert.Equal(new[] { "Anthropic", "Groq" }, PrivacyRecipients.LanguageModels(["anthropic", "groq"]));
        Assert.Equal(new[] { "mistral-house" }, PrivacyRecipients.LanguageModels(["mistral-house", " "]));
    }

    /// <summary>كُلُّ <c>ProviderName</c> تُعلِنُه خَلفِيَّةٌ مَبنِيَّةٌ لَه اسمٌ في المَعجَم — مَقيساً بِبِناءِ الخَلفِيّاتِ لا بِقِراءَةِ الجَدوَل.</summary>
    [Theory]
    [InlineData("anthropic", null, "Anthropic")]
    [InlineData("gemini", null, "Google Gemini")]
    [InlineData("openai", null, "OpenAI")]
    [InlineData("openai", "https://api.groq.com/openai/v1", "Groq")]
    [InlineData("openai", "https://api.cerebras.ai/v1", "Cerebras")]
    [InlineData("openai", "https://openrouter.ai/api/v1", "OpenRouter")]
    [InlineData("openai", "http://localhost:11434/v1", "Ollama")]
    public void Every_backend_provider_name_has_a_display_name(string provider, string? baseUrl, string expected)
    {
        var backend = AgentBackendFactory.Create(new AgentProfile("Analysis", provider, baseUrl, "k", null, null));
        Assert.Equal(new[] { expected }, PrivacyRecipients.LanguageModels([backend.ProviderName]));
    }

    // ─── مَخزَنُ الصُوَر — حينَ يَختارُ القَرارُ S3 ───────────────────────────────────────────────

    [Theory]
    [InlineData("https://abc123.r2.cloudflarestorage.com", "Cloudflare R2")]
    [InlineData("https://s3.eu-central-1.amazonaws.com", "Amazon S3")]
    [InlineData("https://minio.example.net:9000", "minio.example.net")]
    public void A_complete_s3_configuration_names_the_store_by_its_endpoint(string endpoint, string expected)
        => Assert.Equal(expected, PrivacyRecipients.FileStore(new S3StorageSettings(endpoint, "b", "id", "secret", "https://cdn.example")));

    [Fact]
    public void Local_or_incomplete_storage_names_no_store()
    {
        Assert.Null(PrivacyRecipients.FileStore(S3StorageSettings.None));
        Assert.Null(PrivacyRecipients.FileStore(new S3StorageSettings("https://abc.r2.cloudflarestorage.com", "b", "id", null, "https://cdn")));
    }

    // ─── الجَمع ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Names_are_joined_with_the_dictionary_connectors_of_the_document_language()
    {
        Assert.Equal("", PrivacyRecipients.Join([], Ar("platform.privacy.names_pair"), Ar("platform.privacy.names_comma")));
        Assert.Equal("Twilio", PrivacyRecipients.Join(["Twilio"], Ar("platform.privacy.names_pair"), Ar("platform.privacy.names_comma")));
        Assert.Equal("Twilio وBrevo", PrivacyRecipients.Join(["Twilio", "Brevo"], Ar("platform.privacy.names_pair"), Ar("platform.privacy.names_comma")));
        Assert.Equal("A، B وC", PrivacyRecipients.Join(["A", "B", "C"], Ar("platform.privacy.names_pair"), Ar("platform.privacy.names_comma")));
        Assert.Equal("A, B and C", PrivacyRecipients.Join(["A", "B", "C"], En("platform.privacy.names_pair"), En("platform.privacy.names_comma")));
    }

    /// <summary>الوَسيطُ يُرَمَّزُ فَلا يَكتُبُ وَسماً، والحُروفُ تَبقى حُروفاً (‏<see cref="PlatformDocLanguage.Markup(string, object?[])"/>).</summary>
    [Fact]
    public void A_configured_name_cannot_write_markup_and_arabic_stays_readable()
    {
        var doc = PlatformDocLanguage.FromRoute(null);
        var hostile = doc.Markup("platform.privacy.share_files", "<script>x</script>").Value;
        Assert.DoesNotContain("<script>", hostile, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", hostile, StringComparison.Ordinal);
        var arabic = doc.Markup("platform.privacy.share_otp", "Twilio وBrevo").Value;
        Assert.Contains("(Twilio وBrevo)", arabic, StringComparison.Ordinal);
    }

    // ─── الصَفحَةُ تَقرَأُ المَواضِعَ نَفسَها ───────────────────────────────────────────────────────

    [Fact]
    public void The_privacy_page_reads_the_registered_channels_the_agent_backends_and_the_storage_decision()
    {
        var page = PrivacyShareInventoryTests.PrivacyPage();
        foreach (var said in new[]
        {
            "typeof(ACommerce.Kit.Auth.IOtpChannel)", "typeof(ACommerce.Kit.Auth.IEmailOtpChannel)", "ACommerce.Kit.Auth.IDevelopmentStubChannel",
            "PrivacyRecipients.SignInSenders(registered, Cfg[PrivacyRecipients.SmtpHostKey])",
            "Agents.For(AgentNames.Analysis).ProviderName", "Agents.For(AgentNames.Studio).ProviderName",
            "PrivacyRecipients.FileStore(", "@if (fileStore is not null)",
            "@doc.Markup(\"platform.privacy.share_otp\", Names(signInSenders))", "@doc.Markup(\"platform.privacy.share_llm\", Names(languageModels))",
            "@doc.Markup(\"platform.privacy.share_files\", fileStore)", "platform.privacy.none_configured",
        })
            Assert.True(page.Contains(said, StringComparison.Ordinal), $"الصَفحَةُ لا تَقرَأُ «{said}».");
        foreach (var key in FileStorageSelection.ConfigKeys)
        {
            var constant = key switch
            {
                FileStorageSelection.EndpointKey        => "EndpointKey",
                FileStorageSelection.BucketKey          => "BucketKey",
                FileStorageSelection.AccessKeyIdKey     => "AccessKeyIdKey",
                FileStorageSelection.SecretAccessKeyKey => "SecretAccessKeyKey",
                _                                       => "PublicBaseUrlKey",
            };
            Assert.Contains($"FileStorageSelection.{constant}]", page, StringComparison.Ordinal);
        }
        foreach (var key in new[] { "platform.privacy.share_otp", "platform.privacy.share_llm", "platform.privacy.share_files" })
        {
            Assert.Contains("({0})", Ar(key), StringComparison.Ordinal);
            Assert.Contains("({0})", En(key), StringComparison.Ordinal);
        }
    }

    // ─── الجِهاتُ الثابِتَةُ الاسمِ — كُلٌّ مَربوطٌ بِطَرَفِه ──────────────────────────────────────────

    /// <summary>الاستِضافَةُ وقاعِدَةُ البَيانات: لا عَميلَ صادِراً يَراه الجَرد، فَالطَرَفُ وَثيقَةُ النَشرِ ومِلَفُّه.</summary>
    [Fact]
    public void The_hosting_line_names_what_the_deploy_document_and_workflow_deploy_to()
    {
        // نَصٌّ خامٌ لا كود: مُزيلُ التَعليقاتِ يَقطَعُ `https://` (مَقيسٌ: «huggingface.co=0» بِه).
        var deploy = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "docs", "DEPLOY.md"));
        var workflow = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, ".github", "workflows", "deploy-hf.yml"));
        output.WriteLine($"DEPLOY.md: Hugging Face Space={deploy.Contains("Hugging Face Space")} · Neon={Regex.Matches(deploy, "Neon").Count} · deploy-hf.yml: huggingface.co={Regex.Matches(workflow, "huggingface.co").Count}");
        Assert.StartsWith("# النَشر — مِن `main` إلى Hugging Face Space", deploy, StringComparison.Ordinal);
        Assert.Contains("huggingface.co/spaces/", workflow, StringComparison.Ordinal);
        Assert.Contains("Neon", deploy, StringComparison.Ordinal);
        foreach (var text in new[] { Ar("platform.privacy.share_hosting"), En("platform.privacy.share_hosting") })
        {
            Assert.Contains("Hugging Face", text, StringComparison.Ordinal);
            Assert.Contains("Neon", text, StringComparison.Ordinal);
        }
        Assert.Contains("@doc.Markup(\"platform.privacy.share_hosting\")", PrivacyShareInventoryTests.PrivacyPage(), StringComparison.Ordinal);
    }

    /// <summary>خِدمَةُ إشعاراتِ المُتَصَفِّح: الشَرِكاتُ الأَربَعُ هي مالِكاتُ المُضيفاتِ الَّتي يَقبَلُها بابُ الاشتِراكِ — لا أَقَلَّ ولا أَكثَر.</summary>
    [Fact]
    public void The_push_line_names_the_owner_of_every_push_host_the_subscription_door_admits()
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fcm.googleapis.com"]          = "Google",
            [".push.services.mozilla.com"] = "Mozilla",
            [".push.apple.com"]            = "Apple",
            [".notify.windows.com"]        = "Microsoft",
        };
        var hosts = PushSubscriptionDoor.ExactHosts.Concat(PushSubscriptionDoor.HostSuffixes).ToList();
        output.WriteLine($"مُضيفاتُ بابِ الاشتِراك: {hosts.Count} ({string.Join(", ", hosts)})");
        Assert.Equal(owners.Keys.OrderBy(k => k, StringComparer.Ordinal), hosts.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var company in owners.Values)
        {
            Assert.Contains(company, Ar("platform.privacy.share_push"), StringComparison.Ordinal);
            Assert.Contains(company, En("platform.privacy.share_push"), StringComparison.Ordinal);
        }
    }

    /// <summary>بَوّابَةُ المَتجَرِ ودَفعُ الاشتِراك: مَلَفّاتُ تَعريفِ الدَفعِ الحَقيقِيَّةُ بِأَسمائِها، وكُلٌّ في سَطرِه.</summary>
    [Fact]
    public void The_payment_lines_name_every_real_payment_definition()
    {
        var dir = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "core", "ACommerce.Platform.Providers", "Definitions");
        var payments = Directory.GetFiles(dir, "*.provider.json")
            .Select(f => JsonDocument.Parse(File.ReadAllText(f)).RootElement)
            .Where(e => e.GetProperty("capability").GetString() == "payments")
            .Select(e => e.GetProperty("slug").GetString()!)
            .Where(s => !s.StartsWith("mock_", StringComparison.Ordinal))
            .OrderBy(s => s, StringComparer.Ordinal).ToList();
        output.WriteLine($"تَعريفاتُ دَفعٍ حَقيقِيَّة: {payments.Count} ({string.Join(", ", payments)})");
        Assert.Equal(new[] { "moyasar_api", "moyasar_hosted", "paddle_billing", "paypal_subscriptions" }, payments);

        Assert.Contains("PayPal", Ar("platform.privacy.share_payment"), StringComparison.Ordinal);
        Assert.Contains("Paddle", Ar("platform.privacy.share_payment"), StringComparison.Ordinal);
        Assert.Contains("PayPal", En("platform.privacy.share_payment"), StringComparison.Ordinal);
        Assert.Contains("Paddle", En("platform.privacy.share_payment"), StringComparison.Ordinal);
        Assert.Contains("مُيَسِّر", Ar("platform.privacy.share_gateway"), StringComparison.Ordinal);
        Assert.Contains("Moyasar", Ar("platform.privacy.share_gateway"), StringComparison.Ordinal);
        Assert.Contains("Moyasar", En("platform.privacy.share_gateway"), StringComparison.Ordinal);
        // وما يَصِلُ البَوّابَةَ مِنّا هُوَ جِسمُ الفاتورَةِ نَفسُه: المَبلَغُ والوَصفُ والمُعَرِّفات — لا اسمُ المُشتَري ولا هاتِفُه.
        var moyasar = UpgradePromptPriceTests.Code("libs", "kits", "Payments", "ACommerce.Kit.Payments.Core", "MoyasarClient.cs");
        Assert.Contains("[\"description\"] = draft.Description", moyasar, StringComparison.Ordinal);
        var gateway = AccountDeletionRetentionTests.Bare(Ar("platform.privacy.share_gateway"));
        foreach (var said in new[] { "مبلغ الفاتورة", "وصفها", "معرفات الصفقات", "بطاقتك" })
            Assert.True(gateway.Contains(said, StringComparison.Ordinal), $"share_gateway لا يَقولُ «{said}»: {gateway}");
    }

    /// <summary>جُملَةُ المُعالَجَةِ خارِجَ المَملَكَة — بَعدَ القائِمَةِ في قِسمِها، بِاللُغَتَين.</summary>
    [Fact]
    public void The_outside_the_kingdom_sentence_follows_the_share_list()
    {
        var page = PrivacyShareInventoryTests.PrivacyPage();
        var legal = page.IndexOf("@doc.Markup(\"platform.privacy.share_legal\")", StringComparison.Ordinal);
        var abroad = page.IndexOf("@doc.Markup(\"platform.privacy.s5_abroad\")", StringComparison.Ordinal);
        var s6 = page.IndexOf("platform.privacy.s6_title", StringComparison.Ordinal);
        Assert.True(legal > 0 && abroad > legal && s6 > abroad, "الجُملَةُ لَيسَت بَعدَ القائِمَةِ في قِسمِها.");
        Assert.Contains("خارج المملكة العربية السعودية", AccountDeletionRetentionTests.Bare(Ar("platform.privacy.s5_abroad")), StringComparison.Ordinal);
        Assert.Contains("outside the Kingdom of Saudi Arabia", En("platform.privacy.s5_abroad"), StringComparison.Ordinal);
    }

    /// <summary>تاريخُ الوَثيقَةِ تَبَدَّلَ مَعَها وَحدَها — §9 يَعِدُ بِأَن «يُنشَرُ أَيُّ تَغييرٍ في هذِه الصَفحَةِ مَع تاريخِه».</summary>
    [Fact]
    public void The_privacy_page_carries_its_own_dated_update_line()
    {
        var page = PrivacyShareInventoryTests.PrivacyPage();
        Assert.Contains("@doc.Markup(\"platform.privacy.updated\")", page, StringComparison.Ordinal);
        Assert.DoesNotContain("platform.doc.updated", page, StringComparison.Ordinal);
        var date = Regex.Match(Ar("platform.privacy.updated"), @"\d{4}-\d{2}-\d{2}").Value;
        Assert.Equal("2026-10-29", date);
        Assert.Contains(date, En("platform.privacy.updated"), StringComparison.Ordinal);
    }
}
