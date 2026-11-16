#!/usr/bin/env dotnet
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false
#:project ../libs/templates/ACommerce.Templates.Customer.Marketplace/ACommerce.Templates.Customer.Marketplace.csproj
#:project ../libs/core/ACommerce.Platform.MultiTenancy/ACommerce.Platform.MultiTenancy.csproj
// ═══════════════════════════════════════════════════════════════════════
//  المَتاجِرُ القائِمَةُ الَّتي لا يَقبَلُ مُعَرِّفَها شَكلُ المُعَرِّفِ الواحِد — عَرضٌ فَقَط
// ───────────────────────────────────────────────────────────────────────
//  **لِماذا هذا المِلَفّ** (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·SEC-C1-2؛ «ب» في سِجِلِّ المالِك): صارَ لِأَبوابِ الإنشاءِ الثَلاثَةِ شَكلٌ
//  واحِد (`TenantFromAnalysisFactory.SlugRefusal`: يَبدَأُ بِحَرفٍ أَو رَقَم، ثُمَّ حُروفٌ وأَرقامٌ وشَرطَة، 63 عَلى الأَكثَر، ولا مَحجوزَ ولا قِسمَ
//  قاعِدَة). وقَبلَه كانَ `^[a-z0-9_-]+$` بِلا حَدٍّ ولا قائِمَةِ أَقسام — فَقَد يَكونُ في الإنتاجِ مَتجَرٌ وُلِدَ عَلى غَيرِ الشَكل (`_studio`،
//  `-`، `my_store`)، وعَدَدُه غَيرُ مَقيس: القاعِدَةُ مَحظورَةٌ عَلى الوَكيل.
//
//  **ولا يَكتُبُ شَيئاً**: لا بابَ يُغَيِّرُ مُعَرِّفَ مَتجَر (وَثائِقُه في قِسمٍ بِاسمِه)، فَما يُفعَلُ بِكُلِّ مَتجَرٍ يُطبَعُ هُنا قَرارُ المالِكِ
//  بِحُضورِه — والافتِراضُ إلى أَن يَقول: يُترَكُ كَما هُوَ ويُعرَف. ويُطبَعُ المُعَرِّفُ ورَمزُ الرَدِّ ووُجودُ مالِكٍ وَحدَها — لا جَوّالَ ولا بَريدَ
//  ولا اسم.
//
//  **و‏2026-11-07 (ADR-050 §٣، W13-4·C2a·3 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه)**: البابُ صارَ يَسأَلُ مُصادِقَ اسمِ المُضيف (`StoreHostName`) — فَالمَتجَرُ
//  الَّذي يَسرُدُه هذا السكربتُ الآنَ هُوَ أَيضاً المَتجَرُ الَّذي **لا مُضيفَ لَه** (`{slug}.R`): شَرطَةٌ في الآخِر، أَو شَرطَتان، أَو أَطوَلُ مِن 53، أَو
//  يَنتَهي بِاسمِ مُضيفِ دَور. يَبقى بِمَسارِه كَما هُوَ ولا يُعادُ تَسمِيَتُه («ب» في سِجِلِّ المالِك)، ويُطبَعُ بِجانِبِ رَمزِ البابِ رُموزُ المُصادِق.
//
//  **و‏2026-11-07 (مُراجَعَةُ C2a الجَولَةُ الأُولى · C2a-P-02 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه)**: مُعَرِّفٌ صارَ مَحجوزاً بَعدَ أَن وُلِدَ (`lang` مُنذُ
//  `a180a7c`) **يَفقِدُ مَسارَه أَيضاً لا مُضيفَه وَحدَه، والنِطاقُ فارِغ**: ‏`SlugFromPath` يَقرَأُ القائِمَةَ نَفسَها فَيَرُدُّه `null`، فَلا سِياقَ مَتجَرٍ
//  ويُرسَمُ `/lang` بِقِشرَةِ المَنَصَّة (مَقيس). فَيُعَدُّ هذا وَحدَه بِسُؤالِ الحالِّ نَفسِه، ويَخرُجُ بِرَمزٍ خاصٍّ — ويُشَغَّلُ **قَبلَ نَشرِ أَيِّ بِناءٍ يَحمِلُ
//  `a180a7c`** لا قَبلَ ضَبطِ النِطاق.
//
// ─── الاستِعمال ────────────────────────────────────────────────────────
//   export ConnectionStrings__Postgres="…"        # ولا تُطبَع أَبَداً
//   dotnet run scripts/list-off-shape-store-slugs.cs
//
// ─── رُموزُ الخُروج ────────────────────────────────────────────────────
//   0 — كُلُّ مَتجَرٍ عَلى الشَكل.
//   2 — وُجِدَ مَتجَرٌ عَلى غَيرِ الشَكل (لَم يُكتَب شَيء) — يَبقى بِمَسارِه بِلا مُضيف.
//   3 — وُجِدَ مَتجَرٌ **لا يَحُلُّه المَسارُ** (مُعَرِّفُه مَحجوز) — يُقَرِّرُ المالِكُ فيه قَبلَ النَشر؛ ويَغلِبُ 2.
//   1 — خَطَأ، أَو صِفرُ مَتجَرٍ مَفحوص (أَداةٌ عَمياء — القاعِدَة ١٠).
// ═══════════════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using System.Text;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using JasperFx;
using Marten;

var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
if (string.IsNullOrWhiteSpace(cs))
{
    Console.Error.WriteLine("خَطَأ: ‏ConnectionStrings__Postgres غَير مَضبوط.");
    return 1;
}
var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cs))).ToLowerInvariant()[..12];

using var store = DocumentStore.For(o =>
{
    o.Connection(cs);
    o.DatabaseSchemaName = "platform";
    o.AutoCreateSchemaObjects = AutoCreate.None;   // ← لا يُمَسُّ المُخَطَّط
    o.Policies.AllDocumentsAreMultiTenanted();
    o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);
});

List<Tenant> tenants;
await using (var qs = store.QuerySession())
    tenants = (await qs.Query<Tenant>().ToListAsync()).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();

Console.WriteLine($"القاعِدَة: بَصمَة {fingerprint} · المَتاجِرُ المَفحوصَة: {tenants.Count}");
Console.WriteLine("الوَضع:   عَرضٌ فَقَط — بِلا كِتابَة");
Console.WriteLine(new string('-', 78));
if (tenants.Count == 0)
{
    Console.Error.WriteLine("✗ BLIND CHECK: صِفرُ مَتجَرٍ مَفحوص — لا يُقال «كُلُّها عَلى الشَكل» عَن قاعِدَةٍ لَم تُقرَأ.");
    return 1;
}

var off = tenants.Select(t => (Tenant: t, Code: TenantFromAnalysisFactory.SlugRefusal(t.Id))).Where(x => x.Code is not null).ToList();
// ‏(C2a-P-02) الحالُّ نَفسُه يُسأَل: مَتجَرٌ لا يَحُلُّه مَسارُه `/{id}` لا سِياقَ لَه عَلى المَسارِ ولا عَلى المُضيف.
var pathless = tenants.Where(t => TenantResolverMiddleware.SlugFromPath("/" + t.Id) is null).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
Console.WriteLine($"عَلى غَيرِ الشَكل (ولا مُضيفَ لَه): {off.Count}");
Console.WriteLine($"ومِنها بِلا مَسارٍ أَيضاً (مُعَرِّفُه مَحجوزٌ فَلا يَحُلُّه SlugFromPath): {pathless.Count}");
foreach (var (t, code) in off)
    Console.WriteLine($"  # {t.Id}  ← {code} [{string.Join(" ", StoreHostName.Validate(t.Id).Select(v => v.Code))}]{(pathless.Contains(t.Id) ? " · بِلا مَسار" : "")} · مالِك {(t.OwnerUserId == Guid.Empty ? "لا" : "نَعَم")} · مُعَلَّق {(t.IsSuspended ? "نَعَم" : "لا")}");
if (off.Count == 0 && pathless.Count == 0)
{
    Console.WriteLine("كُلُّ مَتجَرٍ عَلى الشَكل.");
    return 0;
}
Console.WriteLine();
Console.WriteLine("عَرضٌ فَقَط — لَم يُكتَب شَيء. ما يُفعَلُ بِكُلِّ مَتجَرٍ قَرارُ المالِك (لا بابَ يُغَيِّرُ المُعَرِّف).");
if (pathless.Count > 0)
{
    Console.WriteLine($"✗ {pathless.Count} مَتجَرٌ لا يَحُلُّه مَسارُه — لا يُنشَرُ بِناءٌ يَحمِلُ حَجزَ اسمِه قَبلَ قَرارِ المالِك فيه (DEPLOY §٢·ي).");
    return 3;
}
return 2;
