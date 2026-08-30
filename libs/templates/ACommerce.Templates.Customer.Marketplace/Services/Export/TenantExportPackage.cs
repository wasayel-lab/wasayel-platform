using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace ACommerce.Templates.Customer.Marketplace.Services.Export;

/// <summary>خَرقٌ في حَقيبَةِ التَخارُج — <b>يَرمي ولا يَتَخَطّى</b>.
/// حَقيبَةٌ نَقَصَ مِنها صَفٌّ بِصَمتٍ تَكذِبُ مَرَّتَين: تُسَلَّمُ
/// ناقِصَةً، وتَبدو كامِلَة.</summary>
public sealed class TenantExportViolationException(string message) : Exception(message);

/// <summary>جَدوَلُ صِنفٍ واحِد — صُفوفُه مُعَتَّمَةٌ سَلَفاً.</summary>
/// <param name="ReadErrorAr">سَبَبُ تَعَذُّرِ القِراءَة، إن تَعَذَّرَت.
/// <b>ولا يُبتَلَع</b>: يُكتَبُ في الفَهرَسَين وفي <c>README</c>،
/// لِأَنّ جَدوَلاً فارِغاً لِعَطَبٍ يُقرَأُ «لا بَياناتِ لَك».</param>
public sealed record ExportTable(
    string TypeName, string Entry, IReadOnlyList<JsonObject> Rows, string? ReadErrorAr = null);

/// <summary>كائِنٌ مِن مَخزَنِ المِلَفّاتِ بِمِفتاحِه.</summary>
public sealed record ExportFile(string Key, byte[] Content);

/// <summary>مُحتَوى الحَقيبَةِ كامِلاً — مَجموعٌ سَلَفاً، مُعَتَّمٌ
/// سَلَفاً، ولَم يُكتَب بَعد.</summary>
public sealed record ExportContent(
    string TenantSlug,
    string TenantName,
    Guid OwnerUserId,
    DateTime GeneratedAtUtc,
    IReadOnlyList<ExportTable> Tables,
    IReadOnlyList<ExportFile> Files,
    IReadOnlyList<string> MissingFileKeys,
    IReadOnlyList<string> NotesAr);

/// <summary>
/// <para><b>الكاتِبُ هُوَ الحارِس.</b> كُلُّ فَحصٍ هُنا يَعمَلُ في
/// الإنتاجِ لا في حَقيبَةِ الاختِبارِ وَحدَها — وحارِسٌ يَعيشُ في
/// الاختِبارِ فَقَط يَحرُسُ الاختِبار.</para>
///
/// <para>وثَلاثَةُ أَسئِلَةٍ مُتَساوِيَةُ الوَزن:</para>
/// <list type="number">
///   <item><b>أَيَخرُجُ صَفٌّ لِمُستَأجِرٍ آخَر؟</b> — كُلُّ صَفٍّ
///   يَحمِلُ سلاجاً يُقارَنُ بِسلاجِ الحَقيبَة، ووَثيقَةُ المُستَأجِرِ
///   يُقارَنُ مُعَرِّفُها، وكُلُّ مِفتاحِ مِلَفٍّ يَجِبُ أَن يَبدَأَ
///   بِبادِئَةِ المُستَأجِر.</item>
///   <item><b>أَيَخرُجُ بَندٌ مِن قائِمَةِ الاستِثناء؟</b> — جَدوَلٌ
///   لِنَوعٍ لَيسَ في القائِمَةِ البَيضاء يُرفَض، ومُؤَشِّرُ اعتِمادٍ
///   في أَيِّ عُمقٍ يُرفَض.</item>
///   <item><b>أَيَنقُصُ صِنفٌ مِن أَصنافِ المُستَأجِر؟</b> — كُلُّ
///   إدخالَةٍ في <see cref="TenantExportLedger.Exported"/> يَجِبُ أَن
///   يَكونَ لَها جَدوَلٌ، ولَو فارِغاً.</item>
/// </list>
///
/// <para><b>وحَدُّ الحَجمِ مُعلَنٌ لا مَظنون</b>: الحَقيبَةُ تُبنى في
/// الذاكِرَةِ لِأَنّ الفَهرَسَ الآلِيَّ يَحمِلُ <c>sha256</c> كُلِّ
/// مَدخَل، ولِأَنّ القاعِدَةَ كُلَّها ‏1.74 MB يَومَ الكِتابَة
/// (‏2026-08-30) وأَكبَرَ مُستَأجِرٍ فيها ‏13 kB. <b>وعِندَ تَجاوُزِ
/// حَقيبَةِ مُستَأجِرٍ واحِدٍ مِئَةَ مِيغابايت</b> يُنقَلُ التَوليدُ
/// إلى مَخزَنِ الكائِناتِ ورابِطٍ مُنتَهي الصَلاحِيَّة — الشَرطُ
/// مَكتوبٌ الآنَ ولا يُبنى الآن.</para>
/// </summary>
public static class TenantExportPackageWriter
{
    public const int FormatVersion = 1;

    private static readonly Regex SlugShape = new("^[a-z0-9][a-z0-9-]*$", RegexOptions.Compiled);

    public static void Write(Stream destination, ExportContent content)
    {
        var slug = content.TenantSlug;

        // ═══ ٠) السلاجُ نَفسُه ═══════════════════════════════════════
        if (string.IsNullOrWhiteSpace(slug) || !SlugShape.IsMatch(slug))
            throw new TenantExportViolationException(
                $"سلاجٌ لا يَصلُحُ لِحَقيبَة: «{slug}». والأَقسامُ المَحجوزَةُ " +
                "(‏`_platform`, `_studio`, `_admin`) لا تُصَدَّرُ أَبَداً.");

        // ═══ ١) الاكتِمال — لا صِنفَ يَنقُص ═════════════════════════
        var byType = new Dictionary<string, ExportTable>(StringComparer.Ordinal);
        foreach (var t in content.Tables)
        {
            if (!TenantExportLedger.IsExported(t.TypeName))
                throw new TenantExportViolationException(
                    $"جَدوَلٌ لِنَوعٍ خارِجَ القائِمَةِ البَيضاء: «{t.TypeName}». " +
                    "القائِمَةُ بَيضاءُ لا سَوداء — ما لَم يُصَنَّف لا يَخرُج.");

            if (!byType.TryAdd(t.TypeName, t))
                throw new TenantExportViolationException($"جَدوَلٌ مُكَرَّرٌ: «{t.TypeName}».");
        }

        var missingClasses = TenantExportLedger.Exported
            .Where(e => !byType.ContainsKey(e.TypeName))
            .Select(e => e.TypeName)
            .ToArray();

        if (missingClasses.Length > 0)
            throw new TenantExportViolationException(
                "صِنفٌ مِن أَصنافِ بَياناتِ المُستَأجِرِ ناقِصٌ مِن الحَقيبَة — " +
                "وتَخارُجٌ مَنقوصٌ أَسوَأُ مِن لا تَخارُج لِأَنَّه يُطَمئِنُ كَذِباً:\n  " +
                string.Join("\n  ", missingClasses));

        // ═══ ٢) التَسريبُ والاستِثناء — صَفّاً صَفّاً ═══════════════
        foreach (var t in content.Tables)
        {
            var entry = TenantExportLedger.Find(t.TypeName)!;
            foreach (var row in t.Rows)
            {
                AssertBelongsToTenant(entry, row, slug);
                AssertCarriesNoCredential(entry, row);
            }
        }

        // ═══ ٣) المِلَفّات — بادِئَةُ المُستَأجِرِ وَحدَها ═══════════
        var prefix = $"tenants/{slug}/";
        foreach (var f in content.Files)
            if (!f.Key.StartsWith(prefix, StringComparison.Ordinal))
                throw new TenantExportViolationException(
                    $"كائِنٌ خارِجَ بادِئَةِ المُستَأجِر: «{f.Key}» — والمُتَوَقَّعُ «{prefix}…». " +
                    "الدَلوُ واحِدٌ لِكُلِّ المُستَأجِرين، والعَزلُ فيه بِالبادِئَةِ وَحدَها.");

        // ═══ ٤) الكِتابَة ═══════════════════════════════════════════
        var manifestTables = new JsonArray();
        var manifestFiles = new JsonArray();

        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        // ‏README أَوَّلُ مَدخَلٍ عَمداً: أَوَّلُ ما يَفتَحُه المُستَلِم.
        WriteText(zip, "README.md", BuildReadme(content));

        foreach (var e in TenantExportLedger.Exported)
        {
            var table = byType[e.TypeName];
            var json = new JsonArray(table.Rows.Select(r => (JsonNode?)r.DeepClone()).ToArray());
            var jsonBytes = WriteText(zip, e.JsonPath, json.ToJsonString(TenantExportRedaction.Json));
            var csvBytes = WriteText(zip, e.CsvPath, BuildCsv(table.Rows), bom: true);

            manifestTables.Add(new JsonObject
            {
                ["type"] = e.TypeName,
                ["entry"] = e.Entry,
                ["rows"] = table.Rows.Count,
                ["json"] = e.JsonPath,
                ["csv"] = e.CsvPath,
                ["jsonSha256"] = Sha256(jsonBytes),
                ["csvSha256"] = Sha256(csvBytes),
                ["readError"] = table.ReadErrorAr,
            });
        }

        foreach (var f in content.Files)
        {
            var fileEntry = zip.CreateEntry("files/" + f.Key, CompressionLevel.Optimal);
            using (var s = fileEntry.Open()) s.Write(f.Content, 0, f.Content.Length);
            manifestFiles.Add(new JsonObject
            {
                ["key"] = f.Key,
                ["bytes"] = f.Content.Length,
                ["sha256"] = Sha256(f.Content),
            });
        }

        // مِفتاحٌ مَذكورٌ في وَثيقَةٍ ولا كائِنَ لَه — يُقالُ ولا يُبتَلَع.
        if (content.MissingFileKeys.Count > 0)
            WriteText(zip, "files/MISSING.txt",
                "مَفاتيحُ مَذكورَةٌ في الوَثائِقِ ولا كائِنَ لَها في المَخزَن.\n" +
                "سَبَبُها المَعروف: مِلَفّاتٌ رُفِعَت على قُرصِ حاوِيَةٍ زائِلٍ قَبلَ " +
                "‏ADR-017، فَذَهَبَت وبَقِيَ رابِطُها في القاعِدَة.\n\n" +
                string.Join("\n", content.MissingFileKeys));

        WriteBytes(zip, "index.xlsx", BuildIndexWorkbook(content, byType));

        // الفَهرَسُ الآلِيُّ آخِرُ مَدخَلٍ: يَحمِلُ بَصمَةَ كُلِّ ما قَبلَه.
        var manifest = new JsonObject
        {
            ["formatVersion"] = FormatVersion,
            ["tenantSlug"] = slug,
            ["tenantName"] = content.TenantName,
            ["ownerUserId"] = content.OwnerUserId.ToString(),
            ["generatedAtUtc"] = content.GeneratedAtUtc.ToString("O"),
            ["tables"] = manifestTables,
            ["files"] = manifestFiles,
            ["missingFiles"] = new JsonArray(
                content.MissingFileKeys.Select(k => (JsonNode?)k).ToArray()),
            ["excluded"] = new JsonArray(TenantExportLedger.Excluded
                .Select(e => (JsonNode?)new JsonObject
                {
                    ["type"] = e.TypeName,
                    ["disposition"] = e.Disposition.ToString(),
                    ["whyAr"] = e.WhyAr,
                }).ToArray()),
            ["withheldRules"] = new JsonArray(TenantExportRedaction.Fields
                .Select(f => (JsonNode?)new JsonObject
                {
                    ["type"] = f.TypeName,
                    ["property"] = f.Property,
                    ["whyAr"] = f.WhyAr,
                }).ToArray()),
            ["notesAr"] = new JsonArray(content.NotesAr.Select(n => (JsonNode?)n).ToArray()),
        };
        WriteText(zip, "manifest.json",
            manifest.ToJsonString(new JsonSerializerOptions(TenantExportRedaction.Json)
            { WriteIndented = true }));
    }

    // ─── الحُرّاس ─────────────────────────────────────────────────

    private static void AssertBelongsToTenant(ExportedType entry, JsonObject row, string slug)
    {
        if (entry.Disposition == ExportDisposition.ExportSelf)
        {
            var id = Value(row, "id");
            if (!string.Equals(id, slug, StringComparison.Ordinal))
                throw new TenantExportViolationException(
                    $"وَثيقَةُ مُستَأجِرٍ آخَرَ في حَقيبَةِ «{slug}»: id=«{id}». " +
                    "جَدوَلُها بِلا `tenant_id`، فَلا شَبَكَةَ أَمانٍ بِنيَوِيَّةً تَحتَه.");
            return;
        }

        if (entry.Disposition == ExportDisposition.ExportOwner) return;  // لا سلاجَ عَلَيها

        var rowSlug = Value(row, "tenantSlug");
        if (!string.IsNullOrEmpty(rowSlug) && !string.Equals(rowSlug, slug, StringComparison.Ordinal))
            throw new TenantExportViolationException(
                $"صَفٌّ لِمُستَأجِرٍ آخَرَ في حَقيبَةِ «{slug}»: " +
                $"{entry.TypeName}.tenantSlug=«{rowSlug}».");
    }

    private static void AssertCarriesNoCredential(ExportedType entry, JsonObject row)
    {
        var hit = FindForbidden(row);
        if (hit is not null)
            throw new TenantExportViolationException(
                $"حَقلُ اعتِمادٍ في صَفٍّ يَخرُج: {entry.TypeName} ← «{hit}». " +
                "يُحذَفُ في `TenantExportRedaction` قَبلَ الكِتابَة، لا يُكتَبُ ثُمَّ يُنَظَّف.");
    }

    private static string? FindForbidden(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (k, v) in o)
                {
                    if (TenantExportRedaction.ForbiddenAnywhere
                            .Contains(k, StringComparer.OrdinalIgnoreCase))
                        return k;
                    var deeper = FindForbidden(v);
                    if (deeper is not null) return deeper;
                }
                return null;

            case JsonArray a:
                foreach (var item in a)
                {
                    var deeper = FindForbidden(item);
                    if (deeper is not null) return deeper;
                }
                return null;

            default:
                return null;
        }
    }

    private static string Value(JsonObject row, string property)
    {
        var key = row.Select(p => p.Key)
            .FirstOrDefault(k => string.Equals(k, property, StringComparison.OrdinalIgnoreCase));
        if (key is null) return "";
        var node = row[key];
        return node is null ? "" : node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>() : node.ToJsonString();
    }

    // ─── التَصيير ─────────────────────────────────────────────────

    private static byte[] WriteText(ZipArchive zip, string path, string text, bool bom = false)
    {
        var bytes = bom
            ? Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray()
            : Encoding.UTF8.GetBytes(text);
        WriteBytes(zip, path, bytes);
        return bytes;
    }

    private static void WriteBytes(ZipArchive zip, string path, byte[] bytes)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(bytes, 0, bytes.Length);
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>‏CSV بِـUTF-8 وBOM — <b>وبِلا BOM يَقرَأُ Excel
    /// العَرَبِيَّةَ رُموزاً</b>، فَتُسَلَّمُ «عَينٌ» لا تُقرَأ.</summary>
    private static string BuildCsv(IReadOnlyList<JsonObject> rows)
    {
        if (rows.Count == 0) return "";

        var columns = new List<string>();
        foreach (var r in rows)
            foreach (var (k, _) in r)
                if (!columns.Contains(k, StringComparer.Ordinal)) columns.Add(k);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", columns.Select(Escape)));
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", columns.Select(c => Escape(Cell(r, c)))));
        return sb.ToString();

        static string Cell(JsonObject row, string column)
        {
            if (!row.TryGetPropertyValue(column, out var node) || node is null) return "";
            return node.GetValueKind() switch
            {
                JsonValueKind.String => node.GetValue<string>(),
                JsonValueKind.Null => "",
                JsonValueKind.Object or JsonValueKind.Array => node.ToJsonString(TenantExportRedaction.Json),
                _ => node.ToJsonString(),
            };
        }

        static string Escape(string v) => "\"" + v.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>الفَهرَسُ البَشَريّ — وَرَقَةٌ واحِدَة يَفتَحُها
    /// المُستَلِمُ بِالنَقر: الصِنفُ، وعَدَدُه، ومِلَفُّه، وتَصنيفُه.</summary>
    private static byte[] BuildIndexWorkbook(
        ExportContent content, Dictionary<string, ExportTable> byType)
    {
        using var wb = new XLWorkbook();
        wb.RightToLeft = true;
        wb.Properties.Title = $"فَهرَسُ بَياناتِ {content.TenantName}";

        var ws = wb.Worksheets.Add("الفَهرَس");
        ws.Style.Font.FontName = "Cairo";

        ws.Cell("B2").Value = $"بَياناتُ {content.TenantName} (/{content.TenantSlug})";
        ws.Cell("B2").Style.Font.FontSize = 18;
        ws.Cell("B2").Style.Font.Bold = true;
        ws.Cell("B4").Value = "وَقتُ التَوليد (UTC):";
        ws.Cell("B4").Style.Font.Bold = true;
        ws.Cell("C4").Value = content.GeneratedAtUtc.ToString("yyyy-MM-dd HH:mm");

        var head = 6;
        var headers = new[] { "الصِنف", "العَدَد", "المِلَفّ", "التَصنيف", "مُلاحَظَة" };
        for (var i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(head, 2 + i);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");
            ws.Column(2 + i).Width = i == 4 ? 60 : 26;
        }

        var row = head + 1;
        foreach (var e in TenantExportLedger.Exported)
        {
            var t = byType[e.TypeName];
            ws.Cell(row, 2).Value = e.TypeName;
            ws.Cell(row, 3).Value = t.Rows.Count;
            ws.Cell(row, 4).Value = e.JsonPath;
            ws.Cell(row, 5).Value = e.Disposition == ExportDisposition.ExportOwner
                ? "بَياناتُ صاحِبِ المَتجَر" : "بَياناتُ المَتجَر";
            ws.Cell(row, 6).Value = t.ReadErrorAr ?? "";
            ws.Cell(row, 6).Style.Alignment.WrapText = true;
            row++;
        }

        row += 1;
        ws.Cell(row, 2).Value = "ما لا يَخرُج — ولِماذا";
        ws.Cell(row, 2).Style.Font.Bold = true;
        ws.Cell(row, 2).Style.Font.FontSize = 14;
        row++;
        foreach (var e in TenantExportLedger.Excluded)
        {
            ws.Cell(row, 2).Value = e.TypeName;
            ws.Cell(row, 3).Value = "—";
            ws.Cell(row, 4).Value = "—";
            ws.Cell(row, 5).Value = e.Disposition switch
            {
                ExportDisposition.ExcludeGlobal => "وَثيقَةٌ عامَّة",
                ExportDisposition.ExcludeSecret => "اعتِماد",
                _ => "آلِيَّةٌ داخِلِيَّة",
            };
            ws.Cell(row, 6).Value = e.WhyAr;
            ws.Cell(row, 6).Style.Alignment.WrapText = true;
            row++;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>الفَهرَسُ البَشَريّ — <b>ومَسؤولِيَّةُ المُستَلِمِ
    /// مَنصوصَةٌ فيه</b>، لا في وَثيقَةٍ بَعيدَةٍ يُحالُ إلَيها.</summary>
    private static string BuildReadme(ExportContent content)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# بَياناتُ «{content.TenantName}» — /{content.TenantSlug}");
        sb.AppendLine();
        sb.AppendLine($"وُلِّدَت في {content.GeneratedAtUtc:yyyy-MM-dd HH:mm} بِتَوقيتِ UTC، " +
                      $"بِنُسخَةِ حَقيبَةٍ رَقم {FormatVersion}.");
        sb.AppendLine();
        sb.AppendLine("## ماذا في الحَقيبَة");
        sb.AppendLine();
        sb.AppendLine("| المُجَلَّد | ما فيه |");
        sb.AppendLine("|---|---|");
        sb.AppendLine("| `data/` | بَياناتُ المَتجَر — مِلَفُّ JSON لِكُلِّ صِنف. |");
        sb.AppendLine("| `tables/` | نَفسُها جَداوِلَ CSV بِـUTF-8 وBOM — تُفتَحُ بِالنَقرِ في Excel. |");
        sb.AppendLine("| `owner/` | بَياناتُ صاحِبِ المَتجَرِ نَفسِه: حِسابُه، ومُوافَقاتُه، ودِراساتُ جَدواه. |");
        sb.AppendLine("| `files/` | الصُوَرُ والمُرفَقاتُ بِمَفاتيحِها كَما في المَخزَن. |");
        sb.AppendLine("| `index.xlsx` | فَهرَسٌ يُفتَحُ بِالنَقر: كُلُّ صِنفٍ وعَدَدُه ومِلَفُّه. |");
        sb.AppendLine("| `manifest.json` | فَهرَسٌ آلِيّ، ومَعَه بَصمَةُ `sha256` لِكُلِّ مِلَفّ. |");
        sb.AppendLine();

        sb.AppendLine("## الأَعداد");
        sb.AppendLine();
        sb.AppendLine("| الصِنف | العَدَد | المِلَفّ |");
        sb.AppendLine("|---|---|---|");
        var byType = content.Tables.ToDictionary(t => t.TypeName, StringComparer.Ordinal);
        foreach (var e in TenantExportLedger.Exported)
        {
            var t = byType[e.TypeName];
            var note = t.ReadErrorAr is null ? "" : $" ⚠ {t.ReadErrorAr}";
            sb.AppendLine($"| {e.TypeName} | {t.Rows.Count} | `{e.JsonPath}`{note} |");
        }
        sb.AppendLine();

        sb.AppendLine("## ما لا يَخرُج — ولِماذا");
        sb.AppendLine();
        sb.AppendLine("لا شَيءَ هُنا يُسقَطُ صامِتاً. لِكُلِّ بَندٍ سَبَبُه:");
        sb.AppendLine();
        foreach (var e in TenantExportLedger.Excluded)
            sb.AppendLine($"- **{e.TypeName}** — {e.WhyAr}");
        sb.AppendLine();
        sb.AppendLine("وحُقولٌ تُحذَفُ مِن أَصنافٍ تَخرُج:");
        sb.AppendLine();
        foreach (var f in TenantExportRedaction.Fields)
            sb.AppendLine($"- **{f.TypeName}.{f.Property}** — {f.WhyAr}");
        sb.AppendLine();
        sb.AppendLine("وصُفوفٌ تُحجَبُ كامِلَةً: قُيودُ التَدقيقِ الَّتي فاعِلُها " +
                      "`paypal ·` أَو `paddle ·` (أَثَرُ فَوتَرَةِ المَنَصَّةِ لا أَثَرُك)، " +
                      "وصُفوفُ الاستِيرادِ مِن جَدوَلَي " +
                      $"{string.Join(" و", TenantExportRedaction.WithheldImportTables)} " +
                      "(رُموزُ أَجهِزَةٍ حَيَّةٌ بِأَعمِدَةٍ غَيرِ مَقروءَة).");
        sb.AppendLine();

        if (content.MissingFileKeys.Count > 0)
        {
            sb.AppendLine($"## مِلَفّاتٌ مَفقودَة ({content.MissingFileKeys.Count})");
            sb.AppendLine();
            sb.AppendLine("مَفاتيحُ مَذكورَةٌ في الوَثائِقِ ولا كائِنَ لَها في المَخزَن — " +
                          "قائِمَتُها في `files/MISSING.txt`.");
            sb.AppendLine();
        }

        foreach (var n in content.NotesAr) sb.AppendLine($"> {n}").AppendLine();

        sb.AppendLine("## مَسؤولِيَّتُكَ بَعدَ الاستِلام");
        sb.AppendLine();
        sb.AppendLine("هذِه الحَقيبَةُ تَحمِلُ **بَياناتٍ شَخصِيَّةً** لِأَشخاصٍ حَقيقيّين: " +
                      "أَسماءً وهَواتِفَ وبُرُداً وأَرقامَ هُوِيَّةٍ ونُصوصَ مُحادَثاتٍ خاصَّة.");
        sb.AppendLine();
        sb.AppendLine("- كانَت وَسايِلُ تُعالِجُ هذِه البَياناتِ نِيابَةً عَنك. " +
                      "وبِتَسَلُّمِكَ إيّاها تَصيرُ **جِهَةَ تَحَكُّم** فيها وَحدَك، " +
                      "ويَنتَقِلُ إلَيكَ كُلُّ ما يَتَرَتَّبُ على ذلك.");
        sb.AppendLine("- **حُقوقُ أَصحابِ البَياناتِ تَنتَقِلُ مَعَها**: الوُصولُ، " +
                      "والتَصحيحُ، والإتلاف. وهُم يُطالِبونَكَ بِها بَعدَ اليَومِ لا يُطالِبونَنا.");
        sb.AppendLine("- **والنَقلُ والتَخزينُ عَلَيك**: احفَظِ الحَقيبَةَ في مَوضِعٍ مُعَمّىً، " +
                      "ولا تُرسِلها بِبَريدٍ ولا بِرابِطٍ مَفتوح، وأَتلِف النُسَخَ الزائِدَة.");
        sb.AppendLine("- **والإبلاغُ عَن التَسَرُّبِ صارَ واجِبَك** بِالنِسبَةِ لِهذِه النُسخَة.");
        sb.AppendLine("- وإن كانَ تَخزينُكَ خارِجَ المَملَكَة، فَراجِع أَحكامَ نَقلِ " +
                      "البَياناتِ الشَخصِيَّةِ خارِجَها **قَبلَ** النَقلِ لا بَعدَه.");
        sb.AppendLine();
        sb.AppendLine("> هذا وَصفٌ تَشغيليٌّ لا رَأيٌ نِظاميّ. راجِع مُستَشاراً " +
                      "نِظامِيّاً قَبلَ بِناءِ التِزامٍ تَعاقُديٍّ عَلَيه.");
        sb.AppendLine();
        return sb.ToString();
    }
}
