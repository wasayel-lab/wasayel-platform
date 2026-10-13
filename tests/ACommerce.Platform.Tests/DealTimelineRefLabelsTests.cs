using System.Text;
using System.Text.RegularExpressions;
using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ سَطرُ «attach key=value» في سِجِلِّ الصَفقَةِ لِلطَرَفَين — تَوصيفُ «اليَوم» ═══
//
// مُراجَعَةُ المَوجَتَين 2–3 (‏2026-10-13، G4 (xxvi)): `DealsService.AttachRefAsync` تَكتُبُ لِكُلِّ مَرجِعٍ سَطرَ
// `note` نَصُّه `attach {key}={value}`، وصَفحَةُ الصَفقَةِ لِلعُضو (`MyDealDetail` — يَراها المُشتَري والبائِعُ مَعاً) تَرسُمُ
// `e.Note` كَما هُوَ: فَيَقرَأُ الطَرَفانِ «attach oto_order_id=…» و«attach oto_label_url=https://…» و«attach listing_owner=<مُعَرِّفُ
// مُستَخدِم>» و«attach payment_error=<نَصُّ استِثناء>» — مَفاتيحَ داخِلِيَّةً بِالإنكليزِيَّة، ومُعَرِّفاتٍ ونُصوصَ أَخطاءٍ لا تَعنيهِما.
//
// **ما يَبقى بِحَرفِه بَعدَ التَبديل** (القاعِدَة ٣): شَكلُ السَطرِ الَّذي يَكتُبُه الكاتِب (صَفّا ١)، وجَردُ ما يُلحَقُ اليَومَ بِأَسماءِ
// مَفاتيحِه مِن كُلِّ مَوضِعِ نِداء (صَفُّ ٢) — فَالتَبديلُ في القارِئِ وَحدَه. **وما يُقلَبُ بِاسمِه**: صَفّا ٣ (الصَفحَةُ تَرسُمُ السَطرَ خاماً
// ولا تُرشِّحُ شَيئاً مِن السِجِلّ).

public class DealTimelineRefLabelsTests
{
    private const string Slug = "zz-timeline-refs";
    private static readonly Guid Buyer = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private readonly ITestOutputHelper output;
    public DealTimelineRefLabelsTests(ITestOutputHelper output) => this.output = output;

    /// <summary>مُزَوِّدٌ يَرمي عِندَ أَيِّ نِداء — الإلحاقُ لا يَمَسُّ المال، فَإن مَسَّه احمَرَّ الصَفّ.</summary>
    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("الإلحاقُ لا يَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");
        public string ProviderName => "forbidden";
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

    private static T Now<T>(Task<T> task)
    {
        Assert.True(task.IsCompleted, "نِداءٌ لَم يَكتَمِل في عالَمٍ مُتَزامِن — الأَداةُ تَكذِب.");
        return task.GetAwaiter().GetResult();
    }

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    private static string PageCode() => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(
        Source(Template, "Components", "Pages", "MyDealDetail.razor")));

    // ═══ ١) الكاتِب: سَطرُ `note` واحِدٌ نَصُّه «attach key=value» — يَبقى بِحَرفِه ═══════════════

    /// <summary>الإلحاقُ يَكتُبُ المَرجِعَ في <c>Refs</c> وسَطراً واحِداً في السِجِلّ: فِعلُه <c>note</c>، بِلا فاعِل، ونَصُّه
    /// <c>attach {key}={value}</c> — والقيمَةُ كَما هِيَ ولو حَمَلَت «=» (رابِطٌ بِاستِعلام).</summary>
    [Theory]
    [InlineData("oto_order_id", "8c3c2a52f3b84f4f9d1e0b7a6c5d4e3f")]
    [InlineData("oto_tracking_url", "https://example.com/track?id=7&lang=ar")]
    [InlineData("payment_error", "Gateway said: amount=0 is invalid")]
    public void AttachRefAsync_writes_one_note_line_attach_key_equals_value(string key, string value)
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var deal = Now(deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "سِلعَة", 100m, new()));
        var before = deal.Timeline.Count;

        var after = Now(deals.AttachRefAsync(Slug, deal.Id, key, value));

        Assert.NotNull(after);
        Assert.Equal(before + 1, after!.Timeline.Count);
        var line = after.Timeline[^1];
        Assert.Equal("note", line.Action);
        Assert.Null(line.ActorId);
        Assert.Null(line.ActorName);
        Assert.Equal($"attach {key}={value}", line.Note);
        Assert.Equal(value, after.Refs[key]);
    }

    // ═══ ٢) جَردُ ما يُلحَقُ اليَوم — مِن كُلِّ مَوضِعِ نِداءٍ في الكودِ الحَيّ — يَبقى بِحَرفِه ═══════════════

    /// <summary>الوَسيطُ الثالِثُ لِكُلِّ نِداءٍ كَما كُتِبَ (مُطَبَّعَ البَياض) ← المَفاتيحُ الَّتي يَكتُبُها. و«key» حَلقَةُ
    /// <c>DealShipping.AttachLinksAsync</c> عَلى رابِطَيِ البوليصَةِ والتَتَبُّع (مُثَبَّتَةٌ بِحَرفِها في الصَفِّ نَفسِه).</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> WrittenKeys = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["\"payment_id\""]       = new[] { "payment_id" },
        ["\"payment_status\""]   = new[] { "payment_status" },
        ["ACommerce.Kit.Payments.SimulatedPaymentProvider.ModeRefKey"] = new[] { SimulatedPaymentProvider.ModeRefKey },
        ["\"payment_error\""]    = new[] { "payment_error" },
        ["\"listing_owner\""]    = new[] { DealsService.ListingOwnerRef },
        ["$\"review_{(targetIsCounterparty ? \"cp\" : \"init\")}\""] = new[] { "review_cp", "review_init" },
        ["GatewayPaymentProof.InvoiceRef"] = new[] { GatewayPaymentProof.InvoiceRef },
        ["InvoiceUrlRef"]        = new[] { GatewayPayments.InvoiceUrlRef },
        ["OrderRef"]             = new[] { DealShipping.OrderRef },
        ["OtoIdRef"]             = new[] { DealShipping.OtoIdRef },
        ["ClosedRef"]            = new[] { DealShipping.ClosedRef },
        ["key"]                  = new[] { DealShipping.LabelRef, DealShipping.TrackingRef },
    };

    /// <summary>مَواضِعُ نِداءِ <c>AttachRefAsync(</c> في <c>libs/</c> و<c>apps/</c> (‏<c>.cs</c> و<c>.razor</c>، بِلا تَعليقات، بِلا التَعريف)
    /// ← الوَسيطُ الثالِثُ لِكُلٍّ مِنها بِمُحَلِّلٍ يَعُدُّ الأَقواسَ ويَتَخَطّى النُصوص.</summary>
    internal static List<(string File, string Arg)> AttachCallSites()
    {
        var sites = new List<(string, string)>();
        foreach (var dir in new[] { "libs", "apps" })
        foreach (var path in Directory.EnumerateFiles(Path.Combine(RepoRoot, dir), "*.*", SearchOption.AllDirectories))
        {
            if (!(path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".razor", StringComparison.Ordinal))) continue;
            var sep = Path.DirectorySeparatorChar;
            if (path.Contains($"{sep}bin{sep}") || path.Contains($"{sep}obj{sep}")) continue;
            var raw = File.ReadAllText(path);
            if (!raw.Contains("AttachRefAsync(", StringComparison.Ordinal)) continue;
            var code = WriteEndpointGuardTests.StripComments(path.EndsWith(".razor", StringComparison.Ordinal)
                ? WriteEndpointGuardTests.StripMarkupComments(raw) : raw);
            var at = 0;
            while ((at = code.IndexOf("AttachRefAsync(", at, StringComparison.Ordinal)) >= 0)
            {
                var open = at + "AttachRefAsync".Length;
                var isDefinition = code[Math.Max(0, at - 40)..at].TrimEnd().EndsWith("Task<Deal?>", StringComparison.Ordinal);
                at = open + 1;
                if (isDefinition) continue;
                var args = Arguments(code, open);
                Assert.True(args.Count >= 4, $"نِداءٌ بِأَقَلَّ مِن أَربَعَةِ وُسَطاءَ في {Path.GetFileName(path)}: {string.Join(" | ", args)}");
                sites.Add((Path.GetFileName(path), Regex.Replace(args[2], @"\s+", " ").Trim()));
            }
        }
        return sites;
    }

    /// <summary>وُسَطاءُ النِداءِ مِن القَوسِ عِندَ <paramref name="open"/> إلى قَرينِه — فَواصِلُ المُستَوى الأَوَّلِ وَحدَها؛ والنُصوصُ
    /// (العادِيَّةُ والحَرفِيَّةُ <c>@"…"</c> والمُقحَمَةُ <c>$"…{…}…"</c> بِنُصوصٍ داخِلَ ثُقوبِها) والمَحارِفُ لا تُقسَم.</summary>
    internal static List<string> Arguments(string code, int open)
    {
        var args = new List<string>();
        var sb = new StringBuilder();
        var depth = 0;
        var ctx = new Stack<char>();      // 's' نَصٌّ عادِيّ · 'v' نَصٌّ حَرفِيّ · 'i' نَصٌّ مُقحَم · 'h' ثُقبُ إقحام
        var braces = new Stack<int>();    // أَقواسُ «{» المَفتوحَةُ داخِلَ كُلِّ ثُقب
        for (var i = open; i < code.Length; i++)
        {
            var c = code[i];
            var top = ctx.Count == 0 ? 'c' : ctx.Peek();
            switch (top)
            {
                case 's':
                    sb.Append(c);
                    if (c == '\\') sb.Append(code[++i]);
                    else if (c == '"') ctx.Pop();
                    continue;
                case 'v':
                    sb.Append(c);
                    if (c == '"' && i + 1 < code.Length && code[i + 1] == '"') sb.Append(code[++i]);
                    else if (c == '"') ctx.Pop();
                    continue;
                case 'i':
                    sb.Append(c);
                    if (c == '\\') sb.Append(code[++i]);
                    else if (c == '{' && i + 1 < code.Length && code[i + 1] == '{') sb.Append(code[++i]);
                    else if (c == '{') { ctx.Push('h'); braces.Push(0); }
                    else if (c == '"') ctx.Pop();
                    continue;
            }

            // شِفرَة — في المُستَوى الأَعلى أَو داخِلَ ثُقب.
            if (c == '"')
            {
                var p1 = i > 0 ? code[i - 1] : ' ';
                var p2 = i > 1 ? code[i - 2] : ' ';
                ctx.Push(p1 == '$' || (p1 == '@' && p2 == '$') ? 'i' : p1 == '@' ? 'v' : 's');
                sb.Append(c);
                continue;
            }
            if (c == '\'')
            {
                sb.Append(c);
                while (++i < code.Length && code[i] != '\'') { sb.Append(code[i]); if (code[i] == '\\') sb.Append(code[++i]); }
                sb.Append('\'');
                continue;
            }
            if (top == 'h' && c == '{') { braces.Push(braces.Pop() + 1); sb.Append(c); continue; }
            if (top == 'h' && c == '}')
            {
                var open2 = braces.Pop();
                if (open2 == 0) ctx.Pop(); else braces.Push(open2 - 1);
                sb.Append(c);
                continue;
            }
            if (c == '(') { depth++; if (depth == 1) continue; }
            else if (c == ')') { depth--; if (depth == 0) { args.Add(sb.ToString()); return args; } }
            else if (c == ',' && depth == 1 && top == 'c') { args.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(c);
        }
        throw new InvalidOperationException("نِداءٌ بِلا قَوسِ إغلاق — المُحَلِّلُ أَعمى.");
    }

    /// <summary><b>والمُحَلِّلُ مَقيس</b> بِعَيِّناتٍ مَصنوعَة: فاصِلَةٌ داخِلَ نَصٍّ عادِيّ، وداخِلَ ثُقبِ إقحامٍ بِنُصوصٍ مُتَداخِلَة،
    /// و«""» في نَصٍّ حَرفِيّ، ومِحرَفُ فاصِلَة، وقَوسٌ داخِلَ نَصّ، ونِداءٌ مُتَداخِل — لا يُقسَمُ شَيءٌ مِنها.</summary>
    [Theory]
    [InlineData("X(a, b, \"p,q\", d);", "\"p,q\"")]
    [InlineData("X(a, b, $\"r_{(t ? \"c,p\" : \"i\")}\", d);", "$\"r_{(t ? \"c,p\" : \"i\")}\"")]
    [InlineData("X(a, b, @\"v\"\",w\", d);", "@\"v\"\",w\"")]
    [InlineData("X(a, b, ',', d);", "','")]
    [InlineData("X(a, b, \")(\", d);", "\")(\"")]
    [InlineData("X(a, b, F(g, h), d);", "F(g, h)")]
    [InlineData("X(a, b, $\"{{k}}={v,5}\", d);", "$\"{{k}}={v,5}\"")]
    public void The_argument_parser_splits_top_level_commas_only(string call, string third)
    {
        var args = Arguments(call, 1);
        Assert.Equal(4, args.Count);
        Assert.Equal(third, args[2].Trim());
        Assert.Equal("d", args[3].Trim());
    }

    /// <summary>كُلُّ مَوضِعِ إلحاقٍ في الكودِ الحَيِّ مَفتاحُه في الجَردِ أَعلاه، وكُلُّ ما في الجَردِ مَكتوبٌ مِن مَوضِعٍ واحِدٍ عَلى
    /// الأَقَلّ — فَمَرجِعٌ جَديدٌ يُلحَقُ يَحمَرُّ هُنا حَتّى يُصَنَّف. والأَداةُ مَقيسَة: تَطبَعُ عَدَدَ ما وَجَدَت وتَفشَلُ إن كانَ صِفراً.</summary>
    [Fact]
    public void Every_attach_call_site_writes_a_key_from_the_measured_inventory_and_every_inventoried_key_has_a_writer()
    {
        var sites = AttachCallSites();
        foreach (var (file, arg) in sites) output.WriteLine($"{file}: {arg}");
        output.WriteLine($"مَواضِعُ الإلحاقِ: {sites.Count} · تَعابيرُ مُختَلِفَة: {sites.Select(s => s.Arg).Distinct().Count()} · مَفاتيح: {WrittenKeys.Values.SelectMany(k => k).Distinct().Count()}");

        Assert.Equal(13, sites.Count);
        foreach (var (file, arg) in sites)
            Assert.True(WrittenKeys.ContainsKey(arg), $"مَرجِعٌ يُلحَقُ بِلا تَصنيف في {file}: {arg}");
        foreach (var expr in WrittenKeys.Keys)
            Assert.Contains(sites, s => s.Arg == expr);
        Assert.Equal(14, WrittenKeys.Values.SelectMany(k => k).Distinct(StringComparer.Ordinal).Count());

        // «key» حَلقَةٌ عَلى الرابِطَينِ وَحدَهُما — بِحَرفِها.
        var shipping = WriteEndpointGuardTests.StripComments(Source(Template, "Services", "Deals", "DealShipping.cs"));
        Assert.Contains("foreach (var (key, url) in new[] { (LabelRef, state.LabelUrl), (TrackingRef, state.TrackingUrl) })", shipping, StringComparison.Ordinal);
    }

    // ═══ ٣) «اليَوم»: الصَفحَةُ تَرسُمُ السَطرَ خاماً لِلطَرَفَين — يُقلَبُ بِاسمِه في كوميتِ الإصلاح ═══════════════

    /// <summary><b>اليَوم</b>: سَطرُ <c>note</c> يُرسَمُ بِنَصِّه — «attach oto_order_id=…» — لا بِتَسمِيَةٍ مِن القامُوس.</summary>
    [Fact]
    public void Today_the_member_page_renders_an_attach_line_raw()
    {
        var code = PageCode();
        Assert.Contains("\"note\" => e.Note ?? L[\"deals.detail.event_note\"],", code, StringComparison.Ordinal);
    }

    /// <summary><b>اليَوم</b>: حَلقَةُ السِجِلِّ لا تُرَشِّحُ سَطراً — كُلُّ ما في <c>Timeline</c> يُرسَمُ لِلطَرَفَين.</summary>
    [Fact]
    public void Today_the_member_page_draws_every_timeline_line()
    {
        var code = PageCode();
        Assert.Single(Regex.Matches(code, @"deal\.Timeline\b"));
        Assert.Contains("@foreach (var e in deal.Timeline.OrderByDescending(x => x.At))", code, StringComparison.Ordinal);
    }
}
