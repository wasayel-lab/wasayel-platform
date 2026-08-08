using System.Text.Json;
using ClosedXML.Excel;

namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// تَصدير دِراسَة الجَدوى لِملَفّ Excel مُتَعَدِّد الأَوراق: مُلَخَّص،
/// السوق، نَموذَج الإيراد، التَّكلِفَة، الماليّ، شَرائِح، المنافِسون،
/// المَخاطِر، الدُروس، خارِطَة الطَّريق. RTL، عَناوين عَرَبيَّة.
/// </summary>
public sealed class FeasibilityExcelExporter
{
    public byte[] Export(IncubatorSession session)
    {
        using var wb = new XLWorkbook();
        wb.RightToLeft = true;
        wb.Properties.Title = "دِراسَة جَدوى";
        wb.Properties.Author = "ACommerce Studio";

        AddCover(wb, session);

        if (session.AnalysisJson is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(session.AnalysisJson);
                var r = doc.RootElement;
                if (r.TryGetProperty("summary", out var sum))            AddObject(wb, "مُلَخَّص", sum);
                if (r.TryGetProperty("marketSizing", out var ms))        AddObject(wb, "حَجم السوق", ms);
                if (r.TryGetProperty("revenueModel", out var rm))        AddObject(wb, "نَموذَج الإيراد", rm);
                if (r.TryGetProperty("costStructure", out var cs))       AddObject(wb, "هَيكَل التَّكلِفَة", cs);
                if (r.TryGetProperty("financialProjection", out var fp)) AddObject(wb, "التَّوَقُّع المالي", fp);
                if (r.TryGetProperty("customerSegments", out var sg))    AddArray(wb, "شَرائِح العُملاء", sg);
                if (r.TryGetProperty("competitors", out var cp))         AddArray(wb, "المنافِسون", cp);
                if (r.TryGetProperty("risks", out var rk))               AddArray(wb, "المَخاطِر", rk);
                if (r.TryGetProperty("lessonsFromFailures", out var ls)) AddSimpleList(wb, "دُروس فَشَل", ls);
                if (r.TryGetProperty("roadmap", out var rd))             AddArray(wb, "خارِطَة الطَّريق", rd);
                if (r.TryGetProperty("kpis", out var kp))                AddArray(wb, "مُؤَشِّرات الأَداء", kp);
                if (r.TryGetProperty("recommendedPattern", out var rp))  AddObject(wb, "النَّمَط المُقتَرَح", rp);
                if (r.TryGetProperty("recommendations", out var recs))   AddSimpleList(wb, "تَوصيات", recs);
            }
            catch { /* JSON تالِف — نَكتَفي بِالغِلاف */ }
        }

        using var ms2 = new MemoryStream();
        wb.SaveAs(ms2);
        return ms2.ToArray();
    }

    static void AddCover(XLWorkbook wb, IncubatorSession s)
    {
        var ws = wb.Worksheets.Add("الغِلاف");
        ws.Style.Font.FontName = "Cairo";
        var t = ws.Cell("B2");
        t.Value = "دِراسَة جَدوى";
        t.Style.Font.FontSize = 24;
        t.Style.Font.Bold = true;
        t.Style.Font.FontColor = XLColor.FromHtml("#1d4ed8");

        ws.Cell("B4").Value = "الفِكرَة:";
        ws.Cell("B4").Style.Font.Bold = true;
        ws.Cell("C4").Value = string.IsNullOrEmpty(s.ProjectDescription) ? "—" : s.ProjectDescription;
        ws.Cell("C4").Style.Alignment.WrapText = true;
        ws.Row(4).Height = 60;

        var rows = new (string K, string V)[]
        {
            ("النَّمَط المُقتَرَح:", s.SuggestedPattern),
            ("ثِقَة الاقتِراح:",      s.PatternConfidence),
            ("الحالَة:",              s.Status.ToString()),
            ("جَودَة التَّحليل:",     $"{s.AnalysisQualityScore}%"),
            ("آخِر تَحديث:",          s.UpdatedAt.ToString("yyyy-MM-dd HH:mm")),
            ("إصدار الـ prompt:",    s.PromptVersion),
        };
        var row = 6;
        foreach (var (k, v) in rows)
        {
            ws.Cell($"B{row}").Value = k;
            ws.Cell($"B{row}").Style.Font.Bold = true;
            ws.Cell($"C{row}").Value = v;
            row++;
        }
        ws.Column("B").Width = 25;
        ws.Column("C").Width = 70;

        ws.Cell($"B{row + 2}").Value =
            "تَنويه: هذا التَّحليل مُولَّد آلِيّاً بِناءً على بيانات السوق المُتاحَة. " +
            "المَنصَّة غَير مَسؤولَة عَن نَتائِج القَرارات المَبنيَّة عَلَيه. يُوصَى بِالتَّحَقُّق المُستَقِلّ.";
        ws.Range($"B{row + 2}:C{row + 2}").Merge();
        ws.Cell($"B{row + 2}").Style.Font.Italic = true;
        ws.Cell($"B{row + 2}").Style.Font.FontColor = XLColor.FromHtml("#64748b");
        ws.Cell($"B{row + 2}").Style.Alignment.WrapText = true;
        ws.Row(row + 2).Height = 60;
    }

    static void AddObject(XLWorkbook wb, string name, JsonElement obj)
    {
        var ws = wb.Worksheets.Add(SafeName(name));
        ws.Style.Font.FontName = "Cairo";
        Header(ws, "B1", name);
        var row = 3;
        foreach (var prop in obj.EnumerateObject())
        {
            ws.Cell($"B{row}").Value = ArabicKey(prop.Name);
            ws.Cell($"B{row}").Style.Font.Bold = true;
            ws.Cell($"C{row}").Value = Flatten(prop.Value);
            ws.Cell($"C{row}").Style.Alignment.WrapText = true;
            ws.Row(row).Height = Math.Min(80, 16 + (Flatten(prop.Value).Length / 60) * 14);
            row++;
        }
        ws.Column("B").Width = 28;
        ws.Column("C").Width = 80;
    }

    static void AddArray(XLWorkbook wb, string name, JsonElement arr)
    {
        var ws = wb.Worksheets.Add(SafeName(name));
        ws.Style.Font.FontName = "Cairo";
        Header(ws, "B1", name);
        if (arr.GetArrayLength() == 0) { ws.Cell("B3").Value = "لا عَناصِر."; return; }

        var first = arr.EnumerateArray().First();
        var keys = first.EnumerateObject().Select(p => p.Name).ToList();
        var col = 2; // B
        foreach (var k in keys)
        {
            var c = ws.Cell(3, col);
            c.Value = ArabicKey(k);
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");
            ws.Column(col).Width = 25;
            col++;
        }
        var rowIdx = 4;
        foreach (var item in arr.EnumerateArray())
        {
            var cIdx = 2;
            foreach (var k in keys)
            {
                if (item.TryGetProperty(k, out var v))
                {
                    var cell = ws.Cell(rowIdx, cIdx);
                    cell.Value = Flatten(v);
                    cell.Style.Alignment.WrapText = true;
                }
                cIdx++;
            }
            rowIdx++;
        }
    }

    static void AddSimpleList(XLWorkbook wb, string name, JsonElement arr)
    {
        var ws = wb.Worksheets.Add(SafeName(name));
        ws.Style.Font.FontName = "Cairo";
        Header(ws, "B1", name);
        var row = 3;
        foreach (var item in arr.EnumerateArray())
        {
            ws.Cell($"B{row}").Value = row - 2;
            ws.Cell($"B{row}").Style.Font.Bold = true;
            ws.Cell($"C{row}").Value = item.ValueKind == JsonValueKind.String
                ? item.GetString() : item.ToString();
            ws.Cell($"C{row}").Style.Alignment.WrapText = true;
            row++;
        }
        ws.Column("B").Width = 6;
        ws.Column("C").Width = 90;
    }

    static void Header(IXLWorksheet ws, string addr, string text)
    {
        var c = ws.Cell(addr);
        c.Value = text;
        c.Style.Font.FontSize = 16;
        c.Style.Font.Bold = true;
        c.Style.Font.FontColor = XLColor.FromHtml("#1d4ed8");
    }

    /// <summary>أَسماء الأَوراق في Excel مَحدودَة بِـ 31 حَرف + لا تَحوي / \ ? * [ ].</summary>
    static string SafeName(string s)
    {
        var clean = new string(s.Where(ch => !"/\\?*[]".Contains(ch)).ToArray());
        return clean.Length > 31 ? clean[..31] : clean;
    }

    /// <summary>JSON value → نَصّ مُسطَّح لِخَلِيَّة.</summary>
    static string Flatten(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Number => v.ToString(),
        JsonValueKind.True => "نَعَم",
        JsonValueKind.False => "لا",
        JsonValueKind.Null => "—",
        JsonValueKind.Array => string.Join(" · ",
            v.EnumerateArray().Select(Flatten)),
        JsonValueKind.Object => string.Join(" | ",
            v.EnumerateObject().Select(p => $"{ArabicKey(p.Name)}: {Flatten(p.Value)}")),
        _ => v.ToString()
    };

    static string ArabicKey(string k) => k switch
    {
        "marketSizeSar"           => "حَجم السوق (ر.س)",
        "estimatedCustomersYear1" => "عُملاء السَّنَة الأولى",
        "requiredInvestmentSar"   => "الاستِثمار المَطلوب (ر.س)",
        "verdict"                 => "الحُكم",
        "tam"                     => "TAM (السوق الكُلّي)",
        "sam"                     => "SAM (السوق المُتاح)",
        "som"                     => "SOM (السوق المُستَهدَف)",
        "reasoning"               => "التَّحليل",
        "model"                   => "النَّموذَج",
        "pricingSar"              => "التَّسعير (ر.س)",
        "unitEconomics"           => "اقتِصاد الوَحدَة",
        "launchSar"               => "الإطلاق (ر.س)",
        "monthlyOpsSar"           => "التَّشغيل الشَّهري (ر.س)",
        "cacSar"                  => "CAC (ر.س)",
        "ltvSar"                  => "LTV (ر.س)",
        "year1RevenueSar"         => "إيراد السَّنَة ١ (ر.س)",
        "breakEvenMonths"         => "التَّعادُل (شهر)",
        "scenario"                => "السيناريو",
        "name"                    => "الاسم",
        "description"             => "الوَصف",
        "sizeEstimate"            => "تَقدير الحَجم",
        "positioning"             => "التَّموضُع",
        "gap"                     => "الفَجوَة",
        "title"                   => "العُنوان",
        "category"                => "الفِئَة",
        "impact"                  => "الأَثَر",
        "likelihood"              => "الاحتِمال",
        "mitigation"              => "التَّخفيف",
        "phase"                   => "المَرحَلَة",
        "durationWeeks"           => "المُدَّة (أسبوع)",
        "goal"                    => "الهَدَف",
        "target"                  => "الهَدَف",
        "pattern"                 => "النَّمَط",
        "timeToLaunch"            => "وَقت الإطلاق",
        _ => k
    };
}
