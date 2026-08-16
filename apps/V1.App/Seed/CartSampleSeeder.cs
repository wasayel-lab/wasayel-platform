using ACommerce.Kit.Auth;
using ACommerce.Kit.Cart;
using Marten;

namespace ACommerce.V1.App.Seed;

/// <summary>
/// <para><b>سَلَّةُ عَيِّنَةٍ بِنيَوِيَّة — لِتُصَيَّرَ شاشَةٌ لا لِتُقرَأَ
/// أَرقامُها.</b> ‏<c>CheckoutPage</c> مُعالِجٌ ثُلاثيّ الخُطوات، وجِسمُه
/// كُلُّه خَلفَ شَرطٍ واحِد: <c>cart.Items.Count > 0</c>. وكانَ في
/// القاعِدَة <b>صِفرُ سَلَّة</b> (مَقيس: <c>select count(*) from
/// platform.mt_doc_cart</c> ⇒ ‏0)، فَالشاشَةُ رُحِّلَت في المَوجَة
/// التاسِعَة بِبُرهان <c>HEAD</c> وَحدَه — أَي أَنّ ثَلاثَ خُطُواتٍ
/// كامِلَة خَرَجَت مِن البُرهان البايتيّ.</para>
///
/// <para>ونَفسُ اصطِلاح <see cref="IncubatorSampleSeeder"/> حَرفاً:
/// مُتَغَيِّرُ بيئَة، ومُعَرِّفاتٌ ثابِتَة، و<c>idempotent</c>، وبِلا
/// المُتَغَيِّر <b>صِفرُ قِراءَةٍ وصِفرُ كِتابَة</b>.</para>
///
/// <code>
/// export CART_SAMPLE_SEED=1
/// dotnet run --project apps/V1.App --urls=http://localhost:5050
/// </code>
///
/// <para><b>والقيَمُ مُعَلَّمَةٌ عَيِّنَةً لا تُشبِه الحَقيقَة</b> (القاعِدَة
/// ١٦): العُنوانُ يَبدَأ بِـ<see cref="Mark"/>، و<c>UnitPriceSar</c>
/// يُكتَب <b>صِفراً</b> — لِنَفس سَبَبِ <c>durationWeeks = 0</c> في بَذرَة
/// الحاضِنَة: السِعرُ بَيانُ مُنتَج، ورَقمٌ يَبدو مَعقولاً في سَلَّةٍ
/// يَصير مَرجِعاً لِمَن يَقرَؤُه. أَمّا <c>Quantity</c> فَبِنيَةٌ لا
/// اقتِصاد — واثنانِ في السَطر الثاني لِيُصَيَّرَ ضَربُ الكَمِّيَّة
/// (<c>@item.Title × @item.Quantity</c>) لا لِيُقرَأ.</para>
///
/// <para><b>ومالِكُها لَيسَ Guid مَكتوباً</b>: مُعَرِّفُ السَلَّة <b>هو</b>
/// مُعَرِّفُ صاحِبِها بِعَقد <see cref="Cart"/> نَفسِه، فَيُبحَث عَن
/// المُستَخدِم بِهاتِفِه — <c>CART_SAMPLE_PHONE</c>، وافتِراضُه هاتِفُ
/// أَوَّلِ مُستَخدِمي <c>ejar</c> الَّذي يَبذُرُه
/// <see cref="TestDataSeeder"/> (<c>05{tenantIdx}{roleIdx}1234567</c>).
/// وهو نَفسُ مِلَفِّ <c>member</c> في <c>capture-appearance.sh</c> —
/// فَالعُنوانُ في اللَقطَة يُصادِف صاحِبَ السَلَّة بِلا اكتِشاف.</para>
///
/// <para><b>وأَثَرُها المُعلَن على لَقطَةٍ قائِمَة</b>: صَفحَتانِ فَقَط —
/// <c>/{slug}/cart</c> و<c>/{slug}/checkout</c> لِصاحِبِ السَلَّة — إذ
/// شِلُّ التَطبيق يَعرِض <b>رابِطَ</b> السَلَّة لا عَدَّها (مَقيس:
/// صِفرُ مُستَهلِك لِـ<c>Cart.TotalQuantity</c> خارِجَ
/// <c>CartPage</c>). فَلا يَنتَشِرُ الأَثَرُ إلى بَقِيَّة الصَفَحات.</para>
///
/// <para>وحَذفُها سَطرٌ واحِد:
/// <c>delete from platform.mt_doc_cart where id = '{userId}'</c>.</para>
/// </summary>
public static class CartSampleSeeder
{
    /// <summary>وَسمُ العَيِّنَة — يَتَصَدَّر كُلَّ نَصٍّ مَنصوص.</summary>
    private const string Mark = "عَيِّنَة بِنيَة — لا مُنتَجَ هُنا";

    /// <summary>هاتِفُ صاحِبِ السَلَّة الافتِراضيّ: أَوَّلُ مُستَخدِمي
    /// <c>ejar</c> مِن <see cref="TestDataSeeder"/>، وهو نَفسُه مِلَفُّ
    /// <c>member</c> في أَداة اللَقطَة.</summary>
    private const string DefaultPhone = "05101234567";

    private const string DefaultTenant = "ejar";

    /// <summary>مُعَرِّفا سَطرَي العَيِّنَة — ثابِتان، فَإعادَةُ التَشغيل
    /// لا تُنتِج سُطوراً جَديدَة ولَو حُذِفَت السَلَّة وأُعيدَت.</summary>
    public static readonly Guid ItemAId =
        Guid.Parse("5a3e1d00-0000-4000-8000-0000000ca401");
    public static readonly Guid ItemBId =
        Guid.Parse("5a3e1d00-0000-4000-8000-0000000ca402");

    public static async Task RunAsync(IServiceProvider services)
    {
        var store = services.GetRequiredService<IDocumentStore>();
        var tenant = Environment.GetEnvironmentVariable("CART_SAMPLE_TENANT");
        if (string.IsNullOrWhiteSpace(tenant)) tenant = DefaultTenant;
        var phone = Environment.GetEnvironmentVariable("CART_SAMPLE_PHONE");
        if (string.IsNullOrWhiteSpace(phone)) phone = DefaultPhone;

        await using var qs = store.QuerySession(tenant);
        var owner = (await qs.Query<User>()
            .Where(u => u.Phone == phone).Take(1).ToListAsync()).FirstOrDefault();
        if (owner is null)
        {
            Console.WriteLine($"[CartSample] لا مُستَخدِمَ بِهاتِف {phone} في «{tenant}» — لا بَذرَة.");
            return;
        }

        await using var session = store.LightweightSession(tenant);
        var existing = await session.LoadAsync<Cart>(owner.Id);
        if (existing is not null)
        {
            Console.WriteLine($"[CartSample] قائِمَة أَصلاً: {owner.Id}");
            return;
        }

        // زَمَنٌ ثابِتٌ لا `UtcNow` — لِنَفس سَبَبِ بَذرَة الحاضِنَة:
        // اللَقطَةُ تُقارَن بايتاً بِبايت، ووَقتُ الإقلاع كانَ سَيَجعَل
        // كُلَّ التِقاطٍ مُختَلِفاً عَن سابِقِه.
        var at = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        session.Store(new Cart
        {
            Id = owner.Id,
            UpdatedAt = at,
            Items =
            {
                new CartItem
                {
                    ListingId = ItemAId,
                    Title = $"{Mark} — سَطرٌ أَوَّل",
                    UnitPriceSar = 0m,
                    Quantity = 1,
                    AddedAt = at
                },
                new CartItem
                {
                    ListingId = ItemBId,
                    Title = $"{Mark} — سَطرٌ ثانٍ",
                    UnitPriceSar = 0m,
                    Quantity = 2,
                    AddedAt = at
                }
            }
        });
        await session.SaveChangesAsync();
        Console.WriteLine($"[CartSample] ✅ بُذِرَت سَلَّةُ عَيِّنَة لِـ{owner.Id} في «{tenant}»");
    }
}
