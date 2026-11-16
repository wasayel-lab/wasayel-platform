using ACommerce.Kit.Auth;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ صاحِبُ المَتجَرِ الَّذي قَبِلَ الصَفقَةَ مِن الاستوديو لا يُقرَأُ «بائِعاً أَغلَقَ حِسابَه» (‏2026-11-05، الموجَة ١٢ G1b — مُراجَعَةُ الإصلاح ١، G1b-R1-2) ═══
//
// قَبلَه (`LiveChatPartnerGoneStoreOwnerSellerProofTests` يَطبَعُه ويَبقى بِبَصمَتِه، تَوصيفُه 5741f59): «لِصاحِبِ المَتجَر 1 «طَلَبُ استِرجاعٍ بائِعُه أَغلَقَ حِسابَه» ·
// return_told store · السِجِلّ «أَغلَقَ حِسابَه» True»، والتَلميحُ «أَغلَقَ البائِعُ حِسابَه»، وبِلا `listing_owner` سَطرُ «لَم يَعُد لِهذا البائِعِ حِساب». والقَرارُ «أ» (P1):
// صاحِبُ المَتجَرِ قَبلَ سُؤالِ الغِياب — قيمَةٌ رابِعَةٌ `owner` بِنَصٍّ مُحايِد، وإشعارٌ مُحايِدٌ لَه إلى صَفحَةِ الاستوديو، ولا بابَ مُحادَثَةٍ إلى مُعَرِّفِه في الاستوديو.

public class ChatPartnerGoneStoreOwnerSellerRowsTests(ITestOutputHelper output)
{
    /// <summary><b>المُوجَب لِـ«صاحِبُ المَتجَر»</b>: البائِعُ مُعَرِّفُه صاحِبُ المَتجَرِ — بِلا وَثيقَةٍ (مُعَرِّفُ الاستوديو) أَو بِوَثيقَةٍ قائِمَة، بَل ومَمحُوَّة. <b>والسالِب</b>: بائِعٌ آخَرُ
    /// غائِبٌ ⇐ إدارَةُ المَتجَر، وقائِمٌ ⇐ البائِع، ومَتجَرٌ بِلا صاحِبٍ لا يَجعَلُ أَحَداً «صاحِبَه».</summary>
    [Fact]
    public void The_store_owner_as_the_vendor_is_told_as_owner_before_partner_gone_is_asked()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var ownerDoc = new User { Id = owner, TenantSlug = "zz", FullName = "صاحِبُ المَتجَرِ عُضواً" };
        Assert.Equal(DealNotices.ReturnTold.Owner, DealNotices.ReturnToldFor(owner, null, owner));
        Assert.Equal(DealNotices.ReturnTold.Owner, DealNotices.ReturnToldFor(owner, ownerDoc, owner));
        Assert.Equal(DealNotices.ReturnTold.Owner, DealNotices.ReturnToldFor(owner, AccountDeletion.Erase(ownerDoc, DateTime.UtcNow), owner));
        Assert.Equal(DealNotices.ReturnTold.Store, DealNotices.ReturnToldFor(other, null, owner));
        Assert.Equal(DealNotices.ReturnTold.Vendor, DealNotices.ReturnToldFor(other, new User { Id = other, TenantSlug = "zz", FullName = "بائِع" }, owner));
        Assert.Equal(DealNotices.ReturnTold.Nobody, DealNotices.ReturnToldFor(owner, null, null));
    }

    /// <summary>القيمَةُ الرابِعَةُ ذَهاباً وإياباً، وبَديلُها لِكُلِّ مِفتاحٍ في العائِلَةِ وَحدَها؛ والسِجِلُّ لِلطَرَفَينِ ورَدُّ التَكرارِ يَقرَآنِها.</summary>
    [Fact]
    public void The_owner_value_reads_back_from_its_ref_and_takes_its_variant_inside_the_family_only()
    {
        Assert.Equal("owner", DealNotices.ReturnToldValue(DealNotices.ReturnTold.Owner));
        var deal = new Deal { Id = Guid.NewGuid() };
        deal.Refs[DealNotices.ReturnToldRef] = DealNotices.ReturnToldOwner;
        Assert.Equal(DealNotices.ReturnTold.Owner, DealNotices.ReturnToldOf(deal));
        foreach (var key in DealNotices.ReturnKeys)
            Assert.Equal(key + DealNotices.OwnerVariant, DealNotices.ReturnKey(key, DealNotices.ReturnTold.Owner));
        Assert.Equal("deals.detail.err_deal_not_found", DealNotices.ReturnKey("deals.detail.err_deal_not_found", DealNotices.ReturnTold.Owner));
        Assert.Equal("deals.detail.event_return_requested_owner", DealNotices.ReturnEventKey(deal, studio: false));
        Assert.Equal("studio.deal.event_return_requested_owner", DealNotices.ReturnEventKey(deal, studio: true));
        Assert.Equal("deals.detail.err_return_already_requested_owner", DealNotices.RefusalKeyFor("deals.detail.err_return_already_requested", deal));
    }

    /// <summary>لِكُلِّ مِفتاحٍ في العائِلَةِ بَديلُ «صاحِبُ المَتجَر» بِنَصٍّ عَرَبِيّ: يُسَمّي إدارَةَ المَتجَر، ولا يَقولُ إنّ أَحَداً أَغلَقَ حِسابَه، ولا «أُخبِرَ البائِع»، ولا يُحيلُ إلى
    /// «تَواصَل مَع البائِع». يَطبَعُ ما فَحَصَه ويَفشَلُ عَلى صِفر.</summary>
    [Fact]
    public void Every_return_key_has_an_owner_variant_that_names_the_store_management_and_closes_no_account()
    {
        var checkedKeys = 0;
        foreach (var key in DealNotices.ReturnKeys)
        {
            var text = LocaleCatalog.Find(LocaleCatalog.Arabic, key + DealNotices.OwnerVariant);
            Assert.False(string.IsNullOrWhiteSpace(text), key + DealNotices.OwnerVariant);
            Assert.Contains("إدارَةُ المَتجَر", text!, StringComparison.Ordinal);
            Assert.DoesNotContain("أَغلَقَ", text, StringComparison.Ordinal);
            Assert.DoesNotContain("أُخبِرَ البائِع", text, StringComparison.Ordinal);
            Assert.DoesNotContain("تَواصَل مَع البائِع", text, StringComparison.Ordinal);
            checkedKeys++;
        }
        output.WriteLine($"بَدائِلُ «صاحِبُ المَتجَر» مَفحوصَة: {checkedKeys}");
        Assert.Equal(4, checkedKeys);
    }

    /// <summary>إشعارُ صاحِبِ المَتجَرِ وهو طَرَفُ الصَفقَة: لِمُعَرِّفِه، بِنَوعِ الطَلَبِ الجَديد، إلى صَفحَةِ الصَفقَةِ في الاستوديو (فَيَقرَؤُه قارِئُ «العَمَلِيّات»)، بِعُنوانِ الإعلان —
    /// ولا «أَغلَقَ حِسابَه» ولا اسمَ مُشتَرٍ؛ والأُنبوبُ يَبني لِلفِعلِ السادِسِ بانِيَه.</summary>
    [Fact]
    public void The_owner_notice_is_neutral_and_points_at_the_studio_deal_page()
    {
        var l = new L(new HttpContextAccessor());
        var deal = new Deal { Id = Guid.NewGuid(), ListingTitle = "سِلعَةٌ قَبِلَها صاحِبُ المَتجَر", InitiatorId = Guid.NewGuid(), InitiatorName = "مُشتَرٍ بِاسمِه" };
        var owner = Guid.NewGuid();
        var n = DealNotices.ReturnToOwnerFor("zz", deal, owner, l, DateTime.UtcNow);
        Assert.Equal((owner, DealNotices.NewDealType, DealNotices.StudioDealUrl("zz", deal.Id)), (n.UserId, n.Type, n.RelatedUrl));
        Assert.Equal(LocaleCatalog.Find(LocaleCatalog.Arabic, "notifications.deal_return_owner.title"), n.Title);
        Assert.Contains("«سِلعَةٌ قَبِلَها صاحِبُ المَتجَر»", n.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("أَغلَقَ", n.Title + n.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(deal.InitiatorName, n.Body, StringComparison.Ordinal);
        Assert.Single(DealNotices.OwnerNoticesOf(new[] { n }, "zz"));
        var src = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Services", "Deals", "DealNotices.cs")));
        Assert.Contains("OwnerAction.ReturnToOwner => ReturnToOwnerFor(slug, deal, ownerId, l, DateTime.UtcNow),", src, StringComparison.Ordinal);
    }

    /// <summary>الشَكل: القَرارُ يَسأَلُ «صاحِبُ المَتجَر؟» قَبلَ <c>PartnerGone</c>؛ والأُنبوبُ يُخبِرُه بِفِعلِه قَبلَ سَطرِ البائِعِ الغائِب؛ والصَفحَةُ تَنزِعُ صاحِبَ المَتجَرِ بِلا حِسابٍ فيه
    /// مِن بابِ المُحادَثَةِ قَبلَ أَن تَسأَلَ الغِياب — فَلا بابٌ يُرَدُّ ولا سَطرُ «لَم يَعُد لِهذا البائِعِ حِساب».</summary>
    [Fact]
    public void The_decision_the_pipe_and_the_page_ask_for_the_store_owner_before_the_partner_gone_question()
    {
        var root = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace");
        var src = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(root, "Services", "Deals", "DealNotices.cs")));
        int At(string s, string what, int from = 0) => s.IndexOf(what, from, StringComparison.Ordinal);
        var fn = At(src, "public static ReturnTold ReturnToldFor(Guid vendorId, ACommerce.Kit.Auth.User? vendor, Guid? storeOwner)");
        var ownerFirst = At(src, "=> vendorId == storeOwner ? ReturnTold.Owner", fn);
        var gone = At(src, "AccountErasure.PartnerGone(vendor) ? ReturnTold.Vendor", fn);
        var pipe = At(src, "public async Task<bool> NotifyReturnRequestedAsync(");
        var toOwner = At(src, "if (told == ReturnTold.Owner && !await NotifyStoreOwnerAsync(slug, deal, OwnerAction.ReturnToOwner, l, ct)) told = ReturnTold.Nobody;", pipe);
        var toStore = At(src, "if (told == ReturnTold.Store && !await NotifyStoreOwnerAsync(slug, deal, OwnerAction.ReturnVendorGone, l, ct)) told = ReturnTold.Nobody;", pipe);
        output.WriteLine($"القَرار {fn} · صاحِبُ المَتجَر {ownerFirst} · الغِياب {gone} · الأُنبوب {pipe} · إشعارُه {toOwner} · البائِعُ الغائِب {toStore}");
        Assert.True(fn > 0 && ownerFirst > fn && gone > ownerFirst, "القَرارُ لا يَسأَلُ صاحِبَ المَتجَرِ قَبلَ الغِياب.");
        Assert.True(pipe > 0 && toOwner > pipe && toStore > toOwner, "الأُنبوبُ لا يُخبِرُ صاحِبَ المَتجَرِ بِفِعلِه.");

        var page = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(root, "Components", "Pages", "MyDealDetail.razor")));
        var strip = At(page, "if (vendorId is { } chatVendor && chatVendor == storeOwner && Services.Identity.AccountErasure.PartnerGone(vendorAccount)) vendorId = null;");
        var gonePage = At(page, "vendorGone = isInitiator && vendorId is not null && Services.Identity.AccountErasure.PartnerGone(vendorAccount);");
        Assert.True(strip > 0 && gonePage > strip, "الصَفحَةُ لا تَنزِعُ صاحِبَ المَتجَرِ قَبلَ سُؤالِ الغِياب.");
        Assert.True(At(page, "var storeOwner = isInitiator ? DealNotices.StoreOwnerOf(await Tenants.FindAsync(Slug)) : null;") is var so && so > 0 && so < strip);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveChatPartnerGoneStoreOwnerSellerSwitchProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static string Text(string key) => LiveChatPartnerGoneOwnerlessStoreProofTests.Text(key);

    /// <summary>قُلِبَ ما طَبَعَه التَوصيف («لِصاحِبِ المَتجَر 1 «طَلَبُ استِرجاعٍ بائِعُه أَغلَقَ حِسابَه» · return_told store · السِجِلّ «أَغلَقَ حِسابَه» True»، والتَلميحُ
    /// «أَغلَقَ البائِعُ حِسابَه»، وبِلا <c>listing_owner</c> «لَم يَعُد لِهذا البائِعِ حِساب» True). <b>المُوجَب</b> — بِمالِكِ إعلانٍ قائِمٍ وبِدونِه: التَلميحُ بَديلُ «صاحِبُ المَتجَر»
    /// ولا «أَغلَقَ»، ولا سَطرُ غِياب؛ وصاحِبُ المَتجَرِ يُخبَرُ مَرَّةً بِالنَصِّ المُحايِدِ إلى صَفحَةِ الاستوديو؛ و<c>return_told=owner</c>؛ والسِجِلُّ ورَدُّ التَكرارِ بِبَديلِهِما.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_return_on_a_deal_the_store_owner_accepted_tells_him_as_the_seller_and_nobody_closed_an_account_on_Postgres(bool listingOwnerRef)
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var seeded = await LiveChatPartnerGoneStoreOwnerSellerProofTests.SeedAsync(host.Store, listingOwnerRef);
        try
        {
            var r = await LiveChatPartnerGoneOwnerlessStoreProofTests.RunAsync(host, seeded);
            var toOwner = r.Notices.Where(n => n.UserId == seeded.Owner).ToList();
            output.WriteLine($"صاحِبُ المَتجَرِ طَرَفٌ {(listingOwnerRef ? "ومالِكُ الإعلانِ قائِم" : "بِلا مالِكِ إعلان")}: قَبلَ الطَلَب: تَلميحُ «صاحِبُ المَتجَر» {r.Before.Contains("هي الَّتي قَبِلَت هذِه الصَّفقَة")} · "
                + $"«أَغلَقَ» {r.Before.Contains("أَغلَقَ البائِعُ حِسابَه")} · «لَم يَعُد لِهذا البائِعِ حِساب» {r.Before.Contains(Text("vendors.profile.contact_gone"))} · "
                + $"بابُ المُحادَثَةِ إلى مالِكِ الإعلان {r.Before.Contains(LiveChatPartnerGoneReturnProofTests.ChatAction(seeded))} · إلى مُعَرِّفِ الاستوديو {r.Before.Contains(LiveChatPartnerGoneStoreOwnerSellerProofTests.OwnerChatAction(seeded))} · "
                + $"البابُ: {r.Status} → {r.Location} · لِصاحِبِ المَتجَر {toOwner.Count} «{toOwner.FirstOrDefault()?.Title}» · return_told {r.Deal.Refs.GetValueOrDefault(DealNotices.ReturnToldRef) ?? "غائِب"} · "
                + $"السِجِلّ «{(r.After.Contains(Text("deals.detail.event_return_requested_owner")) ? Text("deals.detail.event_return_requested_owner") : "؟")}» · "
                + $"رَدُّ التَكرار {r.After.Contains(Text("deals.detail.err_return_already_requested_owner"))} · «أَغلَقَ حِسابَه» بَعدُ {r.After.Contains("أَغلَقَ حِسابَه")}");
            Assert.Contains("هي الَّتي قَبِلَت هذِه الصَّفقَة", r.Before, StringComparison.Ordinal);
            Assert.DoesNotContain("أَغلَقَ البائِعُ حِسابَه", r.Before, StringComparison.Ordinal);
            Assert.DoesNotContain(Text("vendors.profile.contact_gone"), r.Before, StringComparison.Ordinal);
            Assert.DoesNotContain(LiveChatPartnerGoneStoreOwnerSellerProofTests.OwnerChatAction(seeded), r.Before, StringComparison.Ordinal);
            Assert.Equal(listingOwnerRef, r.Before.Contains(LiveChatPartnerGoneReturnProofTests.ChatAction(seeded), StringComparison.Ordinal));
            Assert.Equal((302, $"/{seeded.Slug}/deals/{seeded.Deal.Id}"), (r.Status, r.Location));
            var told = Assert.Single(r.Notices);
            Assert.Equal((seeded.Owner, DealNotices.StudioDealUrl(seeded.Slug, seeded.Deal.Id)), (told.UserId, told.RelatedUrl));
            Assert.Equal(Text("notifications.deal_return_owner.title"), told.Title);
            Assert.Equal(DealNotices.ReturnToldOwner, r.Deal.Refs[DealNotices.ReturnToldRef]);
            Assert.Contains(Text("deals.detail.event_return_requested_owner"), r.After, StringComparison.Ordinal);
            Assert.Contains(Text("deals.detail.err_return_already_requested_owner"), r.After, StringComparison.Ordinal);
            Assert.DoesNotContain("أَغلَقَ حِسابَه", r.After, StringComparison.Ordinal);
        }
        finally { await LiveChatPartnerGoneReturnProofTests.CleanAsync(host.Store, seeded.Slug); }
    }

    /// <summary><b>السالِب</b> — البائِعُ لَيسَ صاحِبَ المَتجَر: المَمحُوُّ في مَتجَرٍ بِصاحِبٍ يَبقى «بائِعُه أَغلَقَ حِسابَه» و<c>return_told=store</c> وسَطرُ الغِياب، والقائِمُ يُخبَرُ هُوَ بِلا مَرجِع —
    /// ولا بَديلَ «صاحِبُ المَتجَر» في أَيٍّ مِنهُما.</summary>
    [Fact]
    public async Task A_vendor_who_is_not_the_store_owner_never_reads_the_owner_variant_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var gone = await LiveChatPartnerGoneReturnProofTests.SeedAsync(host.Store, vendorGone: true);
        var live = await LiveChatPartnerGoneReturnProofTests.SeedAsync(host.Store, vendorGone: false);
        try
        {
            var g = await LiveChatPartnerGoneOwnerlessStoreProofTests.RunAsync(host, gone);
            var l = await LiveChatPartnerGoneOwnerlessStoreProofTests.RunAsync(host, live);
            var ownerVariant = Text("deals.detail.event_return_requested_owner");
            output.WriteLine($"البائِعُ المَمحُوّ: لِصاحِبِ المَتجَر «{g.Notices.FirstOrDefault(n => n.UserId == gone.Owner)?.Title}» · return_told {g.Deal.Refs.GetValueOrDefault(DealNotices.ReturnToldRef)} · "
                + $"سَطرُ الغِياب {g.Before.Contains(Text("vendors.profile.contact_gone"))} · بَديلُ «صاحِبُ المَتجَر» {g.After.Contains(ownerVariant) || g.Before.Contains("هي الَّتي قَبِلَت")}");
            output.WriteLine($"البائِعُ القائِم: لِلبائِع {l.Notices.Count(n => n.UserId == live.Vendor.Id)} · مَرجِعُ «مَن أُخبِر» {l.Deal.Refs.ContainsKey(DealNotices.ReturnToldRef)} · "
                + $"بَديلُ «صاحِبُ المَتجَر» {l.After.Contains(ownerVariant) || l.Before.Contains("هي الَّتي قَبِلَت")}");
            var told = Assert.Single(g.Notices, n => n.UserId == gone.Owner);
            Assert.Equal(Text("notifications.deal_return_vendor_gone.title"), told.Title);
            Assert.Equal(DealNotices.ReturnToldStore, g.Deal.Refs[DealNotices.ReturnToldRef]);
            Assert.Contains(Text("vendors.profile.contact_gone"), g.Before, StringComparison.Ordinal);
            Assert.DoesNotContain(ownerVariant, g.After, StringComparison.Ordinal);
            Assert.Single(l.Notices, n => n.UserId == live.Vendor.Id);
            Assert.False(l.Deal.Refs.ContainsKey(DealNotices.ReturnToldRef));
            Assert.DoesNotContain(ownerVariant, l.After, StringComparison.Ordinal);
            Assert.DoesNotContain("هي الَّتي قَبِلَت", l.Before, StringComparison.Ordinal);
        }
        finally
        {
            await LiveChatPartnerGoneReturnProofTests.CleanAsync(host.Store, gone.Slug);
            await LiveChatPartnerGoneReturnProofTests.CleanAsync(host.Store, live.Slug);
        }
    }
}
