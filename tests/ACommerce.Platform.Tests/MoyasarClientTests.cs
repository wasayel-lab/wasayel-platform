using System.Net;
using System.Text;
using System.Text.Json;
using ACommerce.Kit.Payments;
using ACommerce.Platform.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ عَميلُ بَوّابَةِ المُستَأجِر — ما يُرسَلُ بِالضَبط، وما يَعودُ رَمزاً لا استِثناءً (‏ADR-032 §٢‑هـ الخُطوَة 8) ═══════════
//
// **ولا مِفتاحَ بَوّابَةٍ حَقيقيّاً في هذِه البيئَة** — مُعالِجٌ وَهمِيٌّ يَلتَقِطُ الطَلَبَ ويُعيدُ **رُدودَ الوَثيقَةِ بِأَشكالِها**: مِثالُ
// «Fetch Invoice» بِحُقولِه (`id` · `status` · `amount` · `currency` · `url` · `metadata` · `payments`)، ورُدودُ الخَطَإِ ‏401 و‏404
// بِـ`type`/`message`. مَقروءَةٌ مِن وَثائِقِ ميسّر العامَّةِ يَومَ ‏2026-10-12 (‏`docs.moyasar.com/api/invoices/*` و`/api/authentication`).
// نَفسُ نَمَطِ `OtoClientTests`. والبُرهانُ بِمِفتاحِ اختِبارٍ حَقيقيٍّ في سِجِلِّ المالِكِ «ج».

/// <summary>مُعالِجٌ يَلتَقِطُ الطَلَباتِ ويَرُدُّ رُدوداً مُرَتَّبَة.</summary>
internal sealed class MoyasarScriptedHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _replies = new();

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();

    public MoyasarScriptedHandler Then(HttpStatusCode status, string body)
    {
        _replies.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body) });
        return this;
    }

    public MoyasarScriptedHandler ThenThrow()
    {
        _replies.Enqueue(() => throw new HttpRequestException("لا اتِّصال"));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
        return _replies.Count > 0
            ? _replies.Dequeue()()
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
    }
}

/// <summary>لوغٌ يُلتَقَطُ نَصّاً — لِيُقاسَ أَنّ المِفتاحَ لا يَبلُغُه.</summary>
internal sealed class MoyasarCapturingLogger : ILogger<MoyasarClient>
{
    public List<string> Lines { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
}

public class MoyasarClientTests(ITestOutputHelper output)
{
    internal const string Key = "sk_test_5cD409HqwpoOIDjqOPJPd";
    internal const string InvoiceId = "3fa85f64-5717-4562-b3fc-2c963f66afa6";

    /// <summary>رَدُّ الفاتورَةِ بِشَكلِ مِثالِ «Fetch Invoice» — الحالُ والمَبلَغُ و<c>metadata</c> مُعامَلات، وما سِواها بِحَرفِ المِثال.</summary>
    internal static string InvoiceJson(string status = "paid", long amount = 15000, string currency = "SAR",
        IReadOnlyDictionary<string, string>? metadata = null, string id = InvoiceId)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["status"] = status,
            ["amount"] = amount,
            ["currency"] = currency,
            ["description"] = "Radiator leak fix",
            ["logo_url"] = "https://example.com/default.png",
            ["amount_format"] = $"{amount / 100m:0.00} {currency}",
            ["url"] = "http://checkout.moyasar.com/invoices/efebc231-7795-4cfd-b69c-980fe4c02c49?lang=en",
            ["callback_url"] = null,
            ["expired_at"] = null,
            ["created_at"] = "2026-10-12T10:00:00.000Z",
            ["updated_at"] = "2026-10-12T10:05:00.000Z",
            ["back_url"] = "https://store.example/s/pay/return",
            ["success_url"] = "https://store.example/s/pay/return",
            ["payments"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "7c2f8a1e-0d1b-4c39-9a51-3b2e6f4d8a10", ["status"] = status == "paid" ? "paid" : "initiated",
                    ["amount"] = amount, ["fee"] = 0, ["currency"] = currency, ["invoice_id"] = id,
                },
            },
            ["metadata"] = metadata ?? new Dictionary<string, string> { ["deal_1"] = Guid.Empty.ToString("N") },
        });

    private static MoyasarClient Client(MoyasarScriptedHandler handler, ILogger<MoyasarClient>? log = null)
        => new(new HttpClient(handler), log ?? NullLogger<MoyasarClient>.Instance);

    private static MoyasarInvoiceDraft Draft(long amount = 15000, IReadOnlyDictionary<string, string>? metadata = null)
        => new(amount, "مَتجَر — طَلَبُ سَلَّة", "https://store.example/s/pay/return?deal=1",
            "https://store.example/s/pay/return?deal=1",
            metadata ?? new Dictionary<string, string> { ["deal_1"] = Guid.Empty.ToString("N") });

    private static string BasicOf(string key) => Convert.ToBase64String(Encoding.UTF8.GetBytes(key + ":"));

    // ═══ ١) ما يُرسَل — بِحَرفِ الوَثيقَة ═══════════════════════════════════════

    /// <summary><b>الإنشاءُ نِداءان مُوَثَّقان</b>: <c>POST /v1/invoices</c> بِالحُقولِ الخَمسَةِ لا غَير (و<c>metadata</c> لَيسَت مِنها —
    /// خارِجَ جَدوَلِ «Create Invoice»)، ثُمَّ <c>PUT /v1/invoices/{id}</c> بِـ<c>metadata</c> وَحدَها — وكِلاهُما بِـ
    /// <c>Authorization: Basic</c> مِن المِفتاحِ ونُقطَتَين؛ والرابِطُ المُستَضافُ يَعودُ.</summary>
    [Fact]
    public async Task Creating_an_invoice_posts_the_documented_fields_then_puts_the_metadata_with_basic_auth()
    {
        var metadata = new Dictionary<string, string> { ["deal_1"] = Guid.NewGuid().ToString("N"), ["deal_2"] = Guid.NewGuid().ToString("N") };
        var handler = new MoyasarScriptedHandler()
            .Then(HttpStatusCode.Created, InvoiceJson("initiated", metadata: new Dictionary<string, string>()))
            .Then(HttpStatusCode.OK, InvoiceJson("initiated", metadata: metadata));

        var res = await Client(handler).CreateInvoiceAsync(Key, Draft(metadata: metadata));

        Assert.True(res.Ok, res.Failure?.Code);
        Assert.Equal(InvoiceId, res.Value!.Id);
        Assert.Equal("initiated", res.Value.Status);
        Assert.StartsWith("http://checkout.moyasar.com/invoices/", res.Value.Url);
        Assert.Equal(metadata, res.Value.Metadata);

        Assert.Equal(2, handler.Requests.Count);
        var (post, put) = (handler.Requests[0], handler.Requests[1]);
        Assert.Equal((HttpMethod.Post, "https://api.moyasar.com/v1/invoices"), (post.Method, post.RequestUri!.ToString()));
        Assert.Equal((HttpMethod.Put, $"https://api.moyasar.com/v1/invoices/{InvoiceId}"), (put.Method, put.RequestUri!.ToString()));
        foreach (var r in handler.Requests)
            Assert.Equal(("Basic", BasicOf(Key)), (r.Headers.Authorization!.Scheme, r.Headers.Authorization.Parameter));

        using var created = JsonDocument.Parse(handler.Bodies[0]!);
        Assert.Equal(new[] { "amount", "currency", "description", "success_url", "back_url" },
            created.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(15000, created.RootElement.GetProperty("amount").GetInt64());
        Assert.Equal("SAR", created.RootElement.GetProperty("currency").GetString());

        using var tagged = JsonDocument.Parse(handler.Bodies[1]!);
        Assert.Equal(new[] { "metadata" }, tagged.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(metadata["deal_2"], tagged.RootElement.GetProperty("metadata").GetProperty("deal_2").GetString());
    }

    /// <summary><b>وتاريخُ الانتِهاءِ حَقلُ الوَثيقَةِ السادِس</b> (‏2026-10-29، قَرارُ المالِك د5 (٣) — «ويُرسل تاريخ انتهائها مع الفاتورة إلى ميسر»): مُسَوَّدَةٌ تَحمِلُ
    /// <c>ExpiresAt</c> تُرسِلُ <c>expired_at</c> آخِرَ الحُقولِ بِصيغَةِ مِثالِ الوَثيقَة (<c>2018-01-01T09:55:14.000Z</c>) بِالتَوقيتِ العالَمِيّ — ولو جاءَ التاريخُ
    /// بِإزاحَةِ الرِياض؛ والسالِبُ: الصَفُّ أَعلاه بِحَرفِه — مُسَوَّدَةٌ بِلا تاريخٍ بِخَمسَةِ حُقول.</summary>
    [Fact]
    public async Task An_invoice_draft_with_an_expiry_posts_expired_at_as_the_documented_utc_datetime()
    {
        var metadata = new Dictionary<string, string> { ["deal_1"] = Guid.NewGuid().ToString("N") };
        var handler = new MoyasarScriptedHandler()
            .Then(HttpStatusCode.Created, InvoiceJson("initiated", metadata: new Dictionary<string, string>()))
            .Then(HttpStatusCode.OK, InvoiceJson("initiated", metadata: metadata));
        var riyadh = new DateTimeOffset(2026, 10, 30, 12, 55, 14, 7, TimeSpan.FromHours(3));

        var res = await Client(handler).CreateInvoiceAsync(Key, Draft(metadata: metadata) with { ExpiresAt = riyadh });

        Assert.True(res.Ok, res.Failure?.Code);
        using var created = JsonDocument.Parse(handler.Bodies[0]!);
        Assert.Equal(new[] { "amount", "currency", "description", "success_url", "back_url", "expired_at" },
            created.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("2026-10-30T09:55:14.007Z", created.RootElement.GetProperty("expired_at").GetString());
        Assert.Equal("2026-10-30T09:55:14.007Z", MoyasarClient.ExpiryText(riyadh));
        Assert.True(DateTimeOffset.TryParse(MoyasarClient.ExpiryText(riyadh), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var back) && back == riyadh);
        using var tagged = JsonDocument.Parse(handler.Bodies[1]!);
        Assert.Equal(new[] { "metadata" }, tagged.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
    }

    /// <summary><b>الجَلبُ نِداءٌ واحِد</b>: <c>GET /v1/invoices/{id}</c> بِلا جِسمٍ وبِالمُصادَقَةِ نَفسِها؛ ويُقرَأُ مِثالُ الوَثيقَةِ
    /// كامِلاً — الحالُ والمَبلَغُ بِالهَلَلَةِ والعُملَةُ و<c>metadata</c>.</summary>
    [Fact]
    public async Task Fetching_an_invoice_gets_it_by_id_and_reads_the_documented_shape()
    {
        var handler = new MoyasarScriptedHandler().Then(HttpStatusCode.OK, InvoiceJson("paid", 100));

        var res = await Client(handler).FetchInvoiceAsync(Key, InvoiceId);

        Assert.True(res.Ok, res.Failure?.Code);
        Assert.True(res.Value!.IsPaid);
        Assert.Equal((100L, "SAR"), (res.Value.Amount, res.Value.Currency));
        Assert.Equal(Guid.Empty.ToString("N"), res.Value.Metadata["deal_1"]);
        var get = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Get, $"https://api.moyasar.com/v1/invoices/{InvoiceId}"), (get.Method, get.RequestUri!.ToString()));
        Assert.Null(handler.Bodies[0]);
        Assert.Equal(BasicOf(Key), get.Headers.Authorization!.Parameter);
    }

    /// <summary><b>المِفتاحُ لا يَبلُغُ عُنواناً ولا لوغاً ولا وَجهاً نَصِّيّاً</b> — في كُلِّ فَرعٍ مِن فُروعِ الفَشَلِ والنَجاح: رَأسُ
    /// المُصادَقَةِ وَحدَه يَحمِلُه (بِـbase64 كَما تَطلُبُه الوَثيقَة). والعَدَدُ يُطبَع.</summary>
    [Fact]
    public async Task The_key_never_reaches_a_url_a_log_line_or_a_printed_shape()
    {
        var log = new MoyasarCapturingLogger();
        var handler = new MoyasarScriptedHandler()
            .Then(HttpStatusCode.OK, InvoiceJson()).Then(HttpStatusCode.Unauthorized, "{\"type\":\"authentication_error\",\"message\":\"Invalid authorization credentials\",\"errors\":null}")
            .Then(HttpStatusCode.InternalServerError, "{}").ThenThrow().Then(HttpStatusCode.OK, "not json");
        var client = Client(handler, log);

        var shapes = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var r = await client.FetchInvoiceAsync(Key, InvoiceId);
            shapes.Add(r.ToString());
            if (r.Value is { } v) shapes.Add(v.ToString());
            if (r.Failure is { } f) shapes.Add(f.ToString());
        }

        output.WriteLine($"طَلَبات: {handler.Requests.Count} · أَسطُرُ لوغ: {log.Lines.Count} · وُجوه: {shapes.Count}");
        Assert.Equal(5, handler.Requests.Count);
        Assert.True(log.Lines.Count >= 4, $"أَداةٌ عَمياء: {log.Lines.Count} سَطرَ لوغٍ لِأَربَعِ فُشول.");
        Assert.All(handler.Requests, r => Assert.DoesNotContain(Key, r.RequestUri!.ToString(), StringComparison.Ordinal));
        Assert.All(log.Lines, l => Assert.DoesNotContain("sk_", l, StringComparison.Ordinal));
        Assert.All(shapes, s => Assert.DoesNotContain(Key, s, StringComparison.Ordinal));
        Assert.All(shapes, s => Assert.DoesNotContain("checkout.moyasar.com", s, StringComparison.Ordinal));
    }

    // ═══ ٢) مَعجَمُ الفَشَلِ — خَمسَةٌ لا سادِسَ لَها، لِكُلٍّ مُوجَبٌ وسالِب ═══════════════════════════

    /// <summary><c>gateway_not_configured</c> — مُوجَب: بِلا مِفتاحٍ لا نِداءَ أَصلاً؛ سالِب: بِمِفتاحٍ يُنادى.</summary>
    [Fact]
    public async Task NotConfigured_fires_without_a_key_and_never_with_one()
    {
        var silent = new MoyasarScriptedHandler();
        foreach (var key in new[] { "", "   " })
        {
            Assert.Equal(MoyasarFailures.NotConfigured, (await Client(silent).FetchInvoiceAsync(key, InvoiceId)).Failure!.Code);
            Assert.Equal(MoyasarFailures.NotConfigured, (await Client(silent).CreateInvoiceAsync(key, Draft())).Failure!.Code);
        }
        Assert.Empty(silent.Requests);

        var called = new MoyasarScriptedHandler().Then(HttpStatusCode.OK, InvoiceJson());
        Assert.True((await Client(called).FetchInvoiceAsync(Key, InvoiceId)).Ok);
        Assert.Single(called.Requests);
    }

    /// <summary><c>gateway_invoice_unpaid</c> لَيسَ مِن هُنا؛ و<c>gateway_invalid_request</c> — مُوجَب: مُسَوَّدَةٌ خارِجَ حُدودِ الوَثيقَةِ
    /// (مَبلَغٌ ‏99، وَصفٌ فارِغ، <c>metadata</c> فارِغَةٌ أَو بِواحِدٍ وثَلاثينَ مِفتاحاً أَو قيمَةٍ بِخَمسِ مِئَةٍ ووَاحِد، رابِطُ عَودَةٍ
    /// نِسبِيّ) ومُعَرِّفُ فاتورَةٍ لَيسَ uuid — بِلا نِداء؛ سالِب: الحُدودُ نَفسُها عِندَ طَرَفِها المَقبول تُرسَل.</summary>
    [Fact]
    public async Task Invalid_fires_for_a_draft_outside_the_documented_bounds_and_never_inside_them()
    {
        static IReadOnlyDictionary<string, string> Keys(int n, int valueLength = 32)
            => Enumerable.Range(1, n).ToDictionary(i => $"deal_{i}", _ => new string('a', valueLength));

        var silent = new MoyasarScriptedHandler();
        var bad = new[]
        {
            Draft(amount: 99),
            Draft() with { Description = "  " },
            Draft(metadata: new Dictionary<string, string>()),
            Draft(metadata: Keys(31)),
            Draft(metadata: Keys(1, 501)),
            Draft() with { SuccessUrl = "/s/pay/return" },
            Draft() with { BackUrl = "javascript:alert(1)" },
        };
        foreach (var d in bad)
            Assert.Equal(MoyasarFailures.Invalid, (await Client(silent).CreateInvoiceAsync(Key, d)).Failure!.Code);
        foreach (var id in new[] { "../invoices", "3fa85f64?x=1", "", "not-a-uuid" })
            Assert.Equal(MoyasarFailures.Invalid, (await Client(silent).FetchInvoiceAsync(Key, id)).Failure!.Code);
        Assert.Empty(silent.Requests);

        var edge = new[] { Draft(amount: 100), Draft(metadata: Keys(30)), Draft(metadata: Keys(1, 500)) };
        foreach (var d in edge)
        {
            Assert.True(MoyasarClient.IsSendable(d));
            var h = new MoyasarScriptedHandler().Then(HttpStatusCode.Created, InvoiceJson("initiated")).Then(HttpStatusCode.OK, InvoiceJson("initiated"));
            Assert.True((await Client(h).CreateInvoiceAsync(Key, d)).Ok);
        }
        output.WriteLine($"سَوالِب: {bad.Length + 4} · مُوجَباتُ الطَرَف: {edge.Length}");
    }

    /// <summary><c>gateway_unreachable</c> — مُوجَب: ‏500 و‏503 وانقِطاع؛ سالِب: ‏200 يُقرَأ.</summary>
    [Fact]
    public async Task Unreachable_fires_for_5xx_and_a_dropped_connection_and_never_for_a_success()
    {
        var handler = new MoyasarScriptedHandler()
            .Then(HttpStatusCode.InternalServerError, "{}").Then(HttpStatusCode.ServiceUnavailable, "").ThenThrow()
            .Then(HttpStatusCode.OK, InvoiceJson());
        var client = Client(handler);

        var first = await client.FetchInvoiceAsync(Key, InvoiceId);
        Assert.Equal((MoyasarFailures.Unreachable, 500), (first.Failure!.Code, first.Failure.HttpStatus));
        Assert.Equal(MoyasarFailures.Unreachable, (await client.FetchInvoiceAsync(Key, InvoiceId)).Failure!.Code);
        var dropped = await client.FetchInvoiceAsync(Key, InvoiceId);
        Assert.Equal((MoyasarFailures.Unreachable, (int?)null), (dropped.Failure!.Code, dropped.Failure.HttpStatus));
        Assert.True((await client.FetchInvoiceAsync(Key, InvoiceId)).Ok);
    }

    /// <summary><c>gateway_refused</c> — مُوجَب: ‏401 (رَدُّ المِثالِ «Invalid authorization credentials»)، و‏403، و‏404 (فاتورَةٌ لا يَعرِفُها
    /// هذا المِفتاح — «record_not_found»)، و‏400؛ وفي الإنشاءِ يَقِفُ عِندَ النِداءِ المَرفوضِ فَلا يُرسَلُ الثاني. سالِب: ‏201 ثُمَّ ‏200.</summary>
    [Fact]
    public async Task Refused_fires_for_4xx_with_its_status_and_never_for_a_success()
    {
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.BadRequest })
        {
            var h = new MoyasarScriptedHandler().Then(status,
                "{\"type\":\"record_not_found\",\"message\":\"The <object type> record you were looking for was not found.\",\"errors\":null}");
            var r = await Client(h).FetchInvoiceAsync(Key, InvoiceId);
            Assert.Equal((MoyasarFailures.Refused, (int)status), (r.Failure!.Code, r.Failure.HttpStatus));
        }

        var create = new MoyasarScriptedHandler().Then(HttpStatusCode.Unauthorized, "{\"type\":\"authentication_error\"}");
        Assert.Equal(MoyasarFailures.Refused, (await Client(create).CreateInvoiceAsync(Key, Draft())).Failure!.Code);
        Assert.Single(create.Requests);

        var ok = new MoyasarScriptedHandler().Then(HttpStatusCode.Created, InvoiceJson("initiated")).Then(HttpStatusCode.OK, InvoiceJson("initiated"));
        Assert.True((await Client(ok).CreateInvoiceAsync(Key, Draft())).Ok);
    }

    /// <summary><c>gateway_unreadable</c> — مُوجَب: نَجاحٌ لَيسَ JSON، أَو بِلا <c>id</c>، أَو بِمَبلَغٍ نَصّاً، أَو إنشاءٌ بِلا <c>url</c>؛
    /// سالِب: مِثالُ الوَثيقَةِ يُقرَأ.</summary>
    [Fact]
    public async Task Unreadable_fires_for_a_success_missing_a_required_field_and_never_for_the_documented_shape()
    {
        var bodies = new[]
        {
            "not json",
            "[]",
            "{\"status\":\"paid\",\"amount\":100,\"currency\":\"SAR\"}",
            "{\"id\":\"" + InvoiceId + "\",\"status\":\"paid\",\"amount\":\"100\",\"currency\":\"SAR\"}",
        };
        foreach (var body in bodies)
        {
            var h = new MoyasarScriptedHandler().Then(HttpStatusCode.OK, body);
            Assert.Equal(MoyasarFailures.Unreadable, (await Client(h).FetchInvoiceAsync(Key, InvoiceId)).Failure!.Code);
        }

        var noUrl = new MoyasarScriptedHandler().Then(HttpStatusCode.Created,
            "{\"id\":\"" + InvoiceId + "\",\"status\":\"initiated\",\"amount\":15000,\"currency\":\"SAR\"}");
        Assert.Equal(MoyasarFailures.Unreadable, (await Client(noUrl).CreateInvoiceAsync(Key, Draft())).Failure!.Code);
        Assert.Single(noUrl.Requests);   // لا تُرسَلُ metadata لِفاتورَةٍ لا رابِطَ لَها

        var documented = new MoyasarScriptedHandler().Then(HttpStatusCode.OK, InvoiceJson());
        Assert.True((await Client(documented).FetchInvoiceAsync(Key, InvoiceId)).Ok);
    }

    /// <summary>المَعجَمُ مُغلَقٌ ومُعَدودٌ — خَمسَة، ولِكُلٍّ رِسالَةٌ بِلا سِرّ، ورَمزٌ خارِجَه يَرمي.</summary>
    [Fact]
    public void The_failure_vocabulary_is_closed_at_five_and_every_code_has_a_message()
    {
        Assert.Equal(5, MoyasarFailures.All.Count);
        Assert.Equal(5, MoyasarFailures.All.Distinct(StringComparer.Ordinal).Count());
        foreach (var code in MoyasarFailures.All)
        {
            var f = MoyasarFailures.Describe(code, 401);
            Assert.Equal(code, f.Code);
            Assert.True(f.MessageAr.Length > 20);
            Assert.DoesNotContain("sk_", f.MessageAr, StringComparison.Ordinal);
        }
        Assert.Throws<ArgumentException>(() => MoyasarFailures.Describe("gateway_other", null));
        Assert.False(MoyasarFailures.Contains("gateway_other"));
    }

    // ═══ ٣) المَبلَغُ بِالهَلَلَة — بِلا تَقريب ═════════════════════════════════════

    [Theory]
    [InlineData("1.00", 100L)]
    [InlineData("150", 15000L)]
    [InlineData("10.25", 1025L)]
    [InlineData("0.99", null)]       // دونَ الأَدنى «‏>= 100»
    [InlineData("10.005", null)]     // كَسرُ هَلَلَةٍ لا يُقَرَّب
    [InlineData("0", null)]
    [InlineData("-5", null)]
    public void The_smallest_unit_is_exact_and_at_least_the_documented_minimum(string sar, long? halalas)
        => Assert.Equal(halalas, MoyasarClient.ToSmallestUnit(decimal.Parse(sar, System.Globalization.CultureInfo.InvariantCulture)));

    // ═══ ٤) التَسجيلُ وشَرطُ الاستِخراج ═════════════════════════════════════════

    /// <summary>يُحَلُّ مِن وِعاءِ الخِدَماتِ بِسَطرِ التَسجيلِ نَفسِه — والقالِبُ يُناديه بِجِوارِ الخِزانَة.</summary>
    [Fact]
    public void The_client_resolves_from_its_one_registration_line_which_the_template_calls()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddMoyasarGateway();
        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetRequiredService<MoyasarClient>());

        var template = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs"));
        Assert.Equal(1, template.Split("ACommerce.Kit.Payments.MoyasarClientExtensions.AddMoyasarGateway(services);").Length - 1);
        Assert.True(template.IndexOf("ProviderVault.FromKek(", StringComparison.Ordinal)
                    < template.IndexOf("AddMoyasarGateway(services);", StringComparison.Ordinal));
    }

    /// <summary><b>شَرطُ الاستِخراجِ مَقيسٌ لا مَكتوبٌ وَحدَه</b> (القاعِدَتان ١ و٢): العَميلُ واحِدٌ بِلا واجِهَة لِأَنّ الكاتالوجَ فيه
    /// تَعريفُ دَفعٍ واحِدٌ بِحَقلٍ سِرِّيٍّ تَختِمُه الخِزانَة. تَعريفٌ ثانٍ مِثلُه يُحمِرُّ هذا الصَفّ — فَيُقَرَّرُ يَومَها كَيفَ يَختارُ
    /// الكودُ بَوّابَتَه مِن التَعريف، لا سَطرٌ يُخَمِّن.</summary>
    [Fact]
    public void Exactly_one_payments_definition_carries_a_sealed_secret_key_field()
    {
        var scanned = ProviderCatalog.Definitions.Count;
        var keyed = ProviderCatalog.Definitions
            .Where(d => d.Capability == ProviderCapabilities.Payments
                        && d.Credential.Fields.Any(f => f.Kind == CredentialKinds.SecretKey && ProviderVault.Seals(f.Kind)))
            .ToList();
        output.WriteLine($"فُحِصَ {scanned} تَعريفاً؛ دَفعٌ بِمِفتاحٍ سِرِّيٍّ مَختوم: {keyed.Count}");
        Assert.True(scanned > 0, "أَداةٌ عَمياء: صِفرُ تَعريف.");
        Assert.Single(keyed);
    }

    // ═══ ٥) العَمَلِيَّةُ الثالِثَة — إلغاءُ الفاتورَة (‏2026-10-12، مُراجَعَةُ تَيّارِ S9 الثانِيَة) ═══════════════════════
    //
    // مِن صَفحَةِ «Cancel invoice» (`docs.moyasar.com/api/invoices/06-cancel-invoice`، مَقروءَةً ‏2026-10-12): `PUT /invoices/:id/cancel`،
    // ومُعامِلُ المَسارِ uuid، ولا جِسم؛ والرَدُّ ‏200 الفاتورَةُ بِمِثالِها «canceled»، و‏401 «authentication_error» و‏403 «api_error · User not
    // authorized» و‏404 «record_not_found» بِأَشكالِها.

    /// <summary><b>المُوجِب</b>: نِداءٌ واحِد — <c>PUT /v1/invoices/{id}/cancel</c> بِلا جِسمٍ وبِـ<c>Authorization: Basic</c> مِن المِفتاحِ ونُقطَتَين،
    /// ويُقرَأُ رَدُّه الفاتورَةَ بِحالِها «canceled». والمَسارُ مِن ثابِتَي العَميلِ نَفسَيهِما، والمِفتاحُ لا يَبلُغُ العُنوانَ ولا اللوغ.</summary>
    [Fact]
    public async Task Cancelling_an_invoice_puts_to_its_cancel_path_without_a_body_and_reads_the_documented_shape()
    {
        var log = new MoyasarCapturingLogger();
        var handler = new MoyasarScriptedHandler().Then(HttpStatusCode.OK, InvoiceJson("canceled"));

        var res = await Client(handler, log).CancelInvoiceAsync(Key, InvoiceId);

        Assert.True(res.Ok, res.Failure?.Code);
        Assert.Equal((InvoiceId, "canceled", 15000L), (res.Value!.Id, res.Value.Status, res.Value.Amount));
        Assert.False(res.Value.IsPaid);
        var put = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Put, $"https://api.moyasar.com/v1/invoices/{InvoiceId}/cancel"), (put.Method, put.RequestUri!.ToString()));
        Assert.Equal(MoyasarClient.BaseUrl + MoyasarClient.InvoicesPath + "/" + InvoiceId + MoyasarClient.CancelSuffix, put.RequestUri.ToString());
        Assert.Null(handler.Bodies[0]);
        Assert.Equal(("Basic", BasicOf(Key)), (put.Headers.Authorization!.Scheme, put.Headers.Authorization.Parameter));
        Assert.DoesNotContain(Key, put.RequestUri.ToString(), StringComparison.Ordinal);
        Assert.All(log.Lines, l => Assert.DoesNotContain("sk_", l, StringComparison.Ordinal));
        Assert.DoesNotContain(Key, res.Value.ToString(), StringComparison.Ordinal);
    }

    /// <summary><b>السالِب</b>: بِلا مِفتاحٍ أَو بِمُعَرِّفٍ لَيسَ uuid لا نِداءَ أَصلاً (<c>gateway_not_configured</c>، <c>gateway_invalid_request</c>)؛
    /// ورُدودُ الوَثيقَةِ ‏401 و‏403 و‏404 <c>gateway_refused</c> بِحالِها، و‏503 <c>gateway_unreachable</c>، ونَجاحٌ لا يُقرَأُ <c>gateway_unreadable</c> —
    /// المَعجَمُ الخُماسيُّ نَفسُه لا سادِس. والعَدَدُ يُطبَع.</summary>
    [Fact]
    public async Task Cancelling_calls_nothing_without_a_key_or_a_uuid_and_returns_each_gateway_failure_by_its_code()
    {
        var silent = new MoyasarScriptedHandler();
        Assert.Equal(MoyasarFailures.NotConfigured, (await Client(silent).CancelInvoiceAsync("  ", InvoiceId)).Failure!.Code);
        var badIds = new[] { "../invoices", InvoiceId + "/cancel", "", "not-a-uuid" };
        foreach (var id in badIds)
            Assert.Equal(MoyasarFailures.Invalid, (await Client(silent).CancelInvoiceAsync(Key, id)).Failure!.Code);
        Assert.Empty(silent.Requests);

        var refusals = new (HttpStatusCode Status, string Body)[]
        {
            (HttpStatusCode.Unauthorized, "{\"type\":\"authentication_error\",\"message\":\"Invalid authorization credentials\",\"errors\":null}"),
            (HttpStatusCode.Forbidden, "{\"type\":\"api_error\",\"message\":\"User not authorized\",\"errors\":null}"),
            (HttpStatusCode.NotFound, "{\"type\":\"record_not_found\",\"message\":\"The <object type> record you were looking for was not found.\",\"errors\":null}"),
        };
        foreach (var (status, body) in refusals)
        {
            var h = new MoyasarScriptedHandler().Then(status, body);
            var r = await Client(h).CancelInvoiceAsync(Key, InvoiceId);
            Assert.Equal((MoyasarFailures.Refused, (int)status), (r.Failure!.Code, r.Failure.HttpStatus));
            Assert.Single(h.Requests);
        }

        var down = new MoyasarScriptedHandler().Then(HttpStatusCode.ServiceUnavailable, "");
        Assert.Equal(MoyasarFailures.Unreachable, (await Client(down).CancelInvoiceAsync(Key, InvoiceId)).Failure!.Code);
        var odd = new MoyasarScriptedHandler().Then(HttpStatusCode.OK, "not json");
        Assert.Equal(MoyasarFailures.Unreadable, (await Client(odd).CancelInvoiceAsync(Key, InvoiceId)).Failure!.Code);
        output.WriteLine($"بِلا نِداء: {1 + badIds.Length} · رَفضُ البَوّابَة: {refusals.Length} · بِلا جَواب: 1 · لا يُقرَأ: 1");
    }
}
