using System.Net;
using System.Text;
using System.Text.Json;
using ACommerce.Kit.Payments;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ إشعارُ الدَفعِ الآليُّ مِن بَوّابَةِ المَتجَر (قَرارُ المالِك د5 (٢) «ج»، ‏2026-10-29) ═══════════════════════════════════════
//
// **القَرار**: «إشعار آليّ من ميسر إلى المنصّة عند كل دفع، محميّ بسرّ مشترك يُتحقَّق منه في كل إشعار. ويبقى زرّ «تحقّق» للمشتري احتياطًا».
// **والشَكلُ مِن الوَثيقَة** (مَقروءاً ‏2026-10-29): «Create Webhook» — ‏`POST /v1/webhooks` بِـ`http_method` و`url` و`shared_secret` و`events`؛ و«Webhook Payload» —
// `secret_token` «assigned by the consumer» و`type` و`data` (الدَفعُ، وفيه `invoice_id`). **التَوصيف**: `8cff16b` (صِفرُ نُقطَة، وتَعريفٌ بِلا وارِد).
// والصُفوفُ هُنا بِمُعالِجٍ مُزَيَّفٍ لا بِمِفتاحٍ حَقيقِيّ — والبُرهانُ الحَيُّ «ج» عَلى المالِك (مِفتاحُ اختِبارِ ميسر عَلى متجرِ تجربة).

public class GatewayPaymentNoticeTests(ITestOutputHelper output)
{
    private const string Secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static string Payload(string? secret = Secret, string type = MoyasarClient.PaymentPaidEvent, string? invoice = InvoiceId,
        bool withData = true)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = "evt-1",
            ["type"] = type,
            ["created_at"] = "2026-10-29T08:00:00.000Z",
            ["secret_token"] = secret,
            ["account_name"] = "store",
            ["live"] = false,
            ["data"] = withData ? new Dictionary<string, object?> { ["id"] = "pay-1", ["status"] = "paid", ["invoice_id"] = invoice } : null,
        });

    // ═══ ١) البَوّابَةُ النَقِيَّة — المُوجِبُ والسالِبُ لِكُلِّ رَمز ═══════════════════════════════════════════════════

    /// <summary><b>المُوجِب</b>: سِرٌّ يُطابِق، وحَدَثُ <c>payment_paid</c>، ودَفعٌ لِفاتورَة — إشعارٌ بِمُعَرِّفِها (بِصيغَةِ <c>D</c> ولو جاءَ بِحُروفٍ كَبيرَة)، و‏200.</summary>
    [Fact]
    public void A_paid_notice_with_the_store_secret_is_admitted_with_its_invoice()
    {
        var admitted = GatewayNoticeGate.Admit(Secret, Payload(invoice: InvoiceId.ToUpperInvariant()));

        Assert.Null(admitted.Violation);
        Assert.Equal(InvoiceId, admitted.Notice!.InvoiceId);
        Assert.Equal("pay-1", admitted.Notice.PaymentId);
        Assert.True(GatewayNoticeGate.IsValid(Secret, Payload()));
        Assert.Empty(GatewayNoticeGate.Validate(Secret, Payload()));
        Assert.Equal(200, GatewayNoticeGate.StatusFor(admitted.Violation?.Code));
    }

    /// <summary><b>السالِب لِكُلِّ رَمز</b> بِرَدِّه — والتَرتيب: «لا سِرَّ» قَبلَ الجِسم، و«السِرُّ لا يُطابِق» قَبلَ الحَدَثِ والفاتورَة (القاعِدَة ٦): حَدَثٌ آخَرُ بِسِرٍّ
    /// خاطِئٍ يُجابُ ‏401 لا ‏200.</summary>
    [Theory]
    [InlineData("no_secret", GatewayNoticeGate.NoticeUnavailable, 404)]
    [InlineData("empty_body", GatewayNoticeGate.NoticeUnreadable, 400)]
    [InlineData("not_json", GatewayNoticeGate.NoticeUnreadable, 400)]
    [InlineData("array", GatewayNoticeGate.NoticeUnreadable, 400)]
    [InlineData("no_token", GatewayNoticeGate.SecretMismatch, 401)]
    [InlineData("wrong_token", GatewayNoticeGate.SecretMismatch, 401)]
    [InlineData("wrong_token_other_type", GatewayNoticeGate.SecretMismatch, 401)]
    [InlineData("failed_payment", GatewayNoticeGate.NotPaymentPaid, 200)]
    [InlineData("no_data", GatewayNoticeGate.InvoiceMissing, 200)]
    [InlineData("no_invoice", GatewayNoticeGate.InvoiceMissing, 200)]
    [InlineData("invoice_not_uuid", GatewayNoticeGate.InvoiceMissing, 200)]
    public void Every_refused_notice_answers_its_code_and_status(string kind, string code, int status)
    {
        var (secret, body) = kind switch
        {
            "no_secret"              => ((string?)null, Payload()),
            "empty_body"             => (Secret, " "),
            "not_json"               => (Secret, "secret_token=" + Secret),
            "array"                  => (Secret, "[]"),
            "no_token"               => (Secret, Payload(secret: null)),
            "wrong_token"            => (Secret, Payload(secret: Secret[..^1] + "0")),
            "wrong_token_other_type" => (Secret, Payload(secret: "x", type: "payment_failed")),
            "failed_payment"         => (Secret, Payload(type: "payment_failed")),
            "no_data"                => (Secret, Payload(withData: false)),
            "no_invoice"             => (Secret, Payload(invoice: null)),
            "invoice_not_uuid"       => (Secret, Payload(invoice: "inv_1/../x")),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var admitted = GatewayNoticeGate.Admit(secret, body);

        Assert.Null(admitted.Notice);
        Assert.Equal(code, admitted.Violation!.Code);
        Assert.Equal(status, GatewayNoticeGate.StatusFor(code));
        Assert.False(GatewayNoticeGate.IsValid(secret, body));
        Assert.Contains(code, GatewayNoticeGate.All);
    }

    [Fact]
    public void The_notice_lexicon_is_closed_at_five()
    {
        Assert.Equal(5, GatewayNoticeGate.All.Count);
        Assert.Equal(GatewayNoticeGate.All.Count, GatewayNoticeGate.All.Distinct(StringComparer.Ordinal).Count());
    }

    // ═══ ٢) العَميل — «Create Webhook» بِحُقولِه ═════════════════════════════════════════════════════════════

    /// <summary>‏<c>POST /v1/webhooks</c> بِالحُقولِ الأَربَعَةِ لا غَير وبِـ<c>Basic</c> مِن المِفتاح، والمُعَرِّفُ يَعود؛ ورابِطٌ غَيرُ مُطلَقٍ أَو سِرٌّ أَبيَضُ لا يُرسَل.</summary>
    [Fact]
    public async Task Creating_a_webhook_posts_the_documented_fields_and_returns_its_id()
    {
        var handler = new MoyasarScriptedHandler().Then(HttpStatusCode.Created, "{\"id\":\"wh-1\",\"http_method\":\"post\",\"url\":\"https://s.example/zz/payments/notify\",\"events\":[\"payment_paid\"]}");
        var client = new MoyasarClient(new HttpClient(handler), NullLogger<MoyasarClient>.Instance);

        var res = await client.CreateWebhookAsync(Key, "https://s.example/zz/payments/notify", Secret);

        Assert.True(res.Ok, res.Failure?.Code);
        Assert.Equal("wh-1", res.Value);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Post, "https://api.moyasar.com/v1/webhooks"), (sent.Method, sent.RequestUri!.ToString()));
        Assert.Equal(("Basic", BasicOf(Key)), (sent.Headers.Authorization!.Scheme, sent.Headers.Authorization.Parameter));
        using var body = JsonDocument.Parse(handler.Bodies[0]!);
        Assert.Equal(new[] { "http_method", "url", "shared_secret", "events" }, body.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("post", body.RootElement.GetProperty("http_method").GetString());
        Assert.Equal(Secret, body.RootElement.GetProperty("shared_secret").GetString());
        Assert.Equal(new[] { "payment_paid" }, body.RootElement.GetProperty("events").EnumerateArray().Select(e => e.GetString()).ToArray());

        var silent = new MoyasarScriptedHandler();
        var offline = new MoyasarClient(new HttpClient(silent), NullLogger<MoyasarClient>.Instance);
        Assert.Equal(MoyasarFailures.Invalid, (await offline.CreateWebhookAsync(Key, "/zz/payments/notify", Secret)).Failure!.Code);
        Assert.Equal(MoyasarFailures.Invalid, (await offline.CreateWebhookAsync(Key, "javascript:alert(1)", Secret)).Failure!.Code);
        Assert.Equal(MoyasarFailures.Invalid, (await offline.CreateWebhookAsync(Key, "https://s.example/x", " ")).Failure!.Code);
        Assert.Empty(silent.Requests);
    }

    // ═══ ٣) التَسجيلُ بَعدَ الرَبط — السِرُّ يُختَمُ حينَ تَقبَلُه البَوّابَةُ وَحدَه ═══════════════════════════════════════

    private static TenantProviderBinding Binding(World w) => w.Docs.Read<TenantProviderBinding>(ProviderCapabilities.Payments)!;

    /// <summary><b>المُوجِب</b>: رَبطٌ بِمِفتاح ← سِرٌّ جَديدٌ يُسَجَّلُ بِعُنوانِ الوارِد ← البَوّابَةُ تَقبَل ← السِرُّ مَختومٌ في الرَبطِ بِسِياقِه ومَعَه مُعَرِّفُ التَسجيل،
    /// والمِفتاحُ كَما كان؛ و<see cref="GatewayPayments.NoticeSecretAsync"/> يُعيدُ السِرَّ الَّذي أُرسِلَ نَفسَه — فَالوارِدُ يُطابِقُه.</summary>
    [Fact]
    public async Task Binding_the_key_registers_a_notice_and_seals_the_secret_the_gateway_accepted()
    {
        var w = await PaidCart();
        var keyBefore = Binding(w).Values["secret_key"];
        w.Moyasar.Then(HttpStatusCode.Created, "{\"id\":\"wh-1\"}");

        var code = await w.Gateway.ConnectNoticesAsync(Slug, "https://store.example");
        var sent = JsonDocument.Parse(w.Moyasar.Bodies[0]!).RootElement;
        var after = Binding(w);
        output.WriteLine($"الرَمز: {code ?? "—"} · التَسجيل: {after.WebhookId} · المَسار: {w.Moyasar.Requests[0].RequestUri!.AbsolutePath} · الحُقول: {string.Join(", ", after.Values.Keys)}");

        Assert.Null(code);
        Assert.Equal("https://store.example/zz-gateway/payments/notify", sent.GetProperty("url").GetString());
        Assert.Equal(("wh-1", (string?)null), (after.WebhookId, after.WebhookRefusal));
        Assert.Equal(keyBefore.Cipher, after.Values["secret_key"].Cipher);
        Assert.True(after.Values["webhook_secret"].IsSealed);
        Assert.Equal(sent.GetProperty("shared_secret").GetString(), await w.Gateway.NoticeSecretAsync(Slug));
        Assert.Equal(64, sent.GetProperty("shared_secret").GetString()!.Length);
        Assert.True(GatewayNoticeGate.IsValid(await w.Gateway.NoticeSecretAsync(Slug), Payload(secret: sent.GetProperty("shared_secret").GetString())));
    }

    /// <summary><b>السالِب</b>: البَوّابَةُ تَرُدُّ التَسجيلَ (‏401) — رَمزُ فَشَلِها في الرَبطِ ولا سِرّ، والمِفتاحُ مَربوطٌ كَما هُوَ فَزِرُّ «تَحَقَّق» احتِياطٌ؛ ومَتجَرٌ بِلا رَبطٍ
    /// لا نِداءَ لَه؛ وتَعريفٌ بِلا وارِدٍ لا يُسأَلُ عَنه.</summary>
    [Fact]
    public async Task A_refused_registration_keeps_the_key_records_the_code_and_issues_no_secret()
    {
        var w = await PaidCart();
        w.Moyasar.Then(HttpStatusCode.Unauthorized, "{\"type\":\"authentication_error\",\"message\":\"Invalid authorization credentials\"}");

        var code = await w.Gateway.ConnectNoticesAsync(Slug, "https://store.example");

        Assert.Equal(MoyasarFailures.Refused, code);
        var after = Binding(w);
        Assert.Equal((null, MoyasarFailures.Refused), (after.WebhookId, after.WebhookRefusal));
        Assert.False(after.Values.ContainsKey("webhook_secret"));
        Assert.True(after.IsActive);
        Assert.Null(await w.Gateway.NoticeSecretAsync(Slug));

        var unbound = await PaidCart(binding: _ => null);
        Assert.Null(await unbound.Gateway.ConnectNoticesAsync(Slug, "https://store.example"));
        Assert.Empty(unbound.Moyasar.Requests);
        Assert.Null(await unbound.Gateway.NoticeSecretAsync(Slug));
    }

    /// <summary>
    /// <para><b>تَوصيفُ «اليَوم» (القاعِدَة ٣) — رَبطُ أَيِّ قُدرَةٍ يُعيدُ تَسجيلَ إشعارِ الدَفع</b> (مُراجَعَةُ تَيّارِ S3 الأولى، ‏2026-10-29): بابُ الرَبطِ في الاستوديو
    /// يُنادي <see cref="GatewayPayments.ConnectNoticesAsync"/> بَعدَ كُلِّ رَبطٍ بِلا القُدرَةِ الَّتي رُبِطَت، والخِدمَةُ تَقرَأُ رَبطَ الدَفعِ القائِمَ وَحدَه. فَمَتجَرٌ مَربوطٌ
    /// بِمِفتاحِ ميسّر يَربِطُ مُزَوِّدَ مِلَفّاتٍ أَو خَرائِط ← تَسجيلٌ ثانٍ عِندَ ميسّر بِسِرٍّ جَديد، ويُختَمُ فَوقَ القائِم؛ وإن رَدَّته البَوّابَةُ سَقَطَ السِرُّ العامِلُ فَيُجابُ
    /// الوارِدُ ‏404 بَعدَها. يَحمَرُّ يَومَ يُسأَلُ البابُ عَن القُدرَة.</para>
    /// </summary>
    [Fact]
    public async Task Today_connecting_notices_after_a_bind_of_another_capability_re_registers_and_reseals_the_payments_secret()
    {
        var w = await PaidCart();
        w.Moyasar.Then(HttpStatusCode.Created, "{\"id\":\"wh-1\"}");
        Assert.Null(await w.Gateway.ConnectNoticesAsync(Slug, "https://store.example"));
        var first = Binding(w).Values["webhook_secret"].Cipher;

        // رَبطُ مُزَوِّدِ مِلَفّات: البابُ يُنادي الخِدمَةَ بِحَرفِه — ولا يَقولُ لَها ما رُبِط.
        w.Moyasar.Then(HttpStatusCode.Created, "{\"id\":\"wh-2\"}");
        Assert.Null(await w.Gateway.ConnectNoticesAsync(Slug, "https://store.example"));
        var second = Binding(w);
        output.WriteLine($"نِداءاتُ ميسّر: {w.Moyasar.Requests.Count} · التَسجيل: {second.WebhookId} · السِرُّ تَبَدَّل: {first != second.Values["webhook_secret"].Cipher}");

        Assert.Equal(2, w.Moyasar.Requests.Count);
        Assert.Equal("wh-2", second.WebhookId);
        Assert.NotEqual(first, second.Values["webhook_secret"].Cipher);

        // ورَدُّ البَوّابَةِ لِلتَسجيلِ الثاني يُسقِطُ السِرَّ العامِل.
        w.Moyasar.Then(HttpStatusCode.Unauthorized, "{\"type\":\"authentication_error\"}");
        Assert.Equal(MoyasarFailures.Refused, await w.Gateway.ConnectNoticesAsync(Slug, "https://store.example"));
        Assert.False(Binding(w).Values.ContainsKey("webhook_secret"));
        Assert.Null(await w.Gateway.NoticeSecretAsync(Slug));

        var template = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs")));
        var bindAt = template.IndexOf("app.MapPost(\"/studio/apps/{slug}/providers/bind\"", StringComparison.Ordinal);
        var bind = template[bindAt..template.IndexOf(".DisableAntiforgery();", bindAt, StringComparison.Ordinal)];
        Assert.Contains("gateway.ConnectNoticesAsync(slug, Billing.PayPalOrderSurface.OriginFrom(req))", bind, StringComparison.Ordinal);
        Assert.DoesNotContain("read.Capability, Billing.PayPalOrderSurface", bind, StringComparison.Ordinal);
    }

    // ═══ ٤) الإشعارُ المَقبولُ يُثبَتُ بِالفاتورَةِ المَجلوبَة ═══════════════════════════════════════════════════════

    private static GatewayPaymentNotice Notice(string invoice = InvoiceId)
        => GatewayNoticeGate.Admit(Secret, Payload(invoice: invoice)).Notice!;

    /// <summary><b>المُوجِب</b>: فاتورَةٌ «paid» تُسَمّي الصَفقَتَين اللَّتَينِ عَلَيهِما مَرجِعُها — يُكتَبُ الدَفعُ عَلى كِلَيهِما بِالكاتِبِ القائِم، ويُعادانِ لِيُخبَرَ بائِعوهُما؛
    /// والإشعارُ الثاني لا يَكتُبُ شَيئاً. <b>ولا سُلطَةَ دافِع</b>: لا مُشتَريَ في الطَلَب.</summary>
    [Fact]
    public async Task An_admitted_notice_for_a_paid_invoice_records_the_payment_once_on_every_deal()
    {
        var w = await PaidCart();
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w)).Then(HttpStatusCode.OK, PaidInvoice(w));

        var first = await w.Gateway.ConfirmNoticeAsync(Slug, Notice());
        var again = await w.Gateway.ConfirmNoticeAsync(Slug, Notice());
        output.WriteLine($"الأَوَّل: ok={first.Ok} كُتِب={first.Confirmed.Count} · الثاني: ok={again.Ok} كُتِب={again.Confirmed.Count}");

        Assert.True(first.Ok, first.Code);
        Assert.Equal(w.Ids.OrderBy(i => i), first.Confirmed.OrderBy(i => i));
        Assert.All(w.Ids, id => Assert.True(GatewayReturn.IsConfirmed(w.Docs.Read<Deal>(id.ToString())!)));
        Assert.True(again.Ok);
        Assert.Empty(again.Confirmed);
        Assert.Equal((HttpMethod.Get, $"/v1/invoices/{InvoiceId}"), (w.Moyasar.Requests[0].Method, w.Moyasar.Requests[0].RequestUri!.AbsolutePath));
        Assert.Empty(w.Platform.Calls);
    }

    /// <summary><b>السالِب</b>: فاتورَةٌ لَم تُدفَع ⇒ «لَم يُدفَع» وصِفرُ كِتابَة؛ وفاتورَةٌ تُسَمّي صَفَقاتٍ لا تَحمِلُ مَرجِعَها (فاتورَةُ غَيرِنا) ⇒ لا تَعبُرُ البَوّابَة؛
    /// ولا مِفتاح ⇒ لا جَلب.</summary>
    [Fact]
    public async Task A_notice_is_not_proof_an_unpaid_or_foreign_invoice_writes_nothing()
    {
        var unpaid = await PaidCart();
        unpaid.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(unpaid, "initiated"));
        var saves = unpaid.Docs.SaveCalls;
        var res = await unpaid.Gateway.ConfirmNoticeAsync(Slug, Notice());
        Assert.Equal(GatewayPaymentProof.InvoiceUnpaid, res.Code);
        Assert.Equal(saves, unpaid.Docs.SaveCalls);

        var foreign = await PaidCart(invoice: false);
        foreign.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(foreign));
        var refused = await foreign.Gateway.ConfirmNoticeAsync(Slug, Notice());
        Assert.False(refused.Ok);
        Assert.Equal(GatewayPaymentProof.InvoiceMismatch, refused.Code);
        Assert.All(foreign.Ids, id => Assert.False(GatewayReturn.IsConfirmed(foreign.Docs.Read<Deal>(id.ToString())!)));

        var keyless = await PaidCart(binding: _ => null);
        Assert.Equal(MoyasarFailures.NotConfigured, (await keyless.Gateway.ConfirmNoticeAsync(Slug, Notice())).Code);
        Assert.Empty(keyless.Moyasar.Requests);
    }

    // ═══ ٥) الحَقلُ الَّذي نُصدِرُه لا يَملَؤُه صاحِبُ المَتجَر ═══════════════════════════════════════════════════

    /// <summary>دالَّةٌ واحِدَةٌ لِلصَفحَةِ وللقارِئ: <c>issued_secret</c> لا يُرسَمُ ولا يُقرَأُ مِن النَموذَج — فَقيمَةٌ يَدُسُّها طالِبٌ في الحَقلِ لا تُكتَب؛ والمِفتاحُ يُقرَأُ كَما كان.</summary>
    [Fact]
    public void The_issued_notice_secret_is_neither_rendered_nor_read_from_the_bind_form()
    {
        var def = SealedKeyDefinition();
        var issued = Assert.Single(def.Credential.Fields, f => f.Kind == CredentialKinds.IssuedSecret);
        Assert.False(ProviderBindSurface.IsFilledByOwner(issued));
        Assert.True(ProviderBindSurface.IsFilledByOwner(Assert.Single(def.Credential.Fields, f => f.Kind == CredentialKinds.SecretKey)));

        var read = ProviderBindSurface.FromForm(def.Slug, code => code == issued.Code ? "planted-secret" : Key, Slug, NewVault());
        Assert.Null(read.RefusalCode);
        Assert.False(read.Values.ContainsKey(issued.Code));
        Assert.True(read.Values.ContainsKey("secret_key"));

        var razor = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Components", "Pages", "StudioProviders.razor"));
        Assert.Contains("@foreach (var f in def.Credential.Fields.Where(Services.Providers.ProviderBindSurface.IsFilledByOwner))", razor, StringComparison.Ordinal);
    }
}
