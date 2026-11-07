#!/usr/bin/env dotnet
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false
#:project ../libs/templates/ACommerce.Templates.Customer.Marketplace/ACommerce.Templates.Customer.Marketplace.csproj
// ═══════════════════════════════════════════════════════════════════════
//  المَتاجِرُ القائِمَةُ الَّتي فاتَ اسمُها أَو سَطرُها أَو مَدينَتُها حَدَّ الاستِمارَة — عَرضٌ فَقَط
// ───────────────────────────────────────────────────────────────────────
//  **لِماذا هذا المِلَفّ** (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 الجَولَةُ الثانِيَة · W13-1·SEC2-C1-2؛ «ب» في سِجِلِّ المالِك): صارَت حُدودُ الهُوِيَّة
//  (`StudioStoreCreate.NameMax` ‏60، و`TagLineMax` ‏120، و`CityMax` ‏40 — أَرقامُ الاستِمارات) تُسأَلُ في كُلِّ بابٍ يَكتُبُها: الإنشاءُ و«الهُوِيَّة»
//  وبابُ المُشرِفِ وأَداتا الوَكيل. وقَبلَها كانَ حِفظُ «الهُوِيَّة» يَقبَلُ اسماً بِمِئَتَي أَلفِ حَرف — فَقَد يَكونُ في الإنتاجِ مَتجَرٌ فَوقَ الحَدّ،
//  وعَدَدُه غَيرُ مَقيس: القاعِدَةُ مَحظورَةٌ عَلى الوَكيل.
//
//  **ولا يَكتُبُ شَيئاً**: ما يُفعَلُ بِكُلِّ مَتجَرٍ (قَصٌّ، أَو رِسالَةٌ لِصاحِبِه، أَو تَركُه) قَرارُ المالِكِ بِحُضورِه — والافتِراضُ إلى أَن يَقول:
//  يُترَكُ ويُعرَف، وصاحِبُه يُرَدُّ بِرَمزِ الحَدِّ عِندَ حِفظِ «الهُوِيَّة» التالي فَيُقَصِّرُه بِيَدِه. ويُطبَعُ المُعَرِّفُ وأَطوالُ الحُقولِ وأَوَّلُ رَمزٍ
//  فاتَ حَدَّه وَحدَها — لا الاسمُ نَفسُه (قَد يَكونُ مِئَتَي أَلفِ حَرف) ولا جَوّالَ ولا بَريد.
//
// ─── الاستِعمال ────────────────────────────────────────────────────────
//   export ConnectionStrings__Postgres="…"        # ولا تُطبَع أَبَداً
//   dotnet run scripts/list-over-long-store-identities.cs
//
// ─── رُموزُ الخُروج ────────────────────────────────────────────────────
//   0 — كُلُّ مَتجَرٍ في حُدودِه.
//   2 — وُجِدَ مَتجَرٌ فَوقَ الحَدّ (لَم يُكتَب شَيء).
//   1 — خَطَأ، أَو صِفرُ مَتجَرٍ مَفحوص (أَداةٌ عَمياء — القاعِدَة ١٠).
// ═══════════════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using System.Text;
using ACommerce.Kit.Tenants;
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
Console.WriteLine($"الحُدود:  الاسم {StudioStoreCreate.NameMax} · السَطر {StudioStoreCreate.TagLineMax} · المَدينَة {StudioStoreCreate.CityMax}");
Console.WriteLine("الوَضع:   عَرضٌ فَقَط — بِلا كِتابَة");
Console.WriteLine(new string('-', 78));
if (tenants.Count == 0)
{
    Console.Error.WriteLine("✗ BLIND CHECK: صِفرُ مَتجَرٍ مَفحوص — لا يُقال «كُلُّها في حُدودِها» عَن قاعِدَةٍ لَم تُقرَأ.");
    return 1;
}

var over = tenants
    .Select(t => (Tenant: t, Code: StudioStoreCreate.LengthRefusal(t.Name ?? "", t.TagLine ?? "", t.City ?? "")))
    .Where(x => x.Code is not null).ToList();
Console.WriteLine($"فَوقَ الحَدّ: {over.Count}");
foreach (var (t, code) in over)
    Console.WriteLine($"  # {t.Id}  ← {code} · الاسم {t.Name?.Length ?? 0} · السَطر {t.TagLine?.Length ?? 0} · المَدينَة {t.City?.Length ?? 0} · مالِك {(t.OwnerUserId == Guid.Empty ? "لا" : "نَعَم")}");
if (over.Count == 0)
{
    Console.WriteLine("كُلُّ مَتجَرٍ في حُدودِه.");
    return 0;
}
Console.WriteLine();
Console.WriteLine("عَرضٌ فَقَط — لَم يُكتَب شَيء. ما يُفعَلُ بِكُلِّ مَتجَرٍ قَرارُ المالِك.");
return 2;
