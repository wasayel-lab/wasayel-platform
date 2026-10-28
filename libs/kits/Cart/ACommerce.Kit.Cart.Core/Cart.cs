namespace ACommerce.Kit.Cart;

/// <summary>
/// سَلَّة شِراء — مَخزون كَ Marten document لِكُلّ مُستَخدِم في كُلّ مَتجَر.
/// مُلائِم لِأَنماط cafe/grocery/marketplace حَيث يَجمَع المُستَخدِم عِدَّة
/// إعلانات قَبل دَفعَة واحِدَة. غَير مُستَخدَم في أَنماط trip/rental.
/// </summary>
public sealed class Cart
{
    /// <summary>المُعَرِّف = <c>{userId}</c> (سَلَّة واحِدَة لِكُلّ مُستَخدِم
    /// في تَينَنت — Marten يُقَسِّم تِلقائيّاً عَلى schema المُستَأجِر).</summary>
    public Guid Id { get; set; }
    public List<CartItem> Items { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// <para><b>ما كَتَبَه المُشتَري في خُطُواتِ الإتمام</b> (‏2026-10-28، قَرارُ المالِك د60 «ب»؛ مَرجِعُه OWASP وCWE-598): الاسمُ والجَوّالُ
    /// والعُنوانُ وطَريقَةُ الدَفعِ تُحفَظُ هُنا عِندَ الخادِمِ بَينَ الخُطوَةِ والأُخرى — وكانَت تُحمَلُ في رابِطِ الصَفحَةِ فَتُحفَظُ في سِجِلِّ المُتَصَفِّحِ
    /// وسِجِلّاتِ الخَوادِم. و<c>null</c> لِسَلَّةٍ لَم يُكتَب لَها شَيء.</para>
    /// <para>ولا تَمَسُّ <see cref="UpdatedAt"/>: تِلكَ نُسخَةُ البُنودِ الَّتي تُطالِبُ بِها صَفَقاتُ الإتمام، والبَياناتُ لَيسَت بُنوداً. وتُحذَفُ السَلَّةُ
    /// كُلُّها بِحَذفِ الحِسابِ في مُعامَلَتِه.</para>
    /// </summary>
    public CartCheckout? Checkout { get; set; }

    /// <summary>عَدد القِطَع الكُلّيّ (مَجموع الكَمِّيّات).</summary>
    public int TotalQuantity => Items.Sum(i => i.Quantity);
    /// <summary>إجمالي السِّعر (مَجموع كَمِّيَّة × سِعر).</summary>
    public decimal SubtotalSar => Items.Sum(i => i.UnitPriceSar * i.Quantity);
}

public sealed class CartItem
{
    public Guid ListingId { get; set; }
    public string Title { get; set; } = "";
    public decimal UnitPriceSar { get; set; }
    public int Quantity { get; set; } = 1;
    /// <summary>اختِيارات إضافِيَّة (size/options) — JSON-able.</summary>
    public Dictionary<string, string> Options { get; set; } = new();
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>بَياناتُ الإتمامِ المَحفوظَةُ في السَلَّة (<see cref="Cart.Checkout"/>) — كَما كَتَبَها المُشتَري، مُقَصوصَة.</summary>
public sealed class CartCheckout
{
    public string RecipientName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    /// <summary>طَريقَةُ الدَفعِ المُختارَةُ في الخُطوَةِ الثانِيَة — خامٌ كَما أُرسِلَت؛ وتُطَبَّعُ حَيثُ تُقرَأ.</summary>
    public string Pay { get; set; } = "";
}

public sealed record AddToCart(Guid UserId, Guid ListingId, string Title, decimal UnitPriceSar, int Quantity = 1, Dictionary<string, string>? Options = null);
public sealed record UpdateCartQuantity(Guid UserId, Guid ListingId, int NewQuantity);
public sealed record RemoveFromCart(Guid UserId, Guid ListingId);
public sealed record ClearCart(Guid UserId);
