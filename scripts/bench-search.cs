#!/usr/bin/env dotnet
// إدارَةُ الحُزَمِ المَركَزِيَّة تُعَطَّل لِهذا المِلَفِّ وَحدَه: مِلَفٌّ مُفرَدٌ
// بِلا `.csproj` لا يُشارِك `Directory.Packages.props`، وإضافَةُ سَطرٍ هُناكَ
// لِأَداةِ قِياسٍ تُشَغَّل مَرَّةً = حُزمَةٌ في الحَلِّ بِلا مُستَهلِك.
#:property ManagePackageVersionsCentrally=false
#:package Npgsql@9.0.5
// ═══════════════════════════════════════════════════════════════════════
//  قِياسُ دَينِ البَحث — أَينَ يَنهار `LIKE '%…%'` على `jsonb` بِلا فَهرَس
// ───────────────────────────────────────────────────────────────────────
//  **لِماذا هذا المِلَفّ**: ‏`StorefrontQueries.ExploreAsync` تُتَرجِم
//  `x.Title.Contains(s) || x.Description.Contains(s)` إلى
//  `data ->> 'Title' LIKE '%…%'` — **مَسحٌ تَتابُعِيٌّ كامِل**، ولا
//  فَهرَسَ على الحَقلَين (الفَهرَسُ الوَحيدُ على الجَدوَل هُوَ
//  `(tenant_id, id)`). و«يَنهار على عَشَرَةِ آلاف» **دَعوى** ما لَم
//  تُقَس. هذا المِلَفُّ يَقيسُها.
//
//  **وما يَقيسُه بِالضَبط**، عِندَ ‏1k و10k و100k صَفّ:
//    ١) الحالُ اليَوم — `LIKE '%…%'` بِلا فَهرَس.
//    ٢) العِلاجُ الأَوَّل — ‏`pg_trgm` + فَهرَسُ GIN: زَمَنُ البِناء،
//       وحَجمُ الفَهرَس، وزَمَنُ الاستِعلام.
//    ٣) العِلاجُ الثاني — ‏`to_tsvector` + فَهرَسُ GIN: نَفسُ الثَلاثَة،
//       **وحَدُّه العَرَبيُّ يُقاسُ لا يُفتَرَض** (والقامُوسُ العَرَبيُّ
//       **مَوجودٌ فِعلاً** — وذلك خِلافُ ما يُظَنّ).
//
//  **ولا يَمَسُّ بَياناتِ الإنتاج**: كُلُّ شَيءٍ في مُخَطَّطٍ مُنفَصِلٍ
//  اسمُه `bench_search` يُنشَأُ ويُحذَف. والحَذفُ يَقَع في `finally`
//  فَلا يَبقى أَثَرٌ حَتّى عِندَ الفَشَل. **ويُطبَع ما بَقِيَ بَعدَ
//  الحَذفِ لِيُقرَأ** — «نَظَّفتُ» بِلا قِياسٍ دَعوى (القاعِدَة ١٠).
//
//  الاستِعمال:
//     export ConnectionStrings__Postgres="…"
//     dotnet run scripts/bench-search.cs
//     dotnet run scripts/bench-search.cs -- --max 10000   # سَقفٌ أَصغَر
// ═══════════════════════════════════════════════════════════════════════

using System.Diagnostics;
using Npgsql;

const string Schema = "bench_search";

var sizes = new[] { 1_000, 10_000, 100_000 };
var maxArg = Array.IndexOf(args, "--max");
if (maxArg >= 0 && maxArg + 1 < args.Length && int.TryParse(args[maxArg + 1], out var max))
    sizes = sizes.Where(s => s <= max).ToArray();

var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
if (string.IsNullOrWhiteSpace(cs))
{
    Console.Error.WriteLine("ConnectionStrings__Postgres غَير مَضبوط.");
    return 2;
}

await using var ds = new NpgsqlDataSourceBuilder(cs).Build();
await using var conn = await ds.OpenConnectionAsync();

async Task Exec(string sql, int timeoutSeconds = 600)
{
    await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = timeoutSeconds };
    await cmd.ExecuteNonQueryAsync();
}

async Task<T?> Scalar<T>(string sql)
{
    await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 600 };
    var v = await cmd.ExecuteScalarAsync();
    return v is null or DBNull ? default : (T)Convert.ChangeType(v, typeof(T))!;
}

/// <summary>
/// <para>يُرجِع <b>زَمَنَ التَنفيذِ على الخادِم</b> (وَسيطَ خَمسِ
/// قِراءاتٍ مِن <c>EXPLAIN ANALYZE</c>)، <b>وزَمَنَ الذَهابِ والإيابِ
/// عِندَ العَميل</b> إلى جانِبِه.</para>
///
/// <para><b>والفَرقُ بَينَهُما هُوَ سَبَبُ وُجودِ هذِه الدالَّة</b>:
/// أَوَّلُ نُسخَةٍ مِنها قاسَت السّاعَةَ عِندَ العَميلِ وَحدَها، فَأَعطَت
/// ‏«‏152 ms» عِندَ أَلفِ صَفٍّ **و«‏152 ms» بَعدَ بِناءِ الفَهرَس** —
/// أَي أَنّ الأَداةَ كانَت تَقيسُ الشَبَكَةَ إلى Neon (‏us-east-1)
/// لا الاستِعلام، وكانَت سَتَقول «الفَهرَسُ لا يُغَيِّر شَيئاً» وهُوَ
/// كَذِبٌ تامّ: التَنفيذُ الفِعليُّ كانَ ‏0.86 ms. <b>الأَداةُ تُقاسُ
/// قَبلَ أَن يُوثَقَ بِها</b> (القاعِدَة ١٠) — وهذِه كَذَبَت مَرَّةً
/// فَصُحِّحَت.</para>
/// </summary>
async Task<(double serverMs, double clientMs, string plan)> Timed(string sql, int runs = 5)
{
    var client = new List<double>();
    var server = new List<double>();
    string plan = "";

    for (var i = 0; i < runs; i++)
    {
        var sw = Stopwatch.StartNew();
        await using (var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 600 })
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) { }
        sw.Stop();
        client.Add(sw.Elapsed.TotalMilliseconds);

        await using (var cmd = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT) " + sql, conn)
                     { CommandTimeout = 600 })
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            var lines = new List<string>();
            while (await r.ReadAsync()) lines.Add(r.GetString(0));
            plan = string.Join("\n", lines);
        }
        server.Add(ExecutionMs(plan));
    }

    client.Sort();
    server.Sort();
    return (server[server.Count / 2], client[client.Count / 2], plan);
}

/// <summary>«‏Execution Time: 12.345 ms» مِن ذَيلِ الخُطَّة. و<c>-1</c>
/// إن لَم تُوجَد — فَتُقرَأُ عَلامَةَ عَمىً لا صِفراً يُساءُ فَهمُه.</summary>
static double ExecutionMs(string plan)
{
    foreach (var line in plan.Split('\n'))
    {
        var t = line.Trim();
        if (!t.StartsWith("Execution Time:", StringComparison.Ordinal)) continue;
        var v = t["Execution Time:".Length..].Replace("ms", "", StringComparison.Ordinal).Trim();
        return double.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out var ms) ? ms : -1;
    }
    return -1;
}

static string ScanKind(string plan) =>
    plan.Contains("Bitmap Index Scan", StringComparison.Ordinal) ? "Bitmap Index Scan"
    : plan.Contains("Index Scan", StringComparison.Ordinal) ? "Index Scan"
    : plan.Contains("Parallel Seq Scan", StringComparison.Ordinal) ? "Parallel Seq Scan"
    : plan.Contains("Seq Scan", StringComparison.Ordinal) ? "Seq Scan"
    : "?";

// نَفسُ شَكلِ ما تُصدِرُه Marten لِـ
// `Where(x => !x.IsDeleted && !x.IsHiddenByModerator && (Title.Contains(s) || Description.Contains(s)))`
// في جَلسَةِ مُستَأجِر: تَقييدُ الإيجارِ عَمود، والباقي `->>` على jsonb.
// ─── كَلِمَتانِ لا واحِدَة، والفَرقُ بَينَهُما هُوَ القِياسُ نَفسُه ───
//
// **أَوَّلُ نُسخَةٍ مِن هذا المِلَفِّ قاسَت كَلِمَةً واحِدَةً — وكانَت في
// كُلِّ صَفّ.** فَأَعطَت «`pg_trgm` لا يُغَيِّر شَيئاً: ‏88 ms قَبلَ
// و89 ms بَعد». وهذا **لَيسَ حُكماً على `pg_trgm`** بَل على بَياناتٍ
// انتِقائِيَّتُها ‏100%: مُخَطِّطُ Postgres يَتَجاوَزُ أَيَّ فَهرَسٍ
// يُرجِعُ كُلَّ الصُفوف، وهُوَ مُحِقّ. **الأَداةُ كانَت تَقيسُ بَياناتِها
// لا العِلاج** (القاعِدَة ١٠).
//
//   • `Common` — في **كُلِّ** صَفّ. وهذا لَيسَ حالَةً نادِرَة: «شَقّة»
//     في مَتجَرِ عَقارٍ يُصيبُ كُلَّ إعلانٍ تَقريباً. وهي **الحالَةُ
//     الأَسوَأ**، ولا فَهرَسَ يُنقِذُ مِنها — يُنقِذُ مِنها **تَرقيمُ
//     الصَفَحات**.
//   • `Rare` — في ‏**1%** مِن الصُفوف. وهي حالَةُ البَحثِ الحَقيقِيَّة:
//     زائِرٌ يَكتُب كَلِمَةً مُمَيِّزَة. وهُنا يُقاسُ الفَهرَسُ فِعلاً.
const string Common = "شَقّة";
const string Rare   = "مِرقاب";

string LikeQuery(string needle) => $"""
    select d.id, d.data
    from {Schema}.mt_doc_listing as d
    where d.tenant_id = 'ejar'
      and CAST(d.data ->> 'IsDeleted' as boolean) = false
      and (d.data ->> 'Title' like '%{needle}%'
           or d.data ->> 'Description' like '%{needle}%')
    order by d.data ->> 'CreatedAt' desc
    limit 200
    """;

string FtsQuery(string cfg, string needle) => $"""
    select d.id, d.data
    from {Schema}.mt_doc_listing as d
    where d.tenant_id = 'ejar'
      and CAST(d.data ->> 'IsDeleted' as boolean) = false
      and to_tsvector('{cfg}',
            coalesce(d.data ->> 'Title','') || ' ' || coalesce(d.data ->> 'Description',''))
          @@ plainto_tsquery('{cfg}', '{needle}')
    order by d.data ->> 'CreatedAt' desc
    limit 200
    """;

var report = new List<string>();
void Say(string line) { Console.WriteLine(line); report.Add(line); }

var cleaned = false;
try
{
    Say("═══ تَجهيز ═══");
    await Exec($"drop schema if exists {Schema} cascade");
    await Exec($"create schema {Schema}");
    await Exec($"create table {Schema}.mt_doc_listing (like platform.mt_doc_listing including all)");
    Say($"  · مُخَطَّطٌ مُنفَصِل `{Schema}` — نُسخَةُ بِنيَةٍ مِن "
        + "`platform.mt_doc_listing` بِفَهارِسِها (المِفتاحُ الأَوَّليُّ وَحدَه).");

    var seeded = 0;
    foreach (var target in sizes)
    {
        var toAdd = target - seeded;
        // بَياناتٌ عَرَبِيَّةٌ مُوَلَّدَةٌ **داخِلَ القاعِدَة** — لا نَقلَ
        // شَبَكَةٍ لِمِئَةِ أَلفِ صَفّ. وثَلاثَةُ مُستَأجِرينَ لِيَكونَ
        // تَقييدُ الإيجارِ حَقيقِيّاً لا صورِيّاً.
        await Exec($"""
            insert into {Schema}.mt_doc_listing (tenant_id, id, data, mt_last_modified, mt_dotnet_type, mt_version)
            select
              (array['ejar','ejar','ejar','souq','naqel'])[1 + (i % 5)],
              gen_random_uuid(),
              jsonb_build_object(
                'Id', gen_random_uuid(),
                'Title', (array['شَقّة لِلإيجار','سَيّارَة مُستَعمَلَة','مَكتَب تِجاريّ','أَثاث مَنزِليّ','خِدمَة نَقل'])[1 + (i % 5)] || ' رَقم ' || i,
                'Description', 'وَصفٌ تَفصيليٌّ لِلإعلانِ رَقم ' || i || ' يَحوي كَلِماتٍ عَرَبِيَّةً كَثيرَةً لِتَقريبِ الطولِ الواقِعيِّ لِلنَصِّ المَكتوبِ في الإعلانات، ومِنها شَقّة ومَكتَب وسَيّارَة وأَثاث.'
                  || (case when i % 100 = 0 then ' وفيه إطلالَةٌ على مِرقاب البَحر.' else '' end),
                'CategorySlug', (array['apartments','cars','offices','furniture','moving'])[1 + (i % 5)],
                'City', (array['الرياض','جدة','الدمام','مكة','المدينة'])[1 + (i % 5)],
                'District', 'حَيّ ' || (i % 40),
                'Price', 1000 + (i % 90000),
                'IsDeleted', false,
                'IsHiddenByModerator', false,
                'CreatedAt', to_char(now() - (i || ' minutes')::interval, 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
                'Attributes', jsonb_build_object('owner_id', gen_random_uuid()::text)
              ),
              now(), 'ACommerce.Kit.Listings.Listing', 1
            from generate_series({seeded + 1}, {target}) as i
            """);
        seeded = target;
        await Exec($"analyze {Schema}.mt_doc_listing");

        var rows = await Scalar<long>($"select count(*) from {Schema}.mt_doc_listing");
        var tableSize = await Scalar<string>(
            $"select pg_size_pretty(pg_total_relation_size('{Schema}.mt_doc_listing'))");
        Say("");
        Say($"═══ {rows:N0} صَفّ · حَجمُ الجَدوَل {tableSize} · مُضافٌ الآن {toAdd:N0} ═══");

        foreach (var (label, needle) in new[] { ("شائِعَة (كُلُّ صَفّ)", Common), ("نادِرَة (‏1%)", Rare) })
        {
            var (srv, cli, plan) = await Timed(LikeQuery(needle));
            Say($"  ‏[١] LIKE بِلا فَهرَس · كَلِمَةٌ {label,-20} : خادِم {srv,8:F2} ms · عَميل {cli,7:F0} ms · {ScanKind(plan)}");
        }
    }

    // ─── العِلاجُ الأَوَّل: pg_trgm ───────────────────────────────────
    Say("");
    Say("═══ العِلاجُ الأَوَّل — pg_trgm + GIN (عِندَ أَكبَرِ حَجم) ═══");
    await Exec("create extension if not exists pg_trgm");
    var swT = Stopwatch.StartNew();
    await Exec($"""
        create index bench_trgm on {Schema}.mt_doc_listing
        using gin ((d_title(data)) gin_trgm_ops)
        """.Replace("d_title(data)", "(data ->> 'Title')"));
    await Exec($"""
        create index bench_trgm_desc on {Schema}.mt_doc_listing
        using gin ((data ->> 'Description') gin_trgm_ops)
        """);
    swT.Stop();
    await Exec($"analyze {Schema}.mt_doc_listing");
    var trgmSize = await Scalar<string>(
        $"select pg_size_pretty(pg_relation_size('{Schema}.bench_trgm') + pg_relation_size('{Schema}.bench_trgm_desc'))");
    Say($"  بِناءُ الفَهرَسَين : {swT.Elapsed.TotalSeconds,8:F1} s · حَجمُهُما {trgmSize}");
    foreach (var (label, needle) in new[] { ("شائِعَة (كُلُّ صَفّ)", Common), ("نادِرَة (‏1%)", Rare) })
    {
        var (srv, cli, plan) = await Timed(LikeQuery(needle));
        Say($"  ‏[٢] نَفسُ الاستِعلامِ حَرفاً · كَلِمَةٌ {label,-20} : خادِم {srv,8:F2} ms · عَميل {cli,7:F0} ms · {ScanKind(plan)}");
    }

    // ─── العِلاجُ الثاني: to_tsvector ─────────────────────────────────
    Say("");
    Say("═══ العِلاجُ الثاني — to_tsvector + GIN ═══");
    var dicts = new List<string>();
    await using (var cmd = new NpgsqlCommand(
                     "select cfgname from pg_ts_config order by 1", conn))
    await using (var r = await cmd.ExecuteReaderAsync())
        while (await r.ReadAsync()) dicts.Add(r.GetString(0));
    Say($"  قَواميسُ النَصِّ المُتاحَة ({dicts.Count}): {string.Join("، ", dicts)}");
    Say($"  · قاموسٌ عَرَبيّ؟ {(dicts.Contains("arabic") ? "نَعَم" : "**لا**")}");

    // القامُوسُ العَرَبيُّ لا `simple`: القِياسُ أَدناه يُبَيِّنُ لِماذا.
    var swF = Stopwatch.StartNew();
    await Exec($"""
        create index bench_fts on {Schema}.mt_doc_listing using gin (
          to_tsvector('arabic',
            coalesce(data ->> 'Title','') || ' ' || coalesce(data ->> 'Description',''))
        )
        """);
    swF.Stop();
    await Exec($"analyze {Schema}.mt_doc_listing");
    var ftsSize = await Scalar<string>($"select pg_size_pretty(pg_relation_size('{Schema}.bench_fts'))");
    Say($"  بِناءُ الفَهرَس (`arabic`) : {swF.Elapsed.TotalSeconds,8:F1} s · حَجمُه {ftsSize}");
    foreach (var (label, needle) in new[] { ("شائِعَة (كُلُّ صَفّ)", Common), ("نادِرَة (‏1%)", Rare) })
    {
        var (srv, cli, plan) = await Timed(FtsQuery("arabic", needle));
        Say($"  ‏[٣] ‏@@ لا LIKE · كَلِمَةٌ {label,-20} : خادِم {srv,8:F2} ms · عَميل {cli,7:F0} ms · {ScanKind(plan)}");
    }

    // ─── والحُدودُ العَرَبِيَّةُ تُقاسُ لا تُفتَرَض ────────────────────
    // البَياناتُ كُلُّها تَحوي «شَقّة» **بِتَشكيل**. فَالسُؤالُ: أَيُّ
    // قامُوسٍ وأَيُّ صيغَةِ بَحثٍ تُصيب؟
    async Task<long> Hits(string cfg, string term) => await Scalar<long>($"""
        select count(*) from {Schema}.mt_doc_listing d
        where to_tsvector('{cfg}',
                coalesce(d.data ->> 'Title','') || ' ' || coalesce(d.data ->> 'Description',''))
              @@ plainto_tsquery('{cfg}', '{term}')
        """);

    Say("");
    Say("  ─── المُطابَقَةُ العَرَبِيَّة: كُلُّ الصُفوفِ تَحوي «شَقّة» بِتَشكيل ───");
    foreach (var cfg in new[] { "simple", "arabic" })
    {
        var withTashkeel = await Hits(cfg, "شَقّة");
        var without = await Hits(cfg, "شقة");
        var plural = await Hits(cfg, "شُقَق");
        Say($"  · قامُوس `{cfg,-7}` : «شَقّة»={withTashkeel,7:N0} · «شقة»={without,7:N0} · «شُقَق»={plural,7:N0}");
    }
    var unaccentAvailable = await Scalar<long>(
        "select count(*) from pg_available_extensions where name='unaccent'");
    Say($"  · امتِدادُ `unaccent` مُتاحٌ لِلتَثبيت؟ {(unaccentAvailable > 0 ? "نَعَم" : "لا")}");
    Say("    ← **الحَدُّ**: البَحثُ يُطابِقُ المِحرَفَ لا الجِذر. فَمَن كَتَبَ");
    Say("      «شقة» بِلا تَشكيلٍ لا يَجِد «شَقّة»، وهذا ما يَفعَلُه كُلُّ زائِرٍ.");
}
finally
{
    // النَظافَةُ في `finally` — والحَذفُ يُقاسُ بَعدَه لا يُدَّعى.
    try
    {
        await Exec($"drop schema if exists {Schema} cascade");
        await Exec("drop extension if exists pg_trgm");
        cleaned = true;
    }
    catch (Exception ex) { Console.Error.WriteLine("فَشَلَ التَنظيف: " + ex.Message); }
}

Say("");
Say("═══ التَنظيف ═══");
var left = await Scalar<long>(
    $"select count(*) from information_schema.schemata where schema_name = '{Schema}'");
var ext = await Scalar<long>("select count(*) from pg_extension where extname = 'pg_trgm'");
var dbSize = await Scalar<string>("select pg_size_pretty(pg_database_size(current_database()))");
Say($"  · مُخَطَّطُ `{Schema}` باقٍ؟ {left} (المَطلوب ‏0)");
Say($"  · امتِدادُ `pg_trgm` باقٍ؟ {ext} (المَطلوب ‏0 — لَم يَكُن مُثَبَّتاً قَبلَ القِياس)");
Say($"  · حَجمُ القاعِدَةِ بَعدَ التَنظيف: {dbSize}");

return (cleaned && left == 0 && ext == 0) ? 0 : 1;
