using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Offers;
using ACommerce.Kit.Reviews;
using ACommerce.Kit.Support;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using ACommerce.Templates.Customer.Marketplace.Services.Listings;
using ACommerce.Templates.Customer.Marketplace.Services.Queries;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>الحَذفُ يَبلُغُ كُلَّ نُسخَةٍ تُعَدَّل</b> (‏2026-10-29، قَرارُ المالِك د52 (١) «أ»): دالّاتُ <see cref="AccountErasure"/> النَقِيَّةُ صَفّاً مُوجَباً وصَفّاً
/// سالِباً — ما لِصاحِبِ الحِسابِ يُمحى، وما لِغَيرِه بِاسمِه — ثُمَّ إخفاءُ الاسمِ عِندَ عَرضِ سِجِلِّ التَدقيق. والبُرهانُ الحَيُّ في
/// <see cref="LiveAccountErasureProofTests"/>.</para>
/// </summary>
public class AccountErasureTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private const string Name = "صاحِبُ الحِساب";

    private static Notification Notice(Guid inbox, string type, string title, string body, string? url)
        => new() { Id = Guid.NewGuid(), UserId = inbox, Type = type, Title = title, Body = body, RelatedUrl = url };

    // ‏(2026-10-29، مُراجَعَةُ تَيّارِ S2 الجَولَة ٢) `EraseFrom(Notification, …)` صيغَةٌ واحِدَةٌ بِوَسائِطِها السِتَّة (لا صيغَةَ تُحيلُ بِوَسيطٍ فارِغ — القاعِدَة ١):
    // الصُفوفُ القائِمَةُ تُمَرِّرُ ما لا يَعنيها فارِغاً صَراحَةً، وتَأكيداتُها بِحَرفِها.
    private static readonly string[] NoContacts = Array.Empty<string>();
    private static readonly IReadOnlySet<Guid> NoReports = new HashSet<Guid>();

    [Fact]
    public void A_notice_about_the_users_deal_or_chat_in_someone_elses_inbox_loses_the_name()
    {
        var deal = Guid.NewGuid();
        var n = Notice(Other, "deal_accepted", "قَبِلَ " + Name, Name + " قَبِلَ طَلَبَك", $"/zz/deals/{deal}");
        var chat = Notice(Other, "chat_message", "رِسالَةٌ مِن " + Name, "مَرحَباً", $"/zz/chats/{deal}");
        Assert.True(AccountErasure.EraseFrom(n, Me, new[] { Name }, new[] { $"/deals/{deal}" }, NoContacts, NoReports));
        Assert.Equal(("قَبِلَ " + AccountDeletion.ErasedName, AccountDeletion.ErasedName + " قَبِلَ طَلَبَك"), (n.Title, n.Body));
        Assert.True(AccountErasure.EraseFrom(chat, Me, new[] { Name }, new[] { $"/chats/{deal}" }, NoContacts, NoReports));
        Assert.Equal("مَرحَباً", chat.Body);
    }

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29، مُراجَعَةُ تَيّارِ S2 الجَولَة ٢)</b> — كانَ <c>A_manager_notice_naming_the_user_loses_the_name_whatever_its_link</c>:
    /// كُلُّ إشعارِ مُديرينَ يَحمِلُ اسمَه كانَ يُعَدُّ عَنه. والآن بِهُوِيَّتِه وَحدَها — وَسيلَتُه في إشعارِ التَسجيل، أَو بَلاغُه هُو (يُعلِنُ تَذكِرَتَه) — وما سِواهُما لا يُمَسّ.</summary>
    [Fact]
    public void A_manager_notice_loses_the_name_only_when_the_users_contact_or_own_report_ties_it_to_the_user()
    {
        var registered = Notice(Other, "new_user", "مُستَخدِم جَديد سَجَّل", Name, "/admin/tenants/zz/users");
        var registeredByContact = Notice(Other, "new_user", "مُستَخدِم جَديد سَجَّل", $"{Name} · {MyPhone}", "/admin/tenants/zz/users");
        var reported = Notice(Other, "report", "بَلاغ: مُخالِف", Name + " بَلَّغَ عَن إعلان", "/admin/tenants/zz/tickets");
        Assert.False(AccountErasure.EraseFrom(registered, Me, new[] { Name }, Array.Empty<string>(), new[] { MyPhone }, NoReports));
        Assert.False(AccountErasure.EraseFrom(reported, Me, new[] { Name }, Array.Empty<string>(), new[] { MyPhone }, NoReports));
        Assert.Equal((Name, Name + " بَلَّغَ عَن إعلان"), (registered.Body, reported.Body));
        Assert.True(AccountErasure.EraseFrom(registeredByContact, Me, new[] { Name }, Array.Empty<string>(), new[] { MyPhone }, NoReports));
        Assert.True(AccountErasure.EraseFrom(reported, Me, new[] { Name }, Array.Empty<string>(), new[] { MyPhone }, new HashSet<Guid> { reported.Id }));
        Assert.Equal((AccountDeletion.ErasedName, AccountDeletion.ErasedName + " بَلَّغَ عَن إعلان"), (registeredByContact.Body, reported.Body));
    }

    [Fact]
    public void A_notice_about_something_else_or_in_the_users_own_inbox_or_under_a_non_identifying_name_is_untouched()
    {
        var unrelated = Notice(Other, "deal_accepted", "قَبِلَ " + Name, "—", $"/zz/deals/{Guid.NewGuid()}");   // اسمٌ يُشبِهُه في صَفقَةٍ لَيسَت لَه
        var own = Notice(Me, "deal_accepted", "قَبِلَ " + Name, "—", "/zz/deals/x");
        var deflt = Notice(Other, "new_user", "مُستَخدِم جَديد سَجَّل", new User().FullName, "/admin/tenants/zz/users");
        Assert.False(AccountErasure.EraseFrom(unrelated, Me, new[] { Name }, new[] { "/deals/mine" }, NoContacts, NoReports));
        Assert.False(AccountErasure.EraseFrom(own, Me, new[] { Name }, new[] { "/deals/x" }, NoContacts, NoReports));
        Assert.False(AccountErasure.EraseFrom(deflt, Me, new[] { new User().FullName }, Array.Empty<string>(), NoContacts, NoReports));
        Assert.Equal("قَبِلَ " + Name, unrelated.Title);
        Assert.False(AccountErasure.Identifies(""));
        Assert.False(AccountErasure.Identifies(AccountDeletion.ErasedName));
        Assert.True(AccountErasure.Identifies(Name));
    }

    // ─── اسمُ التَسجيلِ الافتِراضِيّ، ووَسيلَةُ الاتِّصالِ في إشعارِ التَسجيلِ القَديم (مُراجَعَةُ تَيّارِ S2 الجَولَة ١، ‏2026-10-29) ───
    //
    // ‏(‏تَوصيفُه `1c651b7` — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): `Identifies` كانَ يَستَثني `new User().FullName` («مُستَخدِم جَديد») ولا يَستَثني ما يُخَزَّنُ فِعلاً
    // لِلحِسابِ الجَديدِ بِالهاتِفِ أَو البَريد: `me.profile.new_member` («عُضو جَديد»)، وإشعاراتُ المُديرينَ (`new_user`/`report`) لا يَحصُرُها رابِط — فَحَذفُ
    // حِسابٍ ما زالَ اسمُه الافتِراضِيَّ كَتَبَ «حِسابٌ مَحذوف» في إشعارِ تَسجيلِ كُلِّ عُضوٍ آخَر. وإشعارُ التَسجيلِ قَبلَ `548ea4d` مَتنُه «{الاسم} · {الهاتِف}» أَو
    // «… · {البَريد}» والاسمُ يَومَئِذٍ الافتِراضِيّ، والمَحوُ كانَ بِالاسمِ وَحدَه فَبَقِيَ الهاتِفُ والبَريد. قُلِبَ الصَفّانِ بِاسمَيهِما: الافتِراضِيُّ لا يَدُلُّ
    // عَلى أَحَد، ووَسيلَةُ الاتِّصالِ تَربِطُ إشعارَ المُديرينَ بِهُوِيَّةِ صاحِبِه.

    private static string Placeholder
        => ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, ACommerce.Kit.Auth.Server.AuthHandlers.NewMemberNameKey)!;

    private const string MyPhone = "+966500000002", MyEmail = "member@example.test";

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_deleting_a_placeholder_named_account_rewrites_another_members_registration_notice</c>: الصَفُّ السالِب.
    /// إشعارُ تَسجيلِ عُضوٍ آخَرَ بِالافتِراضِيّ (الجَديدُ بِلا وَسيلَة، والقَديمُ بِوَسيلَتِه هُو)، وبَلاغُه، لا يُمَسُّ بِحَذفِ حِسابٍ يَحمِلُ الافتِراضِيَّ نَفسَه.</summary>
    [Fact]
    public void Deleting_a_placeholder_named_account_leaves_another_members_registration_notice_untouched()
    {
        var anothers = Notice(Other, "new_user", "مُستَخدِم جَديد سَجَّل", Placeholder, "/admin/tenants/zz/users");
        var anothersOld = Notice(Other, "new_user", "مُستَخدِم جَديد سَجَّل", $"{Placeholder} · +966500000001", "/admin/tenants/zz/users");
        var anothersReport = Notice(Other, "report", "بَلاغ: سَبَب", $"{Placeholder} بَلَّغَ عَن إعلان", "/admin/tenants/zz/tickets");
        foreach (var p in AccountErasure.PlaceholderNames) Assert.False(AccountErasure.Identifies(p));
        Assert.False(AccountErasure.Identifies(" " + Placeholder + " "));
        Assert.Contains(Placeholder, AccountErasure.PlaceholderNames);
        Assert.Contains(new User().FullName, AccountErasure.PlaceholderNames);
        var mine = new[] { MyPhone };
        Assert.False(AccountErasure.EraseFrom(anothers, Me, new[] { Placeholder }, Array.Empty<string>(), mine, NoReports));
        Assert.False(AccountErasure.EraseFrom(anothersOld, Me, new[] { Placeholder }, Array.Empty<string>(), mine, NoReports));
        Assert.False(AccountErasure.EraseFrom(anothersReport, Me, new[] { Placeholder }, Array.Empty<string>(), mine, NoReports));
        Assert.Equal((Placeholder, $"{Placeholder} · +966500000001"), (anothers.Body, anothersOld.Body));
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_an_old_registration_notice_keeps_the_deleted_members_phone_and_email</c>: الصَفُّ المُوجَب.
    /// إشعارُ تَسجيلِه القَديمُ يُعرَفُ بِوَسيلَتِه — ولَو كَتَبَ اسماً بَعدَه — فَتُنزَعُ ويَصيرُ الافتِراضِيُّ في مَتنِه «حِسابٌ مَحذوف»، والعُنوانُ الثابِتُ كَما هُو.</summary>
    [Fact]
    public void An_old_registration_notice_loses_the_deleted_members_phone_or_email_and_its_name()
    {
        var byPhone = Notice(Other, "new_user", "مُستَخدِم جَديد سَجَّل", $"{Placeholder} · {MyPhone}", "/admin/tenants/zz/users");
        var byEmail = Notice(Other, "new_user", "مُستَخدِم جَديد سَجَّل", $"{Placeholder} · {MyEmail}", "/admin/tenants/zz/users");
        var contacts = new[] { MyPhone, MyEmail };
        Assert.True(AccountErasure.EraseFrom(byPhone, Me, new[] { "أَحمَدُ الفاحِص" }, Array.Empty<string>(), contacts, NoReports));
        Assert.True(AccountErasure.EraseFrom(byEmail, Me, new[] { Placeholder }, Array.Empty<string>(), contacts, NoReports));
        Assert.Equal(("مُستَخدِم جَديد سَجَّل", AccountDeletion.ErasedName), (byPhone.Title, byPhone.Body));
        Assert.Equal(("مُستَخدِم جَديد سَجَّل", AccountDeletion.ErasedName), (byEmail.Title, byEmail.Body));
        // ومَرَّةً ثانِيَةً لا شَيء: المَمحُوُّ لا يُكتَبُ مَرَّتَين.
        Assert.False(AccountErasure.EraseFrom(byPhone, Me, new[] { "أَحمَدُ الفاحِص" }, Array.Empty<string>(), contacts, NoReports));
    }

    /// <summary>والوَسيلَةُ في غَيرِ إشعارِ المُديرين، أَو في صُندوقِه هُو، لا تَربِطُ — والمَحوُ بِالوَسيلَةِ لِإشعاراتِ التَسجيلِ والبَلاغِ وَحدَها.</summary>
    [Fact]
    public void A_contact_outside_a_manager_notice_or_in_the_users_own_inbox_does_not_tie_the_notice_to_the_user()
    {
        var dealNotice = Notice(Other, "deal_accepted", "قُبِلَ طَلَبُك", $"اتَّصِل عَلى {MyPhone}", $"/zz/deals/{Guid.NewGuid()}");
        var own = Notice(Me, "new_user", "مُستَخدِم جَديد سَجَّل", $"{Placeholder} · {MyPhone}", "/admin/tenants/zz/users");
        Assert.False(AccountErasure.EraseFrom(dealNotice, Me, Array.Empty<string>(), Array.Empty<string>(), new[] { MyPhone }, NoReports));
        Assert.False(AccountErasure.EraseFrom(own, Me, Array.Empty<string>(), Array.Empty<string>(), new[] { MyPhone }, NoReports));
        Assert.Contains(MyPhone, dealNotice.Body, StringComparison.Ordinal);
    }

    /// <summary>حَدّا الوَسيلَة: تُطابِقُ بِعَينِها ولا تُطابِقُ داخِلَ رَقَمٍ أَو بَريدٍ أَطوَل — مُوجَبٌ وسالِب.</summary>
    [Fact]
    public void A_contact_matches_whole_and_never_inside_a_longer_number_or_address()
    {
        Assert.True(AccountErasure.Carries($"{Placeholder} · {MyPhone}", MyPhone));
        Assert.True(AccountErasure.Carries($"{Placeholder} · {MyEmail}.", MyEmail));
        Assert.False(AccountErasure.Carries($"{Placeholder} · {MyPhone}9", MyPhone));
        Assert.False(AccountErasure.Carries($"{Placeholder} · 1{MyPhone}", MyPhone));
        Assert.False(AccountErasure.Carries($"{Placeholder} · x{MyEmail}", MyEmail));
        Assert.False(AccountErasure.Carries($"{Placeholder} · {MyEmail}.sa", MyEmail));
        Assert.False(AccountErasure.Carries(null, MyPhone));
        var u = new User { FullName = Placeholder, Phone = " " + MyPhone + " ", Email = "" };
        Assert.Equal(new[] { MyPhone }, AccountErasure.ContactsOf(u));
        u.Email = MyEmail;
        Assert.Equal(new[] { MyPhone, MyEmail }, AccountErasure.ContactsOf(u));
    }

    // ─── إشعاراتُ المُديرينَ بِأَسماءٍ حَقيقِيَّة (مُراجَعَةُ تَيّارِ S2 الجَولَة ٢، ‏2026-10-29 — كُبرى وصُغرى) ───────────────────────
    //
    // ‏(تَوصيفُ «اليَوم» قَبلَ رَبطِ البَلاغِ بِهُوِيَّةِ صاحِبِه — يُقلَبُ كُلٌّ بِاسمِه في كوميتِ الإصلاح): إصلاحُ الجَولَةِ ١ أَخرَجَ الاسمَ الافتِراضِيّ، وبَقِيَ
    // `about = admin` صادِقاً لِكُلِّ إشعارِ مُديرين (`new_user`/`report`) أَيّاً كانَ صاحِبُه، وكُلُّ اسمٍ حَقيقِيٍّ مِن `NamesOf` يُستَبدَلُ نَصّاً داخِلَ نَصٍّ (`Replace`
    // بِلا حُدود). والبَلاغُ يَحمِلُ اسمَ المُبَلِّغِ الحَقيقِيّ («{الاسم} بَلَّغَ عَن إعلان»)؛ وعُنوانُه سَبَبُ البَلاغِ حُرّاً (لا يَتَحَقَّقُ مِنه الخادِم).

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29)</b> — كانَ <c>Today_deleting_a_member_rewrites_another_members_report_whose_name_starts_with_the_same_name</c>
    /// («عَلي حَسَن بَلَّغَ…» ← «حِسابٌ مَحذوف حَسَن بَلَّغَ…»). سالِب: بَلاغُ غَيرِه لَيسَ بَلاغَه — لا تَذكِرَةَ لَه يُعلِنُها — فَلا يُمَسّ.</summary>
    [Fact]
    public void Deleting_a_member_leaves_another_members_report_whose_name_starts_with_the_same_name_untouched()
    {
        var anothers = Notice(Other, "report", "بَلاغ: مُخالِف", "عَلي حَسَن بَلَّغَ عَن إعلان", "/admin/tenants/zz/tickets");
        Assert.False(AccountErasure.EraseFrom(anothers, Me, new[] { "عَلي" }, Array.Empty<string>(), Array.Empty<string>(), NoReports));
        Assert.Equal("عَلي حَسَن بَلَّغَ عَن إعلان", anothers.Body);
    }

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29)</b> — كانَ <c>Today_a_deleted_name_inside_a_longer_name_is_cut_out_of_a_manager_notice</c> («عَلِيّان» ← «حِسابٌ مَحذوفان»).
    /// الاسمُ كَلِمَةٌ تامَّة: يُستَبدَلُ حَيثُ هُوَ كَلِمَة (مُوجَب)، ولا يُقطَعُ مِن كَلِمَةٍ أَطوَلَ ولا مِن كَلِمَةٍ بِحَرَكَتِها بَعدَه (سالِب) — ولَو في إشعارٍ عَن صَفقَتِه.</summary>
    [Fact]
    public void A_deleted_name_is_replaced_as_a_whole_word_and_never_inside_a_longer_name()
    {
        var deal = Guid.NewGuid();
        var longer = Notice(Other, "deal_accepted", "قُبِلَ طَلَبُك", "عَلِيّان قَبِلَ طَلَبَك", $"/zz/deals/{deal}");
        Assert.False(AccountErasure.EraseFrom(longer, Me, new[] { "عَلِيّ" }, new[] { $"/deals/{deal}" }, NoContacts, NoReports));
        Assert.Equal("عَلِيّان قَبِلَ طَلَبَك", longer.Body);
        var whole = Notice(Other, "deal_accepted", "قُبِلَ طَلَبُك", "عَلِيّ قَبِلَ طَلَبَك.", $"/zz/deals/{deal}");
        Assert.True(AccountErasure.EraseFrom(whole, Me, new[] { "عَلِيّ" }, new[] { $"/deals/{deal}" }, NoContacts, NoReports));
        Assert.Equal(AccountDeletion.ErasedName + " قَبِلَ طَلَبَك.", whole.Body);
        Assert.Equal("عَليٌّ هُنا", AccountErasure.ReplaceWhole("عَليٌّ هُنا", "عَلي"));
        Assert.Equal("«" + AccountDeletion.ErasedName + "»", AccountErasure.ReplaceWhole("«عَلي»", "عَلي"));
    }

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29)</b> — كانَ <c>Today_a_same_named_members_report_is_erased_as_if_it_were_the_deleted_members</c>. النَصُّ نَفسُه
    /// لِعُضوَينِ بِاسمٍ واحِد: بَلاغُ سَمِيِّه لا يُمَسّ (سالِب)، وبَلاغُه هُو — الَّذي يُعلِنُ تَذكِرَتَه — يَصيرُ «حِسابٌ مَحذوف بَلَّغَ» (مُوجَب).</summary>
    [Fact]
    public void A_same_named_members_report_stays_and_the_deleted_members_own_report_loses_the_name()
    {
        var anothers = Notice(Other, "report", "بَلاغ: مُخالِف", "عَلي بَلَّغَ عَن إعلان", "/admin/tenants/zz/tickets");
        var mine = Notice(Other, "report", "بَلاغ: مُخالِف", "عَلي بَلَّغَ عَن إعلان", "/admin/tenants/zz/tickets");
        var own = new HashSet<Guid> { mine.Id };
        Assert.False(AccountErasure.EraseFrom(anothers, Me, new[] { "عَلي" }, Array.Empty<string>(), Array.Empty<string>(), own));
        Assert.True(AccountErasure.EraseFrom(mine, Me, new[] { "عَلي" }, Array.Empty<string>(), Array.Empty<string>(), own));
        Assert.Equal(("عَلي بَلَّغَ عَن إعلان", AccountDeletion.ErasedName + " بَلَّغَ عَن إعلان"), (anothers.Body, mine.Body));
    }

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29)</b> — كانَ <c>Today_a_report_quoting_the_deleted_members_phone_is_reattributed_to_the_deleted_account</c>. بَلاغُ غَيرِه
    /// يَقتَبِسُ هاتِفَه في سَبَبِه: يُنزَعُ الهاتِفُ وَحدَه، ويَبقى البَلاغُ لِصاحِبِه — والافتِراضِيُّ يَصيرُ «حِسابٌ مَحذوف» في إشعارِ التَسجيلِ وَحدَه.</summary>
    [Fact]
    public void A_report_quoting_the_deleted_members_phone_loses_the_phone_and_keeps_its_reporter()
    {
        var anothers = Notice(Other, "report", $"بَلاغ: اتَّصَلَ بي {MyPhone}", $"{Placeholder} بَلَّغَ عَن إعلان", "/admin/tenants/zz/tickets");
        Assert.True(AccountErasure.EraseFrom(anothers, Me, new[] { Placeholder }, Array.Empty<string>(), new[] { MyPhone }, NoReports));
        Assert.Equal(("بَلاغ: اتَّصَلَ بي", $"{Placeholder} بَلَّغَ عَن إعلان"), (anothers.Title, anothers.Body));
    }

    /// <summary>البَلاغُ يُعلِنُ تَذكِرَتَه (<see cref="ListingReport.Announces"/>): السَبَبُ نَفسُه، واسمُ كاتِبِها مَتنُه كُلُّه، ووَقتُه مِنها إلى التالِيَةِ بِالمَوضوعِ نَفسِه —
    /// مُوجَبٌ، وسالِبٌ لِكُلِّ شَرط.</summary>
    [Fact]
    public void A_report_notice_announces_the_ticket_it_follows_and_no_other()
    {
        var at = new DateTime(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);
        var ticket = new Ticket { Id = Guid.NewGuid(), AuthorId = Me, AuthorName = "عَلي", Subject = ListingReport.TicketSubject("مُخالِف"), CreatedAt = at };
        Notification N(string title, string body, DateTime when) => new()
            { Id = Guid.NewGuid(), UserId = Other, Type = ListingReport.NoticeType, Title = title, Body = body, At = when };
        var good = N(ListingReport.NoticeTitle("مُخالِف"), ListingReport.NoticeBody("عَلي"), at.AddMilliseconds(40));
        Assert.True(ListingReport.Announces(good, ticket, at.AddMinutes(5)));
        Assert.True(ListingReport.Announces(good, ticket, null));
        Assert.False(ListingReport.Announces(good, ticket, at.AddMilliseconds(10)));                                       // بَعدَ التَذكِرَةِ التالِيَة
        Assert.False(ListingReport.Announces(N(good.Title, good.Body, at.AddSeconds(-1)), ticket, null));                   // قَبلَ تَذكِرَتِه
        Assert.False(ListingReport.Announces(N(ListingReport.NoticeTitle("آخَر"), good.Body, good.At), ticket, null));      // سَبَبٌ آخَر
        Assert.False(ListingReport.Announces(N(good.Title, ListingReport.NoticeBody("عَلي حَسَن"), good.At), ticket, null)); // اسمٌ آخَر
        var support = new Ticket { AuthorId = Me, AuthorName = "عَلي", Subject = "سُؤالٌ عَن الشَحن", CreatedAt = at };
        Assert.False(ListingReport.Announces(good, support, null));                                                         // تَذكِرَةٌ لَيسَت بَلاغاً
        Assert.False(ListingReport.Announces(new Notification { Type = "new_user", Title = good.Title, Body = good.Body, At = good.At }, ticket, null));
        Assert.Equal(("تَبليغ: مُخالِف", "بَلاغ: مُخالِف", "عَلي بَلَّغَ عَن إعلان"),
                     (ListingReport.TicketSubject("مُخالِف"), ListingReport.NoticeTitle("مُخالِف"), ListingReport.NoticeBody("عَلي")));
        Assert.Equal("مُخالِف", ListingReport.ReasonOf(ticket.Subject));
        Assert.Null(ListingReport.ReasonOf(support.Subject));
    }

    /// <summary>بابُ البَلاغِ يَكتُبُ بِنُصوصِ <see cref="ListingReport"/> — فَما يَقرَؤُه المَحوُ هُوَ ما يَكتُبُه البابُ لا نُسخَةً تَنجَرِف — ونَوعُ إشعارِه <c>"report"</c>.</summary>
    [Fact]
    public void The_report_door_writes_the_ticket_and_the_manager_notice_through_ListingReport()
    {
        var src = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs")));
        var start = src.IndexOf("\"/{slug}/listings/{id:guid}/report\"", StringComparison.Ordinal);
        var end = src.IndexOf("RequireStoreWritable", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "لا بابَ بَلاغ — الأَداةُ عَمياء.");
        var body = src[start..end];
        Assert.Contains("Subject: Services.Listings.ListingReport.TicketSubject(reason),", body, StringComparison.Ordinal);
        Assert.Contains($"NotifyAdminsAsync(store, slug, \"{ListingReport.NoticeType}\",", body, StringComparison.Ordinal);
        Assert.Contains("Services.Listings.ListingReport.NoticeTitle(reason),", body, StringComparison.Ordinal);
        Assert.Contains("Services.Listings.ListingReport.NoticeBody(userName),", body, StringComparison.Ordinal);
        Assert.DoesNotContain("بَلَّغَ عَن إعلان", body, StringComparison.Ordinal);
        Assert.Contains(ListingReport.NoticeType, AccountErasure.AdminNoticeTypes);
    }

    [Fact]
    public void A_ticket_loses_its_authors_name_on_the_ticket_and_their_replies_and_the_team_reply_keeps_its_signature()
    {
        var t = new Ticket { AuthorId = Me, AuthorName = Name, Replies = new()
        {
            new Reply { AuthorName = Name, FromStaff = false },
            new Reply { AuthorName = "دَعم التَّطبيق", FromStaff = true },
        } };
        var theirs = new Ticket { AuthorId = Other, AuthorName = "غَيري" };
        Assert.True(AccountErasure.EraseFrom(t, Me));
        Assert.Equal(new[] { AccountDeletion.ErasedName, AccountDeletion.ErasedName, "دَعم التَّطبيق" },
                     new[] { t.AuthorName }.Concat(t.Replies.Select(r => r.AuthorName)));
        Assert.False(AccountErasure.EraseFrom(theirs, Me));
        Assert.Equal("غَيري", theirs.AuthorName);
    }

    // ─── التَذكِرَةُ تُمحى في لَقطَتِها وَحدَها (‏2026-10-30، تَدقيقُ الموجَة ١١ CO16) — تَوصيفٌ قَبلَ التَبديل (القاعِدَة ٣) ───────────────────
    //
    // المَقيسُ عِندَ `4ee5400`: `EraseFrom(Ticket)` يَكتُبُ «حِسابٌ مَحذوف» عَلى وَثيقَةِ الإسقاطِ (`Mark(tickets…)` ← `Store`)، ومَجرى التَذكِرَةِ لا يَحمِلُ المَحو —
    // لا حَدَثَ تَسمِيَةٍ في الكيت ولا `Apply` لَه — فَإعادَةُ بِناءِ اللَقطَةِ مِن المَجرى تُعيدُ الاسم. بِخِلافِ العَرض (`EraseEvents(Offer)` ← `OffererRenamed`).

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-30، CO16)</b> — كانَ <c>Today_a_tickets_stream_has_no_rename_event_so_only_its_snapshot_is_erased</c> (‏<c>13b1754</c>:
    /// «Apply عَلى Ticket: 3 · EraseEvents لِـ: Offer»). والآن <c>TicketAuthorRenamed</c> يُطَبِّقُه <see cref="Ticket"/> ومُصدِرُه
    /// <see cref="AccountErasure.EraseEvents(Ticket, Guid, DateTime)"/>: تَذكِرَتُه ⇒ حَدَثٌ واحِدٌ بِالاسمِ المَمحُوّ، وتَطبيقُه عَلى ما بُنِيَ مِن المَجرى يَمحو التَذكِرَةَ ورَدَّه
    /// ويُبقي رَدَّ الفَريقِ و<c>UpdatedAt</c>؛ وتَذكِرَةُ غَيرِه ومَمحُوَّتُه قَبلُ ⇒ لا حَدَث.</summary>
    [Fact]
    public void A_tickets_stream_carries_the_rename_its_author_gets_and_anothers_ticket_gets_none()
    {
        var applies = typeof(Ticket).GetMethods().Where(m => m.Name == "Apply").Select(m => m.GetParameters()[0].ParameterType.Name).OrderBy(n => n).ToList();
        var eraseEvents = typeof(AccountErasure).GetMethods().Where(m => m.Name == nameof(AccountErasure.EraseEvents)).Select(m => m.GetParameters()[0].ParameterType.Name).OrderBy(n => n).ToList();
        output.WriteLine($"Apply عَلى Ticket: {applies.Count} ({string.Join(", ", applies)}) · EraseEvents لِـ: {string.Join(", ", eraseEvents)}");
        Assert.Equal(new[] { "TicketAuthorRenamed", "TicketClosed", "TicketCreated", "TicketReplied" }, applies);
        Assert.Equal(new[] { "Offer", "Ticket" }, eraseEvents);
        // ومُصدِرُه واحِدٌ يَكتُبُ «حِسابٌ مَحذوف» — فَحَقلُه مُصَنَّفٌ `NotAPerson` في `AccountDeletionRetentionTests.PersonNameFields`.
        var emitters = EntitlementContractTests.SourceFiles()
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(WriteEndpointGuardTests.StripComments(f.Text), @"\bnew\s+(?:[\w.]+\.)?TicketAuthorRenamed\s*\("))
            .Select(f => Path.GetFileName(f.File)).ToList();
        Assert.Equal(new[] { "AccountErasure.cs" }, emitters);
        Assert.Equal(AccountDeletionRetentionTests.NameKind.NotAPerson, AccountDeletionRetentionTests.PersonNameFields["TicketAuthorRenamed.AuthorName"].Kind);

        var id = Guid.NewGuid();
        var at = new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);
        Ticket Built(Guid author, string name)
        {
            var t = new Ticket();
            t.Apply(new TicketCreated(id, author, name, "سُؤال", "نَصّ", at.AddDays(-2)));
            t.Apply(new TicketReplied(id, Guid.NewGuid(), name, false, "تَوضيح", at.AddDays(-1)));
            t.Apply(new TicketReplied(id, Guid.NewGuid(), "دَعم التَّطبيق", true, "رَدّ", at.AddDays(-1)));
            return t;
        }
        var mine = Built(Me, Name);
        var events = AccountErasure.EraseEvents(mine, Me, at);
        var renamed = Assert.IsType<TicketAuthorRenamed>(Assert.Single(events));
        Assert.Equal((id, AccountDeletion.ErasedName, at), (renamed.TicketId, renamed.AuthorName, renamed.At));

        var rebuilt = Built(Me, Name);
        var updated = rebuilt.UpdatedAt;
        rebuilt.Apply(renamed);
        Assert.Equal((AccountDeletion.ErasedName, AccountDeletion.ErasedName, "دَعم التَّطبيق", updated),
                     (rebuilt.AuthorName, rebuilt.Replies[0].AuthorName, rebuilt.Replies[1].AuthorName, rebuilt.UpdatedAt));

        var theirs = Built(Other, "غَيري");
        Assert.Empty(AccountErasure.EraseEvents(theirs, Me, at));
        Assert.Equal("غَيري", theirs.AuthorName);
        Assert.Empty(AccountErasure.EraseEvents(mine, Me, at));
    }

    [Fact]
    public void A_listing_match_loses_the_offerers_name_and_anothers_match_is_untouched()
    {
        var mine = new ListingMatch { OffererId = Me, OffererName = Name };
        var theirs = new ListingMatch { OffererId = Other, OffererName = "غَيري" };
        Assert.True(AccountErasure.EraseFrom(mine, Me));
        Assert.False(AccountErasure.EraseFrom(theirs, Me));
        Assert.Equal((AccountDeletion.ErasedName, "غَيري"), (mine.OffererName, theirs.OffererName));
    }

    [Fact]
    public void A_deal_step_another_actor_took_keeps_its_name_and_a_deal_the_user_is_not_party_to_keeps_its_address()
    {
        var deal = new Deal
        {
            InitiatorId = Other, InitiatorName = "مُشتَرٍ", CounterpartyId = Guid.NewGuid(),
            Attributes = new() { [Deal.DeliveryAddressAttribute] = "حَيٌّ" },
            Timeline = new() { new DealEvent(DealStage.Offered, DealStage.Booked, "advanced", Me, Name, null, DateTime.UtcNow) },
        };
        Assert.True(AccountErasure.EraseFrom(deal, Me));     // خُطوَتُه هُو وَحدَها
        Assert.Equal(AccountDeletion.ErasedName, deal.Timeline[0].ActorName);
        Assert.Equal(("مُشتَرٍ", "حَيٌّ"), (deal.InitiatorName, deal.Attributes[Deal.DeliveryAddressAttribute]));
        Assert.False(AccountErasure.EraseFrom(deal, Me));    // ومَرَّةً ثانِيَةً لا شَيء
    }

    [Fact]
    public void The_audit_log_hides_an_erased_actors_name_when_shown_and_keeps_anothers()
    {
        var rows = new List<AuditEntry>
        {
            new() { ActorId = Me, ActorName = Name, Action = "shipping.oto.connect" },
            new() { ActorId = Other, ActorName = "مُدير", Action = "tenant.update" },
            new() { ActorId = null, ActorName = "paypal · webhook", Action = "billing" },
        };
        var shown = AuditLogQueries.HideErasedActors(rows, new HashSet<Guid> { Me });
        Assert.Equal(new[] { AccountDeletion.ErasedName, "مُدير", "paypal · webhook" }, shown.Select(a => a.ActorName));
        Assert.Equal(new[] { "مُدير" }, AuditLogQueries.HideErasedActors(
            new List<AuditEntry> { new() { ActorId = Other, ActorName = "مُدير" } }, new HashSet<Guid>()).Select(a => a.ActorName));
    }

    // ─── سِجِلُّ التَدقيق: اسمُ العُضوِ هَدَفاً في الحاشِيَة (‏2026-10-30، تَدقيقُ الموجَة ١١ B-PRIV-4) — تَوصيفٌ قَبلَ التَبديل ───────────────────

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-30، B-PRIV-4)</b> — كانَ <c>Today_a_grant_admin_row_keeps_the_erased_members_name_in_its_note</c> (‏<c>f1cfe75</c>: «حَواشٍ بِاسمِ
    /// الهَدَف: 2 · المَعروض: … · سارة»). والآن البابانِ لا يَكتُبانِ اسمَ الهَدَف، والسَطرُ القَديمُ الَّذي يَحمِلُه تُخفى حاشِيَتُه عِندَ العَرضِ إذا حُذِفَ حِسابُه (مُوجَب)؛
    /// وحاشِيَةُ حِسابٍ قائِمٍ وحاشِيَةُ سَطرٍ هَدَفُه غَيرُ حِسابٍ — ولو كانَ مُعَرِّفُه مُعَرِّفَ المَحذوف — كَما كُتِبَتا (سالِب).</summary>
    [Fact]
    public void A_grant_admin_row_hides_the_erased_members_note_and_writes_no_name_any_more()
    {
        var mte = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs")));
        var notes = System.Text.RegularExpressions.Regex.Matches(mte, @"note:\s*user\.FullName").Count;
        var doors = System.Text.RegularExpressions.Regex.Matches(mte, @"""user\.(?:grant|revoke)_admin"", ""User"", userId\.ToString\(\), ip:").Count;
        var member = Guid.NewGuid();
        var live = Guid.NewGuid();
        var row = new AuditEntry { ActorId = Other, ActorName = "صاحِبُ المَتجَر", Action = "user.grant_admin", EntityType = "User", EntityId = member.ToString(), Note = "سارة" };
        var kept = new AuditEntry { ActorId = Other, ActorName = "صاحِبُ المَتجَر", Action = "user.revoke_admin", EntityType = "User", EntityId = live.ToString(), Note = "هِند" };
        var listing = new AuditEntry { ActorId = Other, ActorName = "صاحِبُ المَتجَر", Action = "listing.hide", EntityType = "listing", EntityId = member.ToString(), Note = "مُخالَفَة" };
        var shown = AuditLogQueries.HideErasedActors(new List<AuditEntry> { row, kept, listing }, new HashSet<Guid> { member });
        output.WriteLine($"حَواشٍ بِاسمِ الهَدَف: {notes} · بابانِ بِلا حاشِيَة: {doors} · المَعروض: {shown[0].Note} · {shown[1].Note} · {shown[2].Note}");
        Assert.Equal(0, notes);
        Assert.Equal(2, doors);
        Assert.Equal((AccountDeletion.ErasedName, "صاحِبُ المَتجَر"), (shown[0].Note, shown[0].ActorName));
        Assert.Equal(("هِند", "مُخالَفَة"), (shown[1].Note, shown[2].Note));
        Assert.Equal(member, AuditLogQueries.TargetAccount(row));
        Assert.Null(AuditLogQueries.TargetAccount(listing));
    }

    // ─── العَرضُ يُمحى في مَجراه، والقَبولُ يَرُدُّ عَرضَ مَن حُذِف (مُراجَعَةُ تَيّارِ S2 الجَولَة ٢، ‏2026-10-29) ───────────────────

    /// <summary>مُوجَب: عَرضُه المُعَلَّقُ — اسمُه في المَجرى «حِسابٌ مَحذوف» ويُسحَب؛ والمَقبولُ يُمحى اسمُه ولا يُسحَب. والأَحداثُ تُطَبَّقُ عَلى لَقطَةٍ بِالاسمِ
    /// الأَصلِيِّ (كَما يُطَبِّقُها الإسقاطُ المُباشِرُ مِن المَخزَن) فَتَصيرُ مَمحُوَّةً مَسحوبَة.</summary>
    [Fact]
    public void A_deleted_members_pending_offer_gets_the_erased_name_and_a_withdrawal_in_its_stream()
    {
        var at = DateTime.UtcNow;
        var pending = new Offer { Id = Guid.NewGuid(), OffererId = Me, OffererName = Name, Status = OfferStatus.Pending };
        var accepted = new Offer { Id = Guid.NewGuid(), OffererId = Me, OffererName = Name, Status = OfferStatus.Accepted };
        var pendingEvents = AccountErasure.EraseEvents(pending, Me, at);
        var acceptedEvents = AccountErasure.EraseEvents(accepted, Me, at);
        Assert.Equal(new object[] { new OffererRenamed(pending.Id, AccountDeletion.ErasedName, at), new OfferWithdrawn(pending.Id, at) }, pendingEvents);
        Assert.Equal(new object[] { new OffererRenamed(accepted.Id, AccountDeletion.ErasedName, at) }, acceptedEvents);

        var stored = new Offer { Id = pending.Id, OffererId = Me, OffererName = Name, Status = OfferStatus.Pending };
        foreach (var e in pendingEvents)
            switch (e) { case OffererRenamed r: stored.Apply(r); break; case OfferWithdrawn w: stored.Apply(w); break; }
        Assert.Equal((AccountDeletion.ErasedName, OfferStatus.Withdrawn), (stored.OffererName, stored.Status));
    }

    /// <summary>سالِب: عَرضُ غَيرِه لا حَدَثَ لَه، وعَرضُه المَمحُوُّ المَسحوبُ لا يُكتَبُ مَرَّتَين.</summary>
    [Fact]
    public void Anothers_offer_and_an_already_erased_withdrawn_offer_get_no_event()
    {
        var at = DateTime.UtcNow;
        var theirs = new Offer { Id = Guid.NewGuid(), OffererId = Other, OffererName = "غَيري", Status = OfferStatus.Pending };
        var done = new Offer { Id = Guid.NewGuid(), OffererId = Me, OffererName = AccountDeletion.ErasedName, Status = OfferStatus.Withdrawn };
        Assert.Empty(AccountErasure.EraseEvents(theirs, Me, at));
        Assert.Empty(AccountErasure.EraseEvents(done, Me, at));
        Assert.Equal(("غَيري", OfferStatus.Pending), (theirs.OffererName, theirs.Status));
    }

    /// <summary>مُوجَبٌ وسالِب: مَن حَذَفَ حِسابَه — أَو لا حِسابَ لَه — لا يُقبَلُ عَرضُه، والحِسابُ القائِمُ يُقبَل.</summary>
    [Fact]
    public void An_offer_whose_offerer_deleted_their_account_is_gone_and_a_live_offerers_is_not()
    {
        var gone = new User { Id = Me, FullName = AccountDeletion.ErasedName, DeletedAt = DateTime.UtcNow };
        var live = new User { Id = Other, FullName = "قائِم" };
        Assert.True(AccountErasure.OffererGone(gone));
        Assert.True(AccountErasure.OffererGone(null));
        Assert.False(AccountErasure.OffererGone(live));
    }

    /// <summary>الحارِسُ في نُقطَةِ القَبولِ بَعدَ المالِكِ وقَبلَ أَيِّ كِتابَة (القاعِدَة ٦)، وصَفحَةُ الإعلانِ تَقرَأُ رَمزَه بِنَصٍّ في القامُوس (القاعِدَتانِ ١١ و١٢).</summary>
    [Fact]
    public void The_accept_door_refuses_a_gone_offerer_after_the_owner_check_and_before_any_write_and_the_page_says_why()
    {
        var tpl = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace");
        var src = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(tpl, "MarketplaceTemplateExtensions.cs")));
        var start = src.IndexOf("\"/{slug}/offers/{id:guid}/accept\"", StringComparison.Ordinal);
        var end = src.IndexOf("RequireStoreWritable", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "لا نُقطَةَ قَبول — الأَداةُ عَمياء.");
        var body = src[start..end];
        var owner = body.IndexOf("err=not_owner", StringComparison.Ordinal);
        var gone = body.IndexOf("AccountErasure.OffererGone(await s.LoadAsync<User>(offer.OffererId))", StringComparison.Ordinal);
        var firstWrite = body.IndexOf("OfferAccepted(", StringComparison.Ordinal);
        Assert.True(owner > 0 && gone > owner && firstWrite > gone,
            $"الحارِسُ لَيسَ بَعدَ المالِكِ وقَبلَ الكِتابَة: المالِك @{owner} · الحارِس @{gone} · أَوَّلُ كِتابَة @{firstWrite}.");

        var page = File.ReadAllText(Path.Combine(tpl, "Components", "Pages", "TenantListingDetail.razor"));
        Assert.Contains("Err == ACommerce.Templates.Customer.Marketplace.Services.Identity.AccountErasure.OffererGoneCode", page, StringComparison.Ordinal);
        Assert.Contains("@L[\"listings.detail.err_offer_gone\"]", page, StringComparison.Ordinal);
        Assert.NotNull(ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "listings.detail.err_offer_gone"));
        Assert.Equal("offer_gone", AccountErasure.OffererGoneCode);
    }

    /// <summary><b>مُوجَبٌ وسالِب (‏2026-10-30، G-2)</b>: الصَفقَةُ الَّتي فَعَلَ فيها ولَيسَ طَرَفَها رابِطُها في <see cref="AccountErasure.RelatedUrls"/> فَيُمحى اسمُه مِن إشعارِها،
    /// وإشعارُ صَفقَةٍ لَم يَمَسَّها بِالاسمِ نَفسِه لا يُمَسّ.</summary>
    [Fact]
    public void A_deal_the_user_acted_on_without_being_a_party_links_its_notice_and_an_untouched_deal_does_not()
    {
        var acted = new Deal { Id = Guid.NewGuid(), InitiatorId = Other, Timeline = new() { new DealEvent(DealStage.Offered, DealStage.Offered, "cancelled", Me, Name, null, DateTime.UtcNow) } };
        var untouched = new Deal { Id = Guid.NewGuid(), InitiatorId = Other };
        var urls = AccountErasure.RelatedUrls(new[] { acted }, Array.Empty<Conversation>(), Array.Empty<ListingMatch>());
        var told = Notice(Other, DealNotices.CancelledType, "أَلغى البائِعُ طَلَبَك", Name + " أَلغى طَلَبَكَ عَلى «سِلعَة»", $"/zz/deals/{acted.Id}");
        var other = Notice(Other, DealNotices.CancelledType, "أَلغى البائِعُ طَلَبَك", Name + " أَلغى طَلَبَكَ عَلى «سِلعَة»", $"/zz/deals/{untouched.Id}");
        Assert.Equal(new[] { $"/deals/{acted.Id}" }, urls);
        Assert.True(AccountErasure.EraseFrom(told, Me, new[] { Name }, urls, NoContacts, NoReports));
        Assert.False(AccountErasure.EraseFrom(other, Me, new[] { Name }, urls, NoContacts, NoReports));
        Assert.Equal((AccountDeletion.ErasedName + " أَلغى طَلَبَكَ عَلى «سِلعَة»", Name + " أَلغى طَلَبَكَ عَلى «سِلعَة»"), (told.Body, other.Body));
    }

    // ─── إعلاناتُ مَن حَذَفَ حِسابَه (‏2026-10-30، تَدقيقُ الموجَة ١١ B-PRIV-1 + G-5) — تَوصيفٌ قَبلَ التَبديل (القاعِدَة ٣) ─────────────────────
    //
    // المَقيسُ عِندَ `fcbeec5`: `EraseCopiesAsync` تَمَسُّ الصَفقَةَ والمُحادَثَةَ والتَقييمَ والعَرضَ والمُطابَقَةَ والتَذكِرَةَ والمِفتاحَ والإشعار — ولا إعلان؛ فَإعلانُ البائِعِ
    // المَحذوفِ يَبقى في واجِهَةِ المَتجَر، تُراجِعُه السَلَّةُ فَيَبقى، والإتمامُ يَربِطُ مالِكَه المَمحُوَّ طَرَفاً مُقابِلاً (`BindListingOwnerAsync`) — والبابُ الَّذي بُنِيَ لِيَمنَعَ
    // هذا (`open_deal`، د52 «حمايةً للطرف الذي بدأ التنفيذ») لا يَراه لِأَنّ الصَفقَةَ تولَدُ بَعدَ الحَذف. صَفّا «اليَوم» يُقلَبانِ بِاسمَيهِما في كوميتِ التَبديل،
    // والصَفُّ السالِبُ (بائِعٌ قائِمٌ وإعلانٌ بِلا مالِك) يَبقى بِحَرفِه.

    private static ACommerce.Kit.Listings.Listing OwnedListing(Guid? owner, decimal price = 100m)
    {
        var listing = new ACommerce.Kit.Listings.Listing();
        var attrs = owner is { } o ? new Dictionary<string, string> { [ListingEditService.OwnerAttribute] = o.ToString() } : new();
        listing.Apply(new ACommerce.Kit.Listings.ListingCreated(Guid.NewGuid(), "zz", "سِلعَةُ البائِع", null, price, "c", null, null, attrs, DateTime.UtcNow));
        return listing;
    }

    private static ACommerce.Kit.Cart.Cart CartOf(params ACommerce.Kit.Listings.Listing[] listings)
    {
        var cart = new ACommerce.Kit.Cart.Cart { Id = Guid.NewGuid(), UpdatedAt = new DateTime(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc) };
        foreach (var l in listings)
            cart.Items.Add(new ACommerce.Kit.Cart.CartItem { ListingId = l.Id, Title = l.Title, UnitPriceSar = l.Price, Quantity = 1 });
        return cart;
    }

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-30، B-PRIV-1)</b> — كانَ <c>Today_a_deleted_sellers_listing_survives_the_cart_review_and_its_checkout_binds_the_erased_owner</c>
    /// (‏<c>0e95601</c>: المُراجَعَةُ لا تُغَيِّرُ شَيئاً و<c>BindListingOwnerAsync</c> يَربِطُ المَمحُوَّ طَرَفاً مُقابِلاً). والآن بَندُ إعلانٍ أَغلَقَ مالِكُه حِسابَه يُحذَفُ في
    /// المُراجَعَةِ (<see cref="CheckoutCart.OwnerGone"/> كَالغائِب) فَيَعودُ المُشتَري بِـ<c>cart_changed</c> ولا تولَدُ صَفقَةٌ تَربِطُه — والإعلانُ نَفسُه ما زالَ
    /// <see cref="CheckoutCart.Buyable"/> بِحالِه: القاعِدَةُ الجَديدَةُ عَن المالِكِ لا عَن الإعلان. والخِدمَةُ (<c>BindListingOwnerAsync</c>) لَم تُمَسّ: لا يَبلُغُها هذا البَند.</summary>
    [Fact]
    public async Task A_deleted_sellers_listing_leaves_the_cart_review_so_no_checkout_binds_the_erased_owner()
    {
        var world = new DocWorld();
        var seller = AccountDeletion.Erase(new User { Id = Guid.NewGuid(), TenantSlug = "zz", FullName = "بائِعُ الجَولَة" }, DateTime.UtcNow);
        var listing = OwnedListing(seller.Id);
        var cart = CartOf(listing);
        world.Put(seller).Put(listing).Put(cart);

        var reviewed = await CheckoutCart.RefreshAsync(world.Session, cart);

        Assert.True(CheckoutCart.Buyable(listing));
        Assert.True(CheckoutCart.OwnerGone(listing, seller));
        Assert.True(reviewed);
        Assert.Empty(cart.Items);
        Assert.Equal(1, world.SaveCalls);
    }

    /// <summary>مُوجَبٌ وسالِب لِـ<see cref="CheckoutCart.OwnerGone"/>: مالِكٌ حَذَفَ حِسابَه أَو لا وَثيقَةَ لَه ⇒ نَعَم؛ مالِكٌ قائِم، وإعلانٌ بِلا مالِكٍ أَو بِمالِكٍ
    /// لَيسَ مُعَرِّفاً (إعلاناتُ البَذر) ⇒ لا. والرَمزُ ثابِتٌ وصَفحَةُ الإعلانِ تَقرَؤُه بِنَصٍّ في القامُوس.</summary>
    [Fact]
    public void An_owner_gone_listing_is_one_whose_named_owner_deleted_or_never_had_an_account()
    {
        var gone = AccountDeletion.Erase(new User { Id = Guid.NewGuid(), FullName = "بائِع" }, DateTime.UtcNow);
        var live = new User { Id = Guid.NewGuid(), FullName = "قائِم" };
        var seed = OwnedListing(null);
        seed.Attributes[ListingEditService.OwnerAttribute] = "seed-vendor";
        Assert.True(CheckoutCart.OwnerGone(OwnedListing(gone.Id), gone));
        Assert.True(CheckoutCart.OwnerGone(OwnedListing(live.Id), null));
        Assert.False(CheckoutCart.OwnerGone(OwnedListing(live.Id), live));
        Assert.False(CheckoutCart.OwnerGone(OwnedListing(null), null));
        Assert.False(CheckoutCart.OwnerGone(seed, null));
        Assert.Equal("owner_gone", CheckoutCart.OwnerGoneCode);
        Assert.NotNull(ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "listings.detail.err_offer_owner_gone"));
    }

    /// <summary>مُوجَبٌ وسالِب لِـ<see cref="AccountErasure.Withdraws"/> و<see cref="AccountErasure.IncomingUnaccepted"/>: إعلانُه القائِمُ يُسحَبُ ومَحذوفُه وإعلانُ غَيرِه لا؛
    /// والوارِدَةُ الفَعّالَةُ عَلى إعلانِه بِلا طَرَفٍ مُقابِلٍ تُلغى، وما رُبِطَ بِه طَرَفاً (يَمنَعُ الحَذفَ بِـ<c>open_deal</c>) أَو انتَهى أَو لِغَيرِه لا يُمَسّ. والمُلغي
    /// المَنصَّةُ بِسُلطَةِ صاحِبِ المَتجَرِ — تُجيزُها <see cref="DealCancelAuthorization.Validate"/> عِندَ «عَرض».</summary>
    [Fact]
    public void Deletion_withdraws_only_the_users_live_listings_and_closes_only_offer_deals_he_never_accepted()
    {
        var mine = OwnedListing(Me);
        var mineDeleted = OwnedListing(Me);
        mineDeleted.Apply(new ACommerce.Kit.Listings.ListingDeleted(mineDeleted.Id, DateTime.UtcNow));
        Assert.True(AccountErasure.Withdraws(mine, Me));
        Assert.False(AccountErasure.Withdraws(mineDeleted, Me));
        Assert.False(AccountErasure.Withdraws(OwnedListing(Other), Me));
        Assert.False(AccountErasure.Withdraws(OwnedListing(null), Me));

        Deal Incoming(Guid? counterparty = null, DealStatus status = DealStatus.Active, Guid? owner = null) => new()
        {
            Id = Guid.NewGuid(), InitiatorId = Guid.NewGuid(), CounterpartyId = counterparty, Status = status, Stage = DealStage.Offered,
            Refs = new() { [DealsService.ListingOwnerRef] = (owner ?? Me).ToString() },
        };
        var open = Incoming();
        Assert.True(AccountErasure.IncomingUnaccepted(open, Me));
        Assert.False(AccountErasure.IncomingUnaccepted(Incoming(counterparty: Me), Me));
        Assert.False(AccountErasure.IncomingUnaccepted(Incoming(status: DealStatus.Cancelled), Me));
        Assert.False(AccountErasure.IncomingUnaccepted(Incoming(owner: Other), Me));
        Assert.Null(DealCancelAuthorization.Validate(open, AccountErasure.ClosingCanceller));
        Assert.Equal((Guid.Empty, DealPatternCatalog.PlatformActor, true),
            (AccountErasure.ClosingCanceller.ActorId, AccountErasure.ClosingCanceller.ActorName, AccountErasure.ClosingCanceller.IsStoreAdmin));
        Assert.NotNull(ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "account.delete.cancel_reason"));
    }

    /// <summary><b>السالِبُ — يَبقى بِحَرفِه بَعدَ التَبديل</b>: إعلانُ بائِعٍ قائِمٍ وإعلانٌ بِلا مالِكٍ (إعلاناتُ البَذر) يَمُرّانِ بِمُراجَعَةِ الإتمامِ بِلا تَغيير.</summary>
    [Fact]
    public async Task A_live_sellers_listing_and_an_ownerless_listing_survive_the_cart_review()
    {
        var world = new DocWorld();
        var seller = new User { Id = Guid.NewGuid(), TenantSlug = "zz", FullName = "بائِعٌ قائِم" };
        var live = OwnedListing(seller.Id);
        var ownerless = OwnedListing(null, 40m);
        var cart = CartOf(live, ownerless);
        world.Put(seller).Put(live).Put(ownerless).Put(cart);

        Assert.False(await CheckoutCart.RefreshAsync(world.Session, cart));
        Assert.Equal(new[] { live.Id, ownerless.Id }, cart.Items.Select(i => i.ListingId));
        Assert.Equal(0, world.SaveCalls);
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه (‏2026-10-30، B-PRIV-1)</b> — كانَ <c>Today_the_erasure_names_no_listing_and_the_offer_and_cart_doors_ask_no_owner</c> (‏<c>0e95601</c>).
    /// والآن في المَصدَر: مَحوُ النُسَخِ يُلحِقُ <c>ListingDeleted</c> ويَرفُضُ العُروضَ بِـ<c>OfferRejected</c>؛ ونُقطَةُ الحَذفِ تُعيدُ مَرَّةً مِن قِراءَةٍ جَديدَة (G-1) وتَسأَلُ
    /// الجَلسَةَ عَلَيها، ثُمَّ تُلغي الوارِدَةَ <b>بَعدَ</b> الحِفظِ وتُخبِرُ بِالأُنبوبِ القائِم؛ وبابا العَرضِ والسَلَّةِ يَسأَلانِ <c>OwnerGoneAsync</c> قَبلَ أَوَّلِ كِتابَة،
    /// والعَرضُ يَرُدُّ الإعلانَ المَحذوف.</para>
    /// </summary>
    [Fact]
    public void The_erasure_withdraws_the_listings_and_the_offer_and_cart_doors_refuse_a_gone_owner()
    {
        var violations = WithdrawalViolations(ErasureSources());
        // ‏(‏2026-10-30، الموجَة ١٢ F2 · ١) الشَكلُ وَحدَه لا يُثبِتُ أَنّ الإعادَةَ تُعيد — حَلقَةٌ تَخرُجُ عِندَ التَعارُضِ (`{ break; }`) تَمُرُّ مِنه خَضراءَ (مَقيسٌ بِحَقنِها)؛
        // فَالإعادَةُ نَفسُها يَقيسُها `LiveAccountDeletionDoorProofTests` بِجِسمِ البابِ عَلى Postgres، ويَبقى هذا لِلتَرتيبِ وأَبوابِ العَرضِ والسَلَّة.
        output.WriteLine($"مَصادِرُ مَفحوصَة: 4 · شُروطُ شَكلٍ مَعدودَة: {WithdrawalConditions} · مُخالَفات: {violations.Count} · والإعادَةُ نَفسُها: LiveAccountDeletionDoorProofTests (حَيّ)");
        Assert.True(violations.Count == 0, string.Join(" · ", violations));
    }

    /// <summary><b>والفاحِصُ يُحمِرُّ</b> (القاعِدَة ١٠): الشَكلُ عِندَ <c>0e95601</c> — بِلا سَحبٍ ولا إعادَةٍ ولا إلغاءٍ ولا سُؤالٍ عَن المالِك — بِمُخالَفاتِه كُلِّها، وإلغاءٌ قَبلَ الحِفظِ
    /// بِمُخالَفَتِه وَحدَها.</summary>
    [Fact]
    public void The_withdrawal_checker_goes_red_on_the_shape_before_and_on_a_cancel_before_the_save()
    {
        var before = WithdrawalViolations(("var deals = …; return new Counts(", "var violation = …; await session.SaveChangesAsync();",
            "var listing = await s.Events.AggregateStreamAsync<Listing>(id); s.Events.StartStream<ACommerce.Kit.Offers.Offer>(oid, ev);",
            "if (!Services.Deals.CheckoutCart.Buyable(listing)) return x; s.Store(cart);"));
        Assert.Equal(WithdrawalConditions, before.Count);
        var (copies, door, offers, cartAdd) = ErasureSources();
        var early = door.Replace("try { await session.SaveChangesAsync();", "try { await deals.CancelAsync(); await session.SaveChangesAsync();", StringComparison.Ordinal);
        Assert.Equal(new[] { "الإلغاءُ قَبلَ الحِفظ" }, WithdrawalViolations((copies, early, offers, cartAdd)));
        output.WriteLine($"عَيِّنَةُ ما قَبلُ: {before.Count} مُخالَفات · الإلغاءُ المُبَكِّر: 1");
    }

    private static (string Copies, string Door, string Offers, string CartAdd) ErasureSources()
    {
        var tpl = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace");
        var erasure = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(tpl, "Services", "Identity", "AccountErasure.cs")));
        var copies = erasure[erasure.IndexOf("public static async Task<Counts> EraseCopiesAsync(", StringComparison.Ordinal)..];
        var mte = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(tpl, "MarketplaceTemplateExtensions.cs")));
        string Door(string route)
        {
            var start = mte.IndexOf(route, StringComparison.Ordinal);
            Assert.True(start > 0, $"لا بابَ «{route}» — الأَداةُ عَمياء.");
            return mte[start..mte.IndexOf("RequireStoreWritable", start, StringComparison.Ordinal)];
        }
        return (copies, AccountDeletionRetentionTests.DeleteEndpointBody(), Door("app.MapPost(\"/{slug}/listings/{id:guid}/offers\""),
            Door("app.MapPost(\"/{slug}/listings/{id:guid}/cart/add\""));
    }

    /// <summary>شُروطُ الشَكلِ الَّتي يَعُدُّها <see cref="WithdrawalViolations"/> — كُلُّها تُحمِرُّ عَلى شَكلِ <c>0e95601</c> (العَيِّنَةُ المَحقونَة).</summary>
    private const int WithdrawalConditions = 7;

    private static List<string> WithdrawalViolations((string Copies, string Door, string Offers, string CartAdd) src)
    {
        var v = new List<string>();
        int At(string text, string what) => text.IndexOf(what, StringComparison.Ordinal);
        if (At(src.Copies, "new ListingDeleted(") < 0) v.Add("المَحوُ لا يَسحَبُ الإعلانات");
        if (At(src.Copies, "new OfferRejected(") < 0) v.Add("المَحوُ لا يَرفُضُ العُروض");
        if (At(src.Door, "for (var attempt = 1; ; attempt++)") < 0 || At(src.Door, "catch (JasperFx.ConcurrencyException) when (attempt < ACommerce.Kit.Auth.Server.AccountWrites.Attempts)") < 0)
            v.Add("الحَذفُ لا يُعادُ مِن قِراءَةٍ جَديدَة");
        if (At(src.Door, "Services.SessionVersionGate.StillOpen(user, token)") < 0) v.Add("الحَذفُ لا يَسأَلُ الجَلسَةَ عَلى القِراءَة");
        var save = At(src.Door, "await session.SaveChangesAsync();");
        var cancel = At(src.Door, "deals.CancelAsync(");
        if (cancel < 0 || At(src.Door, "notices.NotifyCancelledAsync(") < 0) v.Add("الوارِدَةُ لا تُلغى ولا يُخبَرُ أَصحابُها");
        else if (save < 0 || cancel < save) v.Add("الإلغاءُ قَبلَ الحِفظ");
        var offerGone = At(src.Offers, "CheckoutCart.OwnerGoneAsync(s, listing)");
        var offerWrite = At(src.Offers, "s.Events.StartStream<ACommerce.Kit.Offers.Offer>(");
        if (offerGone < 0 || offerWrite < offerGone || At(src.Offers, "listing.IsDeleted") < 0) v.Add("بابُ العَرضِ لا يَرُدُّ مالِكاً غائِباً أَو إعلاناً مَحذوفاً قَبلَ الكِتابَة");
        var cartGone = At(src.CartAdd, "CheckoutCart.OwnerGoneAsync(s, listing)");
        var cartWrite = At(src.CartAdd, "s.Store(cart);");
        if (cartGone < 0 || cartWrite < cartGone) v.Add("بابُ السَلَّةِ لا يَرُدُّ مالِكاً غائِباً قَبلَ الكِتابَة");
        return v;
    }

    // ─── رَدٌّ نَقدِيٌّ مُستَحَقٌّ لِمَن يَحذِفُ حِسابَه (‏2026-10-30، تَدقيقُ الموجَة ١١ G-7) — تَوصيفٌ قَبلَ التَبديل (القاعِدَة ٣) ─────────────────────
    //
    // المَقيسُ عِندَ `c644737`: صَفقَةُ نَقدٍ أُلغِيَت بَعدَ القَبضِ يَكتُبُ إلغاؤها `cash_refund_due` (البائِعُ يَرُدُّ الثَمَن) — ولا كاتِبَ لِـ«رُدَّ» (S3 OI1)؛ وصَفحَةُ الحَذفِ
    // لا تَسأَلُ عَنه: لا تَعُدُّه `open_deal` (المُلغاةُ لَيسَت جارِيَة) ولا تَذكُرُه، فَيَحذِفُ المُشتَري حِسابَه — ومَعَه الوَسيلَةُ الَّتي يَرُدُّ البائِعُ بِها — ولا يُقالُ لَه.

    private static readonly string DeletePagePath = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "DeleteAccount.razor");

    private static Deal CashDeal(Guid buyer, DealStatus status, params string[] lines)
    {
        var d = new Deal { Id = Guid.NewGuid(), InitiatorId = buyer, CounterpartyId = Other, CounterpartyName = "بائِعُ النَقد",
            ListingTitle = "سِلعَةٌ نَقدِيَّة", AmountSar = 250m, Status = status };
        foreach (var a in lines) d.Timeline.Add(new(DealStage.Delivered, DealStage.Delivered, a, Other, "بائِعُ النَقد", null, DateTime.UtcNow));
        return d;
    }

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-30، G-7)</b> — كانَ <c>Today_the_deletion_page_says_nothing_of_cash_the_vendor_still_owes_back</c> (‏<c>228b1be</c>:
    /// دالّاتُ «رَدٍّ مُستَحَقّ» في المَحو 0، وصَفحَةُ الحَذفِ لا تَذكُرُه). والآن تَسأَلُ الصَفحَةُ <see cref="AccountErasure.CashRefundsOwedAsync"/> بِاستِعلامِ «بَياناتي»،
    /// وتَرسُمُ كُلَّ صَفقَةٍ (البائِع، السِلعَة، المَبلَغ) بِجُملَةِ الوَسيلَة — <b>في فَرعِ الاستِمارَةِ وقَبلَها</b>: تَحذيرٌ يُقرَأُ قَبلَ التَأكيدِ لا مَنعٌ يَرُدُّه.
    /// والمُلغاةُ ما زالَت غَيرَ جارِيَة — لا تَدخُلُ <c>open_deal</c>.</summary>
    [Fact]
    public void The_deletion_page_warns_of_cash_the_vendor_still_owes_back_before_the_confirm_and_does_not_block_it()
    {
        var page = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(DeletePagePath));
        var owed = typeof(AccountErasure).GetMethods().Count(m => m.Name.StartsWith("CashRefund", StringComparison.Ordinal));
        var load = page.IndexOf("owedCash = await Data.CashRefundsOwedAsync(Slug, Auth.UserId.Value);", StringComparison.Ordinal);
        var openBranch = page.IndexOf("@if (openDeals > 0)", StringComparison.Ordinal);
        var warn = page.IndexOf("@if (owedCash.Count > 0)", StringComparison.Ordinal);
        var form = page.IndexOf("/me/delete/confirm", StringComparison.Ordinal);
        var keys = new[] { "account.delete.cash_refund_title", "account.delete.cash_refund_lead", "account.delete.cash_refund_row", "account.delete.cash_refund_vendor" };
        output.WriteLine($"دالّاتُ «رَدٍّ مُستَحَقّ» في المَحو: {owed} · التَحذيرُ قَبلَ الاستِمارَةِ في فَرعِها: {openBranch < warn && warn < form} · مَفاتيحُ في القامُوس: " +
            $"{keys.Count(k => ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, k) is not null)}/{keys.Length}");

        Assert.Equal(3, owed);
        Assert.True(load > 0, "الصَفحَةُ لا تَسأَلُ الاستِعلام.");
        Assert.True(openBranch > 0 && warn > openBranch && form > warn && page.IndexOf("else", openBranch, StringComparison.Ordinal) < warn,
            "التَحذيرُ لَيسَ في فَرعِ الاستِمارَةِ قَبلَها.");
        foreach (var k in keys)
        {
            Assert.Contains(k, page, StringComparison.Ordinal);
            Assert.NotNull(ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, k));
        }
        Assert.Contains("d.CounterpartyName, d.ListingTitle, d.AmountSar", page, StringComparison.Ordinal);
        Assert.False(AccountErasure.IsOpen(new Deal { Status = DealStatus.Cancelled }));
        var l = new ACommerce.Platform.I18n.L(new Microsoft.AspNetCore.Http.HttpContextAccessor());
        Assert.Equal("«سِلعَة» — 250 ريال، يَرُدُّها بائِع", l.Format("account.delete.cash_refund_row", "بائِع", "سِلعَة", 250m));
    }

    /// <summary>مُوجَبٌ وسالِبٌ لِـ<see cref="AccountErasure.CashRefundOwed"/>: مُلغاةٌ آخِرُ سَطرِ مالِها «رَدٌّ نَقدِيٌّ مُستَحَقّ» ⇒ نَعَم. وغَيرُ مُلغاةٍ، أَو لِغَيرِه
    /// مُشتَرِياً (هُوَ البائِعُ فيها)، أَو مُلغاةٌ بِلا قَبض، أَو أُعيدَ مالُها إلى البِطاقَة (<c>gateway_refund_due</c>/<c>refunded</c> آخِراً) ⇒ لا. وسَطرٌ لا مالَ فيه
    /// بَعدَ «المُستَحَقّ» (مُلاحَظَة) لا يَحجُبُه.</summary>
    [Fact]
    public void A_cash_refund_is_owed_to_the_buyer_of_a_cancelled_deal_whose_latest_money_line_says_so_and_not_otherwise()
    {
        var owed = CashDeal(Me, DealStatus.Cancelled, DealEvent.CashOnDelivery, "cancelled", DealEvent.CashRefundDue);
        var noteAfter = CashDeal(Me, DealStatus.Cancelled, DealEvent.CashOnDelivery, DealEvent.CashRefundDue, DealEvent.RefNote);
        var notOwed = CashDeal(Me, DealStatus.Cancelled, "cancelled");
        var active = CashDeal(Me, DealStatus.Active, DealEvent.CashOnDelivery, DealEvent.CashRefundDue);
        var asSeller = CashDeal(Other, DealStatus.Cancelled, DealEvent.CashOnDelivery, DealEvent.CashRefundDue);
        asSeller.CounterpartyId = Me;
        var card = CashDeal(Me, DealStatus.Cancelled, DealEvent.GatewayPaid, "cancelled", DealEvent.GatewayRefundDue);
        var cardRefunded = CashDeal(Me, DealStatus.Cancelled, DealPaymentLines.Captured, "cancelled", DealPaymentLines.Refunded);
        var cashThenCard = CashDeal(Me, DealStatus.Cancelled, DealEvent.CashRefundDue, DealPaymentLines.Refunded);

        Assert.True(AccountErasure.CashRefundOwed(owed, Me));
        Assert.True(AccountErasure.CashRefundOwed(noteAfter, Me));
        Assert.False(AccountErasure.CashRefundOwed(notOwed, Me));
        Assert.False(AccountErasure.CashRefundOwed(active, Me));
        Assert.False(AccountErasure.CashRefundOwed(asSeller, Me));
        Assert.False(AccountErasure.CashRefundOwed(card, Me));
        Assert.False(AccountErasure.CashRefundOwed(cardRefunded, Me));
        Assert.False(AccountErasure.CashRefundOwed(cashThenCard, Me));
        Assert.Equal(new[] { owed.Id, noteAfter.Id }.OrderBy(i => i),
            AccountErasure.CashRefundsOwed(new[] { owed, noteAfter, notOwed, active, asSeller, card, cardRefunded, cashThenCard }, Me).Select(d => d.Id).OrderBy(i => i));
    }

    private readonly ITestOutputHelper output;
    public AccountErasureTests(ITestOutputHelper output) => this.output = output;
}

/// <summary>
/// <para><b>بُرهانُ د52 حَيّاً عَلى Postgres</b> (‏<c>WASAYEL_LIVE_PROOF=1</c>): تَسلسُلُ نُقطَةِ الحَذفِ بِعَينِه — البَوّابَةُ بِعَدِّ الصَفَقاتِ الجارِيَة، ثُمَّ
/// <c>Erase</c> و<c>Store</c> وإبطالُ المَفاتيحِ و<see cref="AccountErasure.EraseCopiesAsync"/> وحِفظٌ واحِد — عَلى مَخزَنٍ بِتَركيبِ المُضيف (لَقَطاتُ العَرضِ
/// والتَذكِرَةِ والإعلان، وتَسجيلُ الصَفقَة، ومَفاتيحُ الرَبطِ عامَّة). ما لا يُقاسُ في الذاكِرَة: <c>Timeline.Any</c> و<c>Title.Contains</c> وقِراءَةُ وَثيقَةٍ عامَّةٍ
/// مِن جَلسَةِ مُستَأجِر — وأَنَّ الحِفظَ الواحِدَ لا يَكتُبُ مِفتاحاً مَرَّتَين.</para>
/// </summary>
[Collection(LiveProofCollection.Name)]
public class LiveAccountErasureProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static DocumentStore BuildStore() => DocumentStore.For(o =>
    {
        o.Connection(LiveDealConcurrencyProofTests.ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120");
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
        o.Schema.For<ApiKeyDocument>().SingleTenanted().Identity(x => x.Id);
        o.Projections.Snapshot<ACommerce.Kit.Listings.Listing>(global::JasperFx.Events.Projections.SnapshotLifecycle.Inline);
        o.Projections.Snapshot<Ticket>(global::JasperFx.Events.Projections.SnapshotLifecycle.Inline);
        o.Projections.Snapshot<Offer>(global::JasperFx.Events.Projections.SnapshotLifecycle.Inline);
        DealsService.RegisterDocument(o);
        o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
    });

    [Fact]
    public async Task Deletion_refuses_an_open_deal_then_erases_every_editable_copy_in_one_save_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = BuildStore();
        var slug = "zz-erase-" + Guid.NewGuid().ToString("N")[..8];
        const string name = "صاحِبُ الحِسابِ الحَيّ";
        var me = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = name, Phone = "966500000552" };
        var other = Guid.NewGuid();
        var open = new Deal { Id = Guid.NewGuid(), TenantSlug = slug, InitiatorId = me.Id, InitiatorName = name, CounterpartyId = other, Status = DealStatus.Active };
        var bought = new Deal { Id = Guid.NewGuid(), TenantSlug = slug, InitiatorId = me.Id, InitiatorName = name, CounterpartyId = other, CounterpartyName = "بائِع",
            Status = DealStatus.Completed, Attributes = new() { [Deal.DeliveryAddressAttribute] = "حَيُّ المِثال", [Deal.DeliveryPhoneAttribute] = "0500000552" },
            Timeline = new() { new DealEvent(DealStage.Offered, DealStage.Offered, "created", me.Id, name, null, DateTime.UtcNow) } };
        var sold = new Deal { Id = Guid.NewGuid(), TenantSlug = slug, InitiatorId = other, InitiatorName = "مُشتَرٍ", CounterpartyId = me.Id, CounterpartyName = name,
            Status = DealStatus.Cancelled, Attributes = new() { [Deal.DeliveryAddressAttribute] = "حَيُّ المُشتَري" } };
        var conv = new Conversation { Id = Guid.NewGuid(), OwnerId = other, OwnerName = "بائِع", PartnerId = me.Id, PartnerName = name };
        var review = new Review { Id = Guid.NewGuid(), AuthorId = me.Id, AuthorName = name, TargetUserId = other, TargetUserName = "بائِع" };
        var offer = new Offer { Id = Guid.NewGuid(), OffererId = me.Id, OffererName = name, Price = 10m };
        var match = new ListingMatch { Id = Guid.NewGuid(), OffererId = me.Id, OffererName = name };
        var ticket = new Ticket { Id = Guid.NewGuid(), AuthorId = me.Id, AuthorName = name, Subject = "سُؤال" };
        var aboutMe = new Notification { Id = Guid.NewGuid(), UserId = other, Type = "deal_accepted", Title = "قَبِلَ " + name, Body = "—", RelatedUrl = $"/{slug}/deals/{bought.Id}" };
        var sameNameElsewhere = new Notification { Id = Guid.NewGuid(), UserId = other, Type = "deal_accepted", Title = "قَبِلَ " + name, Body = "—", RelatedUrl = $"/{slug}/deals/{Guid.NewGuid()}" };
        var audit = new AuditEntry { Id = Guid.NewGuid(), Scope = slug, ActorId = me.Id, ActorName = name, Action = "shipping.oto.connect" };
        var keys = new ApiKeyService(store);
        var issued = new List<string>();
        try
        {
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(me); s.Store(open, bought, sold); s.Store(conv); s.Store(review); s.Store(offer); s.Store(match); s.Store(ticket);
                s.Store(aboutMe, sameNameElsewhere); s.Store(audit);
                await s.SaveChangesAsync();
            }
            var active = await keys.IssueAsync(slug, new ApiKeyValidator.IssueRequest("رَبط", me.Id, name, new[] { ApiScopeCatalog.DealsRead }, null), Guid.NewGuid());
            var old = await keys.IssueAsync(slug, new ApiKeyValidator.IssueRequest("قَديم", me.Id, name, new[] { ApiScopeCatalog.DealsRead }, null), Guid.NewGuid());
            issued.AddRange(new[] { active.Key.Id, old.Key.Id });
            await keys.RevokeAsync(slug, old.Key.Id);

            // ١) صَفقَةٌ جارِيَة ⇒ يُرَدّ، ولا شَيءَ يُكتَب.
            await using (var s = store.LightweightSession(slug))
            {
                var u = await s.LoadAsync<User>(me.Id);
                var refused = AccountDeletion.Validate(u, await AccountErasure.OpenDealsAsync(s, me.Id), "حذف", "حذف");
                output.WriteLine($"بِصَفقَةٍ جارِيَة: {refused?.Code}");
                Assert.Equal(AccountDeletion.OpenDeal, refused?.Code);
            }

            // ٢) انتَهَت ⇒ تَسلسُلُ النُقطَةِ بِعَينِه.
            await using (var s = store.LightweightSession(slug))
            {
                // تُقرَأُ الصَفقَةُ قَبلَ كِتابَتِها — الوَثيقَةُ بِتَزامُنٍ تَفاؤُلِيٍّ (ADR-036)، فَلا تُكتَبُ نُسخَةٌ لَم تُقرَأ.
                var d = await s.LoadAsync<Deal>(open.Id);
                d!.Status = DealStatus.Completed;
                s.Store(d);
                await s.SaveChangesAsync();
            }
            AccountErasure.Counts counts;
            await using (var s = store.LightweightSession(slug))
            {
                var u = await s.LoadAsync<User>(me.Id);
                Assert.Null(AccountDeletion.Validate(u, await AccountErasure.OpenDealsAsync(s, me.Id), "حذف", "حذف"));
                var now = DateTime.UtcNow;
                var nameBefore = u!.FullName;
                var contactsBefore = AccountErasure.ContactsOf(u);   // ‏(2026-10-29، الجَولَة ٢) الصيغَةُ الواحِدَة — وَسيلَتاه قَبلَ Erase كَالنُقطَة
                AccountDeletion.Erase(u, now);
                s.Store(u);
                await ApiKeyService.RevokeActorKeysAsync(s, slug, me.Id, now);
                counts = await AccountErasure.EraseCopiesAsync(s, slug, me.Id, new[] { nameBefore }, contactsBefore);
                await s.SaveChangesAsync();
            }
            output.WriteLine($"مُحِيَ: صَفَقات {counts.Deals} · مُحادَثات {counts.Conversations} · تَقييمات {counts.Reviews} · عُروض {counts.Offers} · مُطابَقات {counts.Matches} · تَذاكِر {counts.Tickets} · مَفاتيح {counts.Keys} · إشعارات {counts.Notifications}");

            await using var q = store.QuerySession(slug);
            var x = AccountDeletion.ErasedName;
            var b = await q.LoadAsync<Deal>(bought.Id);
            var so = await q.LoadAsync<Deal>(sold.Id);
            Assert.Equal((x, "بائِع", false, false), (b!.InitiatorName, b.CounterpartyName, b.Attributes.ContainsKey(Deal.DeliveryAddressAttribute), b.Attributes.ContainsKey(Deal.DeliveryPhoneAttribute)));
            Assert.Equal(x, Assert.Single(b.Timeline).ActorName);
            Assert.Equal((x, "مُشتَرٍ", "حَيُّ المُشتَري"), (so!.CounterpartyName, so.InitiatorName, so.Attributes[Deal.DeliveryAddressAttribute]));
            Assert.Equal((x, "بائِع"), ((await q.LoadAsync<Conversation>(conv.Id))!.PartnerName, (await q.LoadAsync<Conversation>(conv.Id))!.OwnerName));
            Assert.Equal(x, (await q.LoadAsync<Review>(review.Id))!.AuthorName);
            Assert.Equal(x, (await q.LoadAsync<Offer>(offer.Id))!.OffererName);
            Assert.Equal(x, (await q.LoadAsync<ListingMatch>(match.Id))!.OffererName);
            Assert.Equal(x, (await q.LoadAsync<Ticket>(ticket.Id))!.AuthorName);
            Assert.Equal("قَبِلَ " + x, (await q.LoadAsync<Notification>(aboutMe.Id))!.Title);
            Assert.Equal("قَبِلَ " + name, (await q.LoadAsync<Notification>(sameNameElsewhere.Id))!.Title);
            Assert.Equal(name, (await q.LoadAsync<AuditEntry>(audit.Id))!.ActorName);                                  // في المَخزَنِ كَما كُتِب
            Assert.Equal(x, (await new AuditLogQueries(store).RecentAsync(slug)).Single(a => a.Id == audit.Id).ActorName); // ويُخفى عِندَ العَرض
            var docs = await q.Query<ApiKeyDocument>().Where(k => k.TenantSlug == slug).ToListAsync();
            Assert.All(docs, k => Assert.Equal((ApiKeyDocument.StatusRevoked, x), (k.Status, k.ActorName)));
            Assert.Equal(2, docs.Count);
            Assert.Equal((3, 1, 1, 1, 1, 1, 1, 1), (counts.Deals, counts.Conversations, counts.Reviews, counts.Offers, counts.Matches, counts.Tickets, counts.Keys, counts.Notifications));
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            s.DeleteWhere<User>(u => u.Id != Guid.Empty); s.DeleteWhere<Deal>(d => d.Id != Guid.Empty); s.DeleteWhere<Conversation>(c => c.Id != Guid.Empty);
            s.DeleteWhere<Review>(r => r.Id != Guid.Empty); s.DeleteWhere<Offer>(o => o.Id != Guid.Empty); s.DeleteWhere<ListingMatch>(m => m.Id != Guid.Empty);
            s.DeleteWhere<Ticket>(t => t.Id != Guid.Empty); s.DeleteWhere<Notification>(n => n.Id != Guid.Empty); s.DeleteWhere<AuditEntry>(a => a.Id != Guid.Empty);
            foreach (var id in issued) s.Delete<ApiKeyDocument>(id);
            await s.SaveChangesAsync();
        }
    }

    /// <summary>
    /// <para><b>‏(2026-10-29، مُراجَعَةُ تَيّارِ S2 الجَولَة ١) إشعاراتُ المُديرينَ تُربَطُ بِهُوِيَّةِ المَحذوفِ لا بِنَصِّ اسمِه — حَيّاً</b>: ما لا يُقاسُ في الذاكِرَة —
    /// <c>Body.Contains</c> بِالوَسيلَةِ (‏<c>+</c> و<c>@</c> في <c>LIKE</c>) والحَدّانِ بَعدَه. حِسابٌ ما زالَ اسمُه الافتِراضِيّ «عُضو جَديد» وإشعارُ تَسجيلِه
    /// القَديمُ «{الاسم} · {الهاتِف}»، وإشعارا تَسجيلِ عُضوَينِ آخَرَينِ (الجَديدُ بِلا وَسيلَة، والقَديمُ بِهاتِفِه هُو). تَسلسُلُ النُقطَةِ بِعَينِه.</para>
    /// </summary>
    [Fact]
    public async Task A_placeholder_named_deletion_erases_its_own_registration_notice_by_contact_and_no_one_elses_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = BuildStore();
        var slug = "zz-erase-" + Guid.NewGuid().ToString("N")[..8];
        var placeholder = ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, ACommerce.Kit.Auth.Server.AuthHandlers.NewMemberNameKey)!;
        const string myPhone = "+966500000553", theirPhone = "+966500000554";
        var me = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = placeholder, Phone = myPhone };
        var admin = Guid.NewGuid();
        Notification Registered(string body) => new()
            { Id = Guid.NewGuid(), UserId = admin, Type = "new_user", Title = "مُستَخدِم جَديد سَجَّل", Body = body, RelatedUrl = $"/admin/tenants/{slug}/users" };
        var mine = Registered($"{placeholder} · {myPhone}");
        var theirsNew = Registered(placeholder);
        var theirsOld = Registered($"{placeholder} · {theirPhone}");
        try
        {
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(me); s.Store(mine, theirsNew, theirsOld);
                await s.SaveChangesAsync();
            }
            AccountErasure.Counts counts;
            await using (var s = store.LightweightSession(slug))
            {
                var u = await s.LoadAsync<User>(me.Id);
                Assert.Null(AccountDeletion.Validate(u, await AccountErasure.OpenDealsAsync(s, me.Id), "حذف", "حذف"));
                var nameBefore = ActorName.NamesOf(u!);
                var contactsBefore = AccountErasure.ContactsOf(u!);
                AccountDeletion.Erase(u!, DateTime.UtcNow);
                s.Store(u!);
                counts = await AccountErasure.EraseCopiesAsync(s, slug, me.Id, nameBefore, contactsBefore);
                await s.SaveChangesAsync();
            }
            await using var q = store.QuerySession(slug);
            var (m, tn, to) = ((await q.LoadAsync<Notification>(mine.Id))!, (await q.LoadAsync<Notification>(theirsNew.Id))!, (await q.LoadAsync<Notification>(theirsOld.Id))!);
            output.WriteLine($"إشعاراتٌ مَمحُوَّة: {counts.Notifications} · إشعارُه بِلا وَسيلَتِه: {!m.Body.Contains(myPhone, StringComparison.Ordinal)} · " +
                             $"إشعارا غَيرِه كَما كُتِبا: {tn.Body == placeholder && to.Body == $"{placeholder} · {theirPhone}"}");
            Assert.Equal(1, counts.Notifications);
            Assert.Equal(("مُستَخدِم جَديد سَجَّل", AccountDeletion.ErasedName), (m.Title, m.Body));
            Assert.Equal(placeholder, tn.Body);
            Assert.Equal($"{placeholder} · {theirPhone}", to.Body);
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            s.DeleteWhere<User>(u => u.Id != Guid.Empty); s.DeleteWhere<Notification>(n => n.Id != Guid.Empty);
            await s.SaveChangesAsync();
        }
    }

    // ─── مَجرى الأَحداثِ بَعدَ الحَذف (مُراجَعَةُ تَيّارِ S2 الجَولَة ٢، ‏2026-10-29) ───────────────────────────────────────────────
    //
    // ما لا تَراه الصُفوفُ أَعلاه: التَذكِرَةُ والعَرضُ فيهِما وَثيقَتانِ مُخَزَّنَتانِ بِلا مَجرى، والصَفحاتُ والنِقاطُ تَبني مِن المَجرى. فَهُنا يُبدَأُ
    // المَجرى كَما تَبدَؤُه نُقطَتاه (‏`StartStream` بِـ`TicketCreated` و`OfferSubmitted`) ويُقرَأُ بَعدَ الحَذفِ كَما يَقرَؤُه قارِئوه.

    /// <summary>تَسلسُلُ نُقطَةِ الحَذفِ بِعَينِه — الأَسماءُ والوَسيلَتانِ قَبلَ <c>Erase</c>، ثُمَّ المَحوُ وحِفظٌ واحِد.</summary>
    private static async Task<AccountErasure.Counts> EraseLikeTheDoorAsync(DocumentStore store, string slug, Guid userId)
    {
        await using var s = store.LightweightSession(slug);
        var u = await s.LoadAsync<User>(userId);
        Assert.Null(AccountDeletion.Validate(u, await AccountErasure.OpenDealsAsync(s, userId), "حذف", "حذف"));
        var names = ActorName.NamesOf(u!);
        var contacts = AccountErasure.ContactsOf(u!);
        AccountDeletion.Erase(u!, DateTime.UtcNow);
        s.Store(u!);
        var counts = await AccountErasure.EraseCopiesAsync(s, slug, userId, names, contacts);
        await s.SaveChangesAsync();
        return counts;
    }

    /// <summary>
    /// <para><b>البَلاغُ بِتَذكِرَتِه لا بِنَصِّ اسمِه — حَيّاً</b> (‏2026-10-29، مُراجَعَةُ تَيّارِ S2 الجَولَة ٢): ثَلاثَةُ مُبَلِّغينَ بِالسَبَبِ نَفسِه — المَحذوفُ «عَلي»، وسَمِيُّه
    /// «عَلي» بَعدَه، و«عَلي حَسَن» بَعدَهُما — ومُديرانِ يَصِلُ كُلًّا مِنهُما إشعارُ كُلِّ بَلاغ؛ وبَلاغٌ رابِعٌ مِن «عُضو جَديد» يَقتَبِسُ هاتِفَ المَحذوفِ في سَبَبِه. ما لا يُقاسُ
    /// في الذاكِرَة: استِعلامُ <c>Title == … &amp;&amp; Body == … &amp;&amp; At &gt;= …</c> والتَذكِرَةُ التالِيَةُ بِالمَوضوعِ نَفسِه عَلى Postgres.</para>
    /// </summary>
    [Fact]
    public async Task A_deleted_members_report_notices_lose_the_name_and_a_same_named_or_longer_named_members_stay_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = BuildStore();
        var slug = "zz-erase-" + Guid.NewGuid().ToString("N")[..8];
        const string myPhone = "+966500000557";
        var placeholder = ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, ACommerce.Kit.Auth.Server.AuthHandlers.NewMemberNameKey)!;
        var me = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "عَلي", Phone = myPhone };
        var reporters = new[] { (Id: me.Id, Name: "عَلي", Reason: "مُخالِف"), (Id: Guid.NewGuid(), Name: "عَلي", Reason: "مُخالِف"),
                                (Id: Guid.NewGuid(), Name: "عَلي حَسَن", Reason: "مُخالِف"), (Id: Guid.NewGuid(), Name: placeholder, Reason: $"اتَّصَلَ بي {myPhone}") };
        var admins = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var notices = new List<(int Reporter, Guid Id)>();
        var streams = new List<Guid>();
        try
        {
            await using (var s = store.LightweightSession(slug)) { s.Store(me); await s.SaveChangesAsync(); }
            var at = DateTime.UtcNow.AddMinutes(-10);
            for (var i = 0; i < reporters.Length; i++)
            {
                var (id, name, reason) = reporters[i];
                var ticketAt = at.AddSeconds(i * 10);
                var ticketId = Guid.NewGuid();
                streams.Add(ticketId);
                await using var s = store.LightweightSession(slug);
                s.Events.StartStream<Ticket>(ticketId, new TicketCreated(ticketId, id, name, ListingReport.TicketSubject(reason), "نَصّ", ticketAt));
                foreach (var admin in admins)
                {
                    var n = new Notification { Id = Guid.NewGuid(), UserId = admin, Type = ListingReport.NoticeType, Title = ListingReport.NoticeTitle(reason),
                                               Body = ListingReport.NoticeBody(name), RelatedUrl = $"/admin/tenants/{slug}/tickets", At = ticketAt.AddMilliseconds(50) };
                    s.Store(n);
                    notices.Add((i, n.Id));
                }
                await s.SaveChangesAsync();
            }
            var counts = await EraseLikeTheDoorAsync(store, slug, me.Id);
            await using var q = store.QuerySession(slug);
            var after = new List<(int Reporter, Notification N)>();
            foreach (var (r, id) in notices) after.Add((r, (await q.LoadAsync<Notification>(id))!));
            var mine = after.Where(x => x.Reporter == 0).Select(x => x.N).ToList();
            var twin = after.Where(x => x.Reporter == 1).Select(x => x.N).ToList();
            var longer = after.Where(x => x.Reporter == 2).Select(x => x.N).ToList();
            var quoting = after.Where(x => x.Reporter == 3).Select(x => x.N).ToList();
            output.WriteLine($"إشعاراتٌ مَمحُوَّة: {counts.Notifications} · بَلاغاه بِلا اسمِه: {mine.All(n => n.Body == ListingReport.NoticeBody(AccountDeletion.ErasedName))} · " +
                             $"بَلاغا سَمِيِّه كَما كُتِبا: {twin.All(n => n.Body == ListingReport.NoticeBody("عَلي"))} · " +
                             $"بَلاغا «عَلي حَسَن» كَما كُتِبا: {longer.All(n => n.Body == ListingReport.NoticeBody("عَلي حَسَن"))} · " +
                             $"المُقتَبِسُ بِلا هاتِفِه ولِصاحِبِه: {quoting.All(n => !n.Title.Contains(myPhone, StringComparison.Ordinal) && n.Body == ListingReport.NoticeBody(placeholder))}");
            Assert.Equal(4, counts.Notifications);
            Assert.All(mine, n => Assert.Equal(ListingReport.NoticeBody(AccountDeletion.ErasedName), n.Body));
            Assert.All(twin, n => Assert.Equal(ListingReport.NoticeBody("عَلي"), n.Body));
            Assert.All(longer, n => Assert.Equal(ListingReport.NoticeBody("عَلي حَسَن"), n.Body));
            Assert.All(quoting, n => Assert.Equal((ListingReport.NoticeTitle("اتَّصَلَ بي"), ListingReport.NoticeBody(placeholder)), (n.Title, n.Body)));
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            s.DeleteWhere<User>(u => u.Id != Guid.Empty); s.DeleteWhere<Notification>(n => n.Id != Guid.Empty); s.DeleteWhere<Ticket>(t => t.Id != Guid.Empty);
            foreach (var id in streams) s.Events.ArchiveStream(id);
            await s.SaveChangesAsync();
        }
    }

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29)</b> — كانَ <c>Today_the_studio_ticket_page_reads_the_stream_and_shows_the_deleted_authors_name_on_Postgres</c>
    /// («صَفحَةُ الاستوديو تَرسُمُ الاسمَ المَحذوف: True»). صَفحَةُ تَذكِرَةِ الاستوديو (<c>TenantAdminQueries.TicketAsync</c>) تَقرَأُ اللَقطَةَ المَمحُوَّة —
    /// وبَعدَ رَدٍّ يَكتُبُه الفَريقُ بَعدَ الحَذفِ (كَما تَكتُبُه نُقطَتُه) تَبقى مَمحُوَّة، والرَدُّ بِتَوقيعِه.</summary>
    [Fact]
    public async Task The_studio_ticket_page_shows_the_erased_name_and_keeps_it_after_a_staff_reply_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = BuildStore();
        var slug = "zz-erase-" + Guid.NewGuid().ToString("N")[..8];
        const string name = "كاتِبُ التَذكِرَةِ الحَيّ";
        var me = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = name, Phone = "+966500000555" };
        var ticketId = Guid.NewGuid();
        try
        {
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(me);
                s.Events.StartStream<Ticket>(ticketId, new TicketCreated(ticketId, me.Id, name, "سُؤال", "نَصّ", DateTime.UtcNow));
                await s.SaveChangesAsync();
            }
            var counts = await EraseLikeTheDoorAsync(store, slug, me.Id);
            await using var q = store.QuerySession(slug);
            var snapshot = (await q.LoadAsync<Ticket>(ticketId))!;
            var page = (await new TenantAdminQueries(store).TicketAsync(slug, ticketId))!;
            await using (var s = store.LightweightSession(slug))
            {
                s.Events.Append(ticketId, new TicketReplied(ticketId, Guid.NewGuid(), "دَعم التَّطبيق", true, "رَدّ", DateTime.UtcNow));
                await s.SaveChangesAsync();
            }
            var afterReply = (await new TenantAdminQueries(store).TicketAsync(slug, ticketId))!;
            output.WriteLine($"تَذاكِرُ مَمحُوَّة: {counts.Tickets} · اللَقطَةُ مَمحُوَّة: {snapshot.AuthorName == AccountDeletion.ErasedName} · " +
                             $"صَفحَةُ الاستوديو تَرسُمُ الاسمَ المَحذوف: {page.AuthorName == name} · وبَعدَ رَدِّ الفَريق: {afterReply.AuthorName == name} " +
                             $"({afterReply.Replies.Count} رَدّ، {afterReply.Status})");
            Assert.Equal(1, counts.Tickets);
            Assert.Equal(AccountDeletion.ErasedName, snapshot.AuthorName);
            Assert.Equal(AccountDeletion.ErasedName, page.AuthorName);
            Assert.Equal((AccountDeletion.ErasedName, "دَعم التَّطبيق", "answered"),
                         (afterReply.AuthorName, Assert.Single(afterReply.Replies).AuthorName, afterReply.Status));
            // ولَقطَةُ المَظهَرِ الوَحيدَةُ لِهذِه الصَفحَة (`studio-app-ticket-missing`) تَذكِرَةٌ لا وُجودَ لَها: القِراءَتانِ تُجيبانِها بِـnull فَالفَرعُ نَفسُه.
            Assert.Null(await new TenantAdminQueries(store).TicketAsync(slug, Guid.Parse("00000000-0000-0000-0000-000000000001")));
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            s.DeleteWhere<User>(u => u.Id != Guid.Empty); s.DeleteWhere<Ticket>(t => t.Id != Guid.Empty);
            s.Events.ArchiveStream(ticketId);
            await s.SaveChangesAsync();
        }
    }

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-30، CO16)</b> — كانَ <c>Today_a_rebuilt_ticket_gets_the_deleted_members_name_back_on_Postgres</c> (‏<c>13b1754</c>:
    /// «اللَقطَة: حِسابٌ مَحذوف · المُعادُ بِناؤها: {اسمُه} / {اسمُه}، دَعم التَّطبيق»). والآن مَجرى تَذكِرَتِه يَحمِلُ <c>TicketAuthorRenamed</c> فَإعادَةُ بِنائِها
    /// (<c>AggregateStreamAsync&lt;Ticket&gt;</c>) تُطابِقُ اللَقطَة؛ وتَذكِرَةُ غَيرِه بِالاسمِ نَفسِه لا يُلحَقُ بِمَجراها شَيء، وتَذكِرَتُه الَّتي لا مَجرى لَها (وَثيقَةٌ مُباشِرَةٌ
    /// كَعَيِّنَةِ البَذر) تُمحى وَثيقَتُها ولا يولَدُ لَها مَجرى.</summary>
    [Fact]
    public async Task A_rebuilt_ticket_keeps_the_erased_name_and_anothers_ticket_stream_is_untouched_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = BuildStore();
        var slug = "zz-erase-" + Guid.NewGuid().ToString("N")[..8];
        const string name = "كاتِبُ تَذكِرَةٍ يُعادُ بِناؤها";
        var me = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = name, Phone = "+966500000571" };
        var (ticketId, theirsId, docOnlyId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        try
        {
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(me);
                s.Events.StartStream<Ticket>(ticketId, new TicketCreated(ticketId, me.Id, name, "سُؤال", "نَصّ", DateTime.UtcNow),
                    new TicketReplied(ticketId, Guid.NewGuid(), name, false, "تَوضيح", DateTime.UtcNow),
                    new TicketReplied(ticketId, Guid.NewGuid(), "دَعم التَّطبيق", true, "رَدّ", DateTime.UtcNow));
                s.Events.StartStream<Ticket>(theirsId, new TicketCreated(theirsId, Guid.NewGuid(), name, "سُؤالُ غَيري", "نَصّ", DateTime.UtcNow));
                s.Store(new Ticket { Id = docOnlyId, AuthorId = me.Id, AuthorName = name, Subject = "بِلا مَجرى", Status = "open" });
                await s.SaveChangesAsync();
            }
            var counts = await EraseLikeTheDoorAsync(store, slug, me.Id);
            await using var q = store.QuerySession(slug);
            var snapshot = (await q.LoadAsync<Ticket>(ticketId))!;
            var rebuilt = (await q.Events.AggregateStreamAsync<Ticket>(ticketId))!;
            var theirs = (await q.Events.AggregateStreamAsync<Ticket>(theirsId))!;
            var theirEvents = await q.Events.FetchStreamAsync(theirsId);
            var docOnly = (await q.LoadAsync<Ticket>(docOnlyId))!;
            var docOnlyStream = await q.Events.FetchStreamStateAsync(docOnlyId);
            output.WriteLine($"تَذاكِرُ مَمحُوَّة: {counts.Tickets} · اللَقطَة: {snapshot.AuthorName} · المُعادُ بِناؤها: {rebuilt.AuthorName} / {string.Join("، ", rebuilt.Replies.Select(r => r.AuthorName))} · " +
                             $"تَذكِرَةُ غَيرِه: {theirs.AuthorName} ({theirEvents.Count} حَدَث) · بِلا مَجرى: {docOnly.AuthorName} (مَجرى وُلِد: {docOnlyStream is not null})");
            Assert.Equal(2, counts.Tickets);
            Assert.Equal((AccountDeletion.ErasedName, AccountDeletion.ErasedName, "دَعم التَّطبيق"),
                         (snapshot.AuthorName, snapshot.Replies[0].AuthorName, snapshot.Replies[1].AuthorName));
            Assert.Equal((AccountDeletion.ErasedName, AccountDeletion.ErasedName, "دَعم التَّطبيق"),
                         (rebuilt.AuthorName, rebuilt.Replies[0].AuthorName, rebuilt.Replies[1].AuthorName));
            Assert.Equal((name, 1), (theirs.AuthorName, theirEvents.Count));
            Assert.Equal(AccountDeletion.ErasedName, docOnly.AuthorName);
            Assert.Null(docOnlyStream);
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            s.DeleteWhere<User>(u => u.Id != Guid.Empty); s.DeleteWhere<Ticket>(t => t.Id != Guid.Empty);
            s.Events.ArchiveStream(ticketId); s.Events.ArchiveStream(theirsId);
            await s.SaveChangesAsync();
        }
    }

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29)</b> — كانَ <c>Today_a_deleted_members_pending_offer_stays_pending_and_its_stream_keeps_the_name_on_Postgres</c>
    /// («المَجرى بِالاسمِ المَحذوف: True · الحالُ في المَجرى: Pending · في اللَقطَة: Pending»). عَرضُه المُعَلَّقُ يُسحَبُ ومَجراه يَحمِلُ الاسمَ المَمحُوَّ، ومَقبولُه
    /// يُمحى اسمُه ويَبقى مَقبولاً، وعَرضُ غَيرِه عَلى الإعلانِ نَفسِه كَما هُو؛ والقَبولُ يَبني مِن المَجرى فَيَجِدُه مَسحوباً.</summary>
    [Fact]
    public async Task A_deleted_members_pending_offer_is_withdrawn_and_its_stream_carries_the_erased_name_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = BuildStore();
        var slug = "zz-erase-" + Guid.NewGuid().ToString("N")[..8];
        const string name = "مُقَدِّمُ العَرضِ الحَيّ";
        var me = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = name, Phone = "+966500000556" };
        var (offerId, acceptedId, theirsId, listing) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        try
        {
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(me);
                var now = DateTime.UtcNow;
                s.Events.StartStream<Offer>(offerId, new OfferSubmitted(offerId, listing, me.Id, name, 10m, null, 24.7, 46.7, now.AddDays(3), now));
                s.Events.StartStream<Offer>(acceptedId, new OfferSubmitted(acceptedId, Guid.NewGuid(), me.Id, name, 12m, null, 24.7, 46.7, now.AddDays(3), now),
                                                         new OfferAccepted(acceptedId, now));
                s.Events.StartStream<Offer>(theirsId, new OfferSubmitted(theirsId, listing, Guid.NewGuid(), "مُقَدِّمٌ آخَر", 11m, null, 24.7, 46.7, now.AddDays(3), now));
                await s.SaveChangesAsync();
            }
            var counts = await EraseLikeTheDoorAsync(store, slug, me.Id);
            await using var q = store.LightweightSession(slug);
            var snapshot = (await q.LoadAsync<Offer>(offerId))!;
            var stream = (await q.Events.AggregateStreamAsync<Offer>(offerId))!;
            var accepted = (await q.Events.AggregateStreamAsync<Offer>(acceptedId))!;
            var theirs = (await q.LoadAsync<Offer>(theirsId))!;
            output.WriteLine($"عُروضٌ مَمحُوَّة: {counts.Offers} · اللَقطَةُ مَمحُوَّة: {snapshot.OffererName == AccountDeletion.ErasedName} · " +
                             $"المَجرى بِالاسمِ المَحذوف: {stream.OffererName == name} · الحالُ في المَجرى: {stream.Status} · في اللَقطَة: {snapshot.Status} · " +
                             $"المَقبولُ: {accepted.Status}/{accepted.OffererName == AccountDeletion.ErasedName} · عَرضُ غَيرِه: {theirs.Status}/{theirs.OffererName == "مُقَدِّمٌ آخَر"}");
            Assert.Equal(2, counts.Offers);
            Assert.Equal((AccountDeletion.ErasedName, OfferStatus.Withdrawn), (snapshot.OffererName, snapshot.Status));
            Assert.Equal((AccountDeletion.ErasedName, OfferStatus.Withdrawn), (stream.OffererName, stream.Status));
            Assert.Equal((AccountDeletion.ErasedName, OfferStatus.Accepted), (accepted.OffererName, accepted.Status));
            Assert.Equal(("مُقَدِّمٌ آخَر", OfferStatus.Pending), (theirs.OffererName, theirs.Status));
            Assert.Equal(10m, snapshot.Price);   // اللَقطَةُ لَم تُبنَ مِن الصِفر: الإسقاطُ طَبَّقَ الحَدَثَينِ عَلى المُخَزَّنَة.
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            s.DeleteWhere<User>(u => u.Id != Guid.Empty); s.DeleteWhere<Offer>(o => o.Id != Guid.Empty);
            s.Events.ArchiveStream(offerId); s.Events.ArchiveStream(acceptedId); s.Events.ArchiveStream(theirsId);
            await s.SaveChangesAsync();
        }
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه (‏2026-10-30، G-2)</b> — كانَ <c>Today_a_buyers_notice_about_an_offer_the_deleted_owner_rejected_keeps_his_name_on_Postgres</c> (‏<c>fcd9efe</c>:
    /// «صَفَقات 1 · إشعارات 0 · سَطرُه مَمحُوّ: True · إشعارُ المُشتَري بِاسمِه: True»). مالِكُ الإعلانِ الَّذي رَفَضَ عِندَ «عَرض» ولَيسَ طَرَفاً ثُمَّ حَذَفَ حِسابَه: إشعارُ المُشتَري
    /// صارَ «حِسابٌ مَحذوف أَلغى طَلَبَكَ…» — الصَفقَةُ الَّتي فَعَلَ فيها تَربِطُه كَصَفقَةِ الطَرَف (<see cref="AccountErasure.RelatedUrls"/>). <b>والسالِبُ بِحَرفِه</b>: إشعارٌ
    /// بِالاسمِ نَفسِه عَن صَفقَةٍ لَم يَمَسَّها لا يُمَسّ.</para>
    /// </summary>
    [Fact]
    public async Task A_buyers_notice_about_an_offer_the_deleted_owner_rejected_loses_his_name_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = BuildStore();
        var slug = "zz-erase-" + Guid.NewGuid().ToString("N")[..8];
        const string name = "مالِكُ الإعلانِ الرافِض";
        var owner = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = name, Phone = "+966500000558" };
        var buyer = Guid.NewGuid();
        var l = new ACommerce.Platform.I18n.L(new Microsoft.AspNetCore.Http.HttpContextAccessor());
        var rejected = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = slug, ListingId = Guid.NewGuid(), ListingTitle = "سِلعَةٌ مَرفوضَة", InitiatorId = buyer, InitiatorName = "مُشتَرٍ",
            Status = DealStatus.Cancelled, Stage = DealStage.Offered, Refs = new() { [DealsService.ListingOwnerRef] = owner.Id.ToString() },
            Timeline = new() { new DealEvent(DealStage.Offered, DealStage.Offered, "cancelled", owner.Id, name, "رَفض", DateTime.UtcNow) },
        };
        var elsewhere = new Deal { Id = Guid.NewGuid(), TenantSlug = slug, ListingTitle = "صَفقَةُ غَيرِه", InitiatorId = buyer, InitiatorName = "مُشتَرٍ", Status = DealStatus.Cancelled };
        var notice = DealNotices.CancelledFor(slug, rejected, name, l, DateTime.UtcNow);
        var unrelated = DealNotices.CancelledFor(slug, elsewhere, name, l, DateTime.UtcNow);
        try
        {
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(owner); s.Store(rejected, elsewhere); s.Store(notice, unrelated);
                await s.SaveChangesAsync();
            }
            var counts = await EraseLikeTheDoorAsync(store, slug, owner.Id);
            await using var q = store.QuerySession(slug);
            var deal = (await q.LoadAsync<Deal>(rejected.Id))!;
            var told = (await q.LoadAsync<Notification>(notice.Id))!;
            var other = (await q.LoadAsync<Notification>(unrelated.Id))!;
            output.WriteLine($"صَفَقات {counts.Deals} · إشعارات {counts.Notifications} · سَطرُه مَمحُوّ: {deal.Timeline[0].ActorName == AccountDeletion.ErasedName} · " +
                             $"إشعارُ المُشتَري بِاسمِه: {told.Body.Contains(name, StringComparison.Ordinal)} · إشعارُ صَفقَةٍ لَم يَمَسَّها كَما كُتِب: {other.Body == unrelated.Body}");
            Assert.Equal(AccountDeletion.ErasedName, deal.Timeline[0].ActorName);
            Assert.Equal(1, counts.Notifications);
            Assert.Equal(DealNotices.CancelledFor(slug, rejected, AccountDeletion.ErasedName, l, DateTime.UtcNow).Body, told.Body);
            Assert.Equal(unrelated.Body, other.Body);
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            s.DeleteWhere<User>(u => u.Id != Guid.Empty); s.DeleteWhere<Deal>(d => d.Id != Guid.Empty); s.DeleteWhere<Notification>(n => n.Id != Guid.Empty);
            await s.SaveChangesAsync();
        }
    }

    /// <summary><b>حَيّاً (‏2026-10-30، B-PRIV-4)</b>: سَطرُ مَنحِ إدارَةٍ قَديمٌ بِاسمِ العُضوِ في حاشِيَتِه — بَعدَ حَذفِ حِسابِه يَبقى في المَخزَنِ كَما كُتِب، و<c>RecentAsync</c>
    /// تَعرِضُه «حِسابٌ مَحذوف»؛ وسَطرُ عُضوٍ قائِمٍ كَما كُتِب.</summary>
    [Fact]
    public async Task An_old_grant_admin_row_hides_the_deleted_members_note_when_shown_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = BuildStore();
        var slug = "zz-erase-" + Guid.NewGuid().ToString("N")[..8];
        var me = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "سارة المُرَقّاة", Phone = "+966500000561" };
        var liveId = Guid.NewGuid();
        var granted = new AuditEntry { Id = Guid.NewGuid(), Scope = slug, ActorId = Guid.NewGuid(), ActorName = "صاحِبُ المَتجَر", Action = "user.grant_admin",
            EntityType = "User", EntityId = me.Id.ToString(), Note = me.FullName };
        var other = new AuditEntry { Id = Guid.NewGuid(), Scope = slug, ActorId = Guid.NewGuid(), ActorName = "صاحِبُ المَتجَر", Action = "user.grant_admin",
            EntityType = "User", EntityId = liveId.ToString(), Note = "هِند القائِمَة" };
        try
        {
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(me); s.Store(new User { Id = liveId, TenantSlug = slug, FullName = "هِند القائِمَة" }); s.Store(granted, other);
                await s.SaveChangesAsync();
            }
            await EraseLikeTheDoorAsync(store, slug, me.Id);
            var shown = await new AuditLogQueries(store).RecentAsync(slug);
            await using var q = store.QuerySession(slug);
            var stored = (await q.LoadAsync<AuditEntry>(granted.Id))!;
            output.WriteLine($"في المَخزَن: {stored.Note} · في العَرض: {shown.Single(a => a.Id == granted.Id).Note} · سَطرُ القائِمَة: {shown.Single(a => a.Id == other.Id).Note}");
            Assert.Equal("سارة المُرَقّاة", stored.Note);
            Assert.Equal(AccountDeletion.ErasedName, shown.Single(a => a.Id == granted.Id).Note);
            Assert.Equal("هِند القائِمَة", shown.Single(a => a.Id == other.Id).Note);
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            s.DeleteWhere<User>(u => u.Id != Guid.Empty); s.DeleteWhere<AuditEntry>(a => a.Id != Guid.Empty);
            await s.SaveChangesAsync();
        }
    }

    /// <summary><b>مُوجَبٌ وسالِبٌ حَيّاً (‏2026-10-30، B-PRIV-2)</b>: مَوضوعُ مُحادَثَتِه المُباشِرَةِ «تَواصُل مَع {اسمِه}» يَصيرُ «تَواصُل مَع حِسابٌ مَحذوف» في مُعامَلَةِ الحَذف
    /// (<see cref="AccountErasure.EraseSubject"/> عَبرَ <see cref="AccountErasure.EraseCopiesAsync"/>)، ومُحادَثَةُ غَيرِه بِالاسمِ نَفسِه كَما كُتِبَت.</summary>
    [Fact]
    public async Task A_deleted_members_name_leaves_his_direct_chat_subject_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = BuildStore();
        var slug = "zz-erase-" + Guid.NewGuid().ToString("N")[..8];
        const string name = "بائِعُ المُحادَثَة";
        var me = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = name, Phone = "+966500000560" };
        string Subject(string n) => ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "chats.direct.subject").Replace("{0}", n, StringComparison.Ordinal);
        var mine = new Conversation { Id = Guid.NewGuid(), OwnerId = Guid.NewGuid(), OwnerName = "مُشتَرٍ", PartnerId = me.Id, PartnerName = name, Subject = Subject(name) };
        var theirs = new Conversation { Id = Guid.NewGuid(), OwnerId = Guid.NewGuid(), OwnerName = "مُشتَرٍ", PartnerId = Guid.NewGuid(), PartnerName = name, Subject = Subject(name) };
        try
        {
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(me); s.Store(mine, theirs);
                await s.SaveChangesAsync();
            }
            var counts = await EraseLikeTheDoorAsync(store, slug, me.Id);
            await using var q = store.QuerySession(slug);
            var a = (await q.LoadAsync<Conversation>(mine.Id))!;
            var b = (await q.LoadAsync<Conversation>(theirs.Id))!;
            output.WriteLine($"مُحادَثات {counts.Conversations} · مَوضوعُه: {a.Subject} · مُحادَثَةُ غَيرِه: {b.Subject}");
            Assert.Equal((AccountDeletion.ErasedName, Subject(AccountDeletion.ErasedName)), (a.PartnerName, a.Subject));
            Assert.Equal((name, Subject(name)), (b.PartnerName, b.Subject));
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            s.DeleteWhere<User>(u => u.Id != Guid.Empty); s.DeleteWhere<Conversation>(c => c.Id != Guid.Empty);
            await s.SaveChangesAsync();
        }
    }

    /// <summary><b>مُوجَب (‏2026-10-30، G-2)</b>: عُضوٌ مِن فَريقِ المَتجَرِ أَلغى بِمِفتاحِ رَبطٍ صَفقَةً لَيسَ طَرَفَها (بابُ <c>/api/v1/deals/{id}/cancel</c>: سَطرُ الخَطِّ الزَمَنِيِّ
    /// بِمُعَرِّفِه، وإشعارُ المُشتَري بِاسمِ المِفتاحِ كَما أُصدِر — وقَد تَبَدَّلَ اسمُ حِسابِه بَعدَه)، ثُمَّ حَذَفَ حِسابَه: الاسمُ القَديمُ عَلى مِفتاحِه (د56) يُمحى مِن
    /// الإشعارِ أَيضاً، والمِفتاحُ «حِسابٌ مَحذوف».</summary>
    [Fact]
    public async Task An_api_key_actors_cancel_notice_loses_the_name_the_key_was_issued_under_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = BuildStore();
        var slug = "zz-erase-" + Guid.NewGuid().ToString("N")[..8];
        const string issuedAs = "مُوَظَّفُ الرَبطِ القَديم";
        var member = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "مُوَظَّفُ الرَبطِ بَعدَ التَسمِيَة", Phone = "+966500000559" };
        var l = new ACommerce.Platform.I18n.L(new Microsoft.AspNetCore.Http.HttpContextAccessor());
        var cancelled = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = slug, ListingTitle = "طَلَبٌ أُلغِيَ بِالرَبط", InitiatorId = Guid.NewGuid(), InitiatorName = "مُشتَرٍ", CounterpartyId = Guid.NewGuid(),
            CounterpartyName = "بائِع", Status = DealStatus.Cancelled,
            Timeline = new() { new DealEvent(DealStage.Booked, DealStage.Booked, "cancelled", member.Id, issuedAs, "api", DateTime.UtcNow) },
        };
        var notice = DealNotices.CancelledFor(slug, cancelled, issuedAs, l, DateTime.UtcNow);
        var keys = new ApiKeyService(store);
        string? keyId = null;
        try
        {
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(member); s.Store(cancelled); s.Store(notice);
                await s.SaveChangesAsync();
            }
            keyId = (await keys.IssueAsync(slug, new ApiKeyValidator.IssueRequest("رَبطُ المَخزَن", member.Id, issuedAs, new[] { ApiScopeCatalog.DealsRead }, null), Guid.NewGuid())).Key.Id;
            await using (var s = store.LightweightSession(slug))
            {
                var u = (await s.LoadAsync<User>(member.Id))!;
                var names = ActorName.NamesOf(u);
                var contacts = AccountErasure.ContactsOf(u);
                AccountDeletion.Erase(u, DateTime.UtcNow);
                s.Store(u);
                await ApiKeyService.RevokeActorKeysAsync(s, slug, member.Id, DateTime.UtcNow);
                await AccountErasure.EraseCopiesAsync(s, slug, member.Id, names, contacts);
                await s.SaveChangesAsync();
            }
            await using var q = store.QuerySession(slug);
            var told = (await q.LoadAsync<Notification>(notice.Id))!;
            var key = (await q.LoadAsync<ApiKeyDocument>(keyId))!;
            output.WriteLine($"إشعارُ المُشتَري بِاسمِ المِفتاح: {told.Body.Contains(issuedAs, StringComparison.Ordinal)} · المِفتاحُ: {key.Status}/{key.ActorName == AccountDeletion.ErasedName}");
            Assert.Equal(DealNotices.CancelledFor(slug, cancelled, AccountDeletion.ErasedName, l, DateTime.UtcNow).Body, told.Body);
            Assert.Equal((ApiKeyDocument.StatusRevoked, AccountDeletion.ErasedName), (key.Status, key.ActorName));
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            s.DeleteWhere<User>(u => u.Id != Guid.Empty); s.DeleteWhere<Deal>(d => d.Id != Guid.Empty); s.DeleteWhere<Notification>(n => n.Id != Guid.Empty);
            if (keyId is not null) s.Delete<ApiKeyDocument>(keyId);
            await s.SaveChangesAsync();
        }
    }

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-30، B-PRIV-1)</b> — كانَ <c>Today_a_deleted_sellers_listing_its_pending_offer_and_its_incoming_deal_stay_open_on_Postgres</c>
    /// (‏<c>0e95601</c>: «مُحِيَ: 0 · الإعلانُ مَحذوف: False · العَرضُ عَلَيه: Pending · الصَفقَةُ الوارِدَة: Active عِندَ Offered · مالِكُها المَمحُوّ: True»). والآن تَسلسُلُ النُقطَةِ
    /// بِعَينِه عَلى Postgres — الحِفظُ الواحِدُ ثُمَّ إلغاءُ الوارِدَةِ بِـ<see cref="AccountErasure.ClosingCanceller"/> وسَبَبِ القامُوس: الإعلانُ مَحذوفٌ في لَقطَتِه، والعَرضُ عَلَيه
    /// مَرفوض، والصَفقَةُ مُلغاةٌ بِسَطرِ المَنصَّة؛ وإعلانُ عُضوٍ آخَرَ والعَرضُ عَلَيه كَما هُما.</summary>
    [Fact]
    public async Task A_deleted_sellers_listing_is_withdrawn_its_pending_offer_rejected_and_its_incoming_deal_cancelled_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = BuildStore();
        var slug = "zz-erase-" + Guid.NewGuid().ToString("N")[..8];
        var seller = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "بائِعُ الإعلانِ الحَيّ", Phone = "+966500000557" };
        var buyer = Guid.NewGuid();
        var (listingId, offerId, othersListing, othersOffer) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var incoming = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = slug, ListingId = listingId, ListingTitle = "سِلعَةُ البائِع", InitiatorId = buyer, InitiatorName = "مُشتَرٍ",
            Status = DealStatus.Active, Stage = DealStage.Offered, Refs = new() { [DealsService.ListingOwnerRef] = seller.Id.ToString() },
        };
        try
        {
            await using (var s = store.LightweightSession(slug))
            {
                var now = DateTime.UtcNow;
                s.Store(seller);
                s.Events.StartStream<ACommerce.Kit.Listings.Listing>(listingId, new ACommerce.Kit.Listings.ListingCreated(listingId, slug, "سِلعَةُ البائِع", null, 100m, "c",
                    null, null, new Dictionary<string, string> { [ListingEditService.OwnerAttribute] = seller.Id.ToString(), ["pickup_lat"] = "24.7" }, now));
                s.Events.StartStream<Offer>(offerId, new OfferSubmitted(offerId, listingId, buyer, "مُقَدِّمٌ آخَر", 90m, null, 24.7, 46.7, now.AddDays(3), now));
                s.Store(incoming);
                await s.SaveChangesAsync();
            }
            await using (var s = store.LightweightSession(slug))
            {
                var now = DateTime.UtcNow;
                s.Events.StartStream<ACommerce.Kit.Listings.Listing>(othersListing, new ACommerce.Kit.Listings.ListingCreated(othersListing, slug, "سِلعَةُ غَيرِه", null, 50m, "c",
                    null, null, new Dictionary<string, string> { [ListingEditService.OwnerAttribute] = buyer.ToString() }, now));
                s.Events.StartStream<Offer>(othersOffer, new OfferSubmitted(othersOffer, othersListing, Guid.NewGuid(), "مُقَدِّمٌ ثالِث", 45m, null, 24.7, 46.7, now.AddDays(3), now));
                await s.SaveChangesAsync();
            }
            var counts = await EraseLikeTheDoorAsync(store, slug, seller.Id);
            // ما تَفعَلُه النُقطَةُ بَعدَ الحِفظ — بِالمُلغي والسَبَبِ نَفسَيهِما.
            var deals = new DealsService(store, new DealMoneyClaimFirstTests.RecordingPaymentProvider());
            var reason = ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "account.delete.cancel_reason");
            var cancelled = new List<DealCancelResult>();
            foreach (var id in counts.IncomingDeals) cancelled.Add(await deals.CancelAsync(slug, id, AccountErasure.ClosingCanceller, reason));
            await using var q = store.QuerySession(slug);
            var listing = (await q.LoadAsync<ACommerce.Kit.Listings.Listing>(listingId))!;
            var offer = (await q.LoadAsync<Offer>(offerId))!;
            var deal = (await q.LoadAsync<Deal>(incoming.Id))!;
            var theirs = (await q.LoadAsync<ACommerce.Kit.Listings.Listing>(othersListing))!;
            var theirOffer = (await q.LoadAsync<Offer>(othersOffer))!;
            output.WriteLine($"مُحِيَ: {counts.Total} · إعلاناتٌ مَسحوبَة: {counts.Listings} · عُروضٌ مَرفوضَة: {counts.RejectedOffers} · وارِدَةٌ تُلغى: {counts.IncomingDeals.Count} · " +
                             $"الإعلانُ مَحذوف: {listing.IsDeleted} · العَرضُ عَلَيه: {offer.Status} · الصَفقَةُ الوارِدَة: {deal.Status} بِسَطرِ المَنصَّة: {DealEvent.IsByPlatform(deal.Timeline[^1])} · " +
                             $"إعلانُ غَيرِه: {(theirs.IsDeleted ? "مَحذوف" : "قائِم")}/{theirOffer.Status}");
            Assert.Equal((1, 1, 1), (counts.Listings, counts.RejectedOffers, counts.IncomingDeals.Count));
            Assert.True(listing.IsDeleted);
            Assert.Equal(OfferStatus.Rejected, offer.Status);
            Assert.True(Assert.Single(cancelled).Ok);
            Assert.Equal((DealStatus.Cancelled, reason), (deal.Status, deal.CancelReason));
            Assert.True(DealEvent.IsByPlatform(deal.Timeline[^1]));
            Assert.Equal((false, OfferStatus.Pending), (theirs.IsDeleted, theirOffer.Status));
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            s.DeleteWhere<User>(u => u.Id != Guid.Empty); s.DeleteWhere<Offer>(o => o.Id != Guid.Empty); s.DeleteWhere<Deal>(d => d.Id != Guid.Empty);
            s.DeleteWhere<ACommerce.Kit.Listings.Listing>(l => l.Id != Guid.Empty);
            s.Events.ArchiveStream(offerId); s.Events.ArchiveStream(listingId); s.Events.ArchiveStream(othersListing); s.Events.ArchiveStream(othersOffer);
            await s.SaveChangesAsync();
        }
    }

    /// <summary><b>مُوجَبٌ وسالِبٌ حَيّاً (‏2026-10-30، G-7)</b>: استِعلامُ صَفحَةِ الحَذفِ (<see cref="UserDataQueries.CashRefundsOwedAsync"/>) يُعيدُ مُلغاتَه الَّتي آخِرُ سَطرِ مالِها
    /// «رَدٌّ نَقدِيٌّ مُستَحَقّ» — لا مُلغاتَه بِبِطاقَةٍ مُستَرجَعَة، ولا صَفقَةَ غَيرِه بِالسَطرِ نَفسِه، ولا صَفقَتَه في مَتجَرٍ آخَر (جَلسَةُ المُستَأجِر).</summary>
    [Fact]
    public async Task The_deletion_pages_query_returns_his_cancelled_cash_deals_still_owed_back_and_nothing_else_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = BuildStore();
        var slug = "zz-erase-" + Guid.NewGuid().ToString("N")[..8];
        var neighbour = "zz-erase-" + Guid.NewGuid().ToString("N")[..8];
        var me = Guid.NewGuid();
        Deal Make(string tenant, Guid buyer, params string[] lines)
        {
            var d = new Deal { Id = Guid.NewGuid(), TenantSlug = tenant, InitiatorId = buyer, InitiatorName = "مُشتَري", CounterpartyId = Guid.NewGuid(),
                CounterpartyName = "بائِعُ النَقد", ListingTitle = "سِلعَة", AmountSar = 180m, Status = DealStatus.Cancelled };
            foreach (var a in lines) d.Timeline.Add(new(DealStage.Delivered, DealStage.Delivered, a, null, null, null, DateTime.UtcNow));
            return d;
        }
        var owed = Make(slug, me, DealEvent.CashOnDelivery, "cancelled", DealEvent.CashRefundDue);
        var card = Make(slug, me, DealPaymentLines.Captured, "cancelled", DealPaymentLines.Refunded);
        var theirs = Make(slug, Guid.NewGuid(), DealEvent.CashOnDelivery, "cancelled", DealEvent.CashRefundDue);
        var elsewhere = Make(neighbour, me, DealEvent.CashOnDelivery, "cancelled", DealEvent.CashRefundDue);
        try
        {
            await using (var s = store.LightweightSession(slug)) { s.Store(owed, card, theirs); await s.SaveChangesAsync(); }
            await using (var s = store.LightweightSession(neighbour)) { s.Store(elsewhere); await s.SaveChangesAsync(); }

            var found = await new UserDataQueries(store).CashRefundsOwedAsync(slug, me);
            output.WriteLine($"صَفَقاتٌ مَخزونَة: 4 · يُرَدُّ لَه نَقداً هُنا: {found.Count} ({string.Join(", ", found.Select(d => $"{d.ListingTitle} {d.AmountSar:0}"))})");
            Assert.Equal(owed.Id, Assert.Single(found).Id);
        }
        finally
        {
            foreach (var t in new[] { slug, neighbour })
            {
                await using var s = store.LightweightSession(t);
                s.DeleteWhere<Deal>(d => d.Id != Guid.Empty);
                await s.SaveChangesAsync();
            }
        }
    }
}

/// <summary>
/// <para><b>إعادَةُ بابِ الحَذفِ بِجِسمِه لا بِشَكلِه</b> (‏2026-10-30، الموجَة ١٢ F2 · ١ — مُراجَعَةُ <c>8357c95</c> (أ)): كانَت إعادَةُ <c>/{slug}/me/delete/confirm</c> عِندَ
/// <c>ConcurrencyException</c> لا يَقيسُها إلّا فاحِصُ شَكلٍ (<c>WithdrawalViolations</c>: نَصُّ الحَلقَةِ ونَصُّ الـ<c>catch</c>)، وصُفوفُ الحَذفِ الحَيَّةُ تُعيدُ كِتابَةَ
/// التَسلسُلِ في الاختِبار — فَحَلقَةٌ لا تُعيدُ فِعلاً تَبقى خَضراء. والآن <see cref="LiveTemplateDoorHost"/> يُنادي البابَ المُسَجَّلَ بِمُرَشِّحاتِه عَلى Postgres،
/// ومُستَمِعُ جَلسَةٍ في مَخزَنِ المُضيفِ يُدخِلُ كاتِباً مُوازِياً عَلى وَثيقَةِ الحِسابِ <b>بَينَ قِراءَةِ الحَذفِ وحِفظِه</b> — فَالحِفظُ الأَوَّلُ يُرَدُّ بِـ<c>ConcurrencyException</c>
/// حَقّاً.</para>
/// </summary>
[Collection(LiveProofCollection.Name)]
public class LiveAccountDeletionDoorProofTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private const string DeleteDoor = "/{slug}/me/delete/confirm";

    /// <summary>يَعُدُّ حِفظَ جَلسَةِ الحَذفِ (وَثيقَةُ حِسابٍ مَمحُوَّةٌ في ما سيُحفَظ)، ويُشَغِّلُ الكاتِبَ المُوازِيَ قَبلَ أَوَّلِ حِفظٍ مِنها — أَو قَبلَ كُلِّ حِفظٍ إن طُلِب.</summary>
    internal sealed class ConflictOnDeletionSave(Func<Task> concurrentWrite, bool everyTime) : Marten.DocumentSessionListenerBase
    {
        public int DeletionSaves;
        public Func<Task> Write { get; set; } = concurrentWrite;

        public override async Task BeforeSaveChangesAsync(Marten.IDocumentSession session, CancellationToken token)
        {
            if (session.PendingChanges.AllChangedFor<User>().Any(u => u.DeletedAt is not null)
                && (Interlocked.Increment(ref DeletionSaves) == 1 || everyTime))
                await Write();
        }
    }

    private static async Task<(User Seller, Guid Listing, Guid Offer)> SeedAsync(Marten.IDocumentStore store, string slug)
    {
        var seller = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "بائِعٌ يُغلِقُ حِسابَه", Phone = "+966500000561", PhoneVerified = true };
        var (listing, offer) = (Guid.NewGuid(), Guid.NewGuid());
        var now = DateTime.UtcNow;
        await using var s = store.LightweightSession(slug);
        s.Store(seller);
        s.Events.StartStream<ACommerce.Kit.Listings.Listing>(listing, new ACommerce.Kit.Listings.ListingCreated(listing, slug, "سِلعَةُ البائِع", null, 100m, "c",
            null, null, new Dictionary<string, string> { [ListingEditService.OwnerAttribute] = seller.Id.ToString() }, now));
        s.Events.StartStream<Offer>(offer, new OfferSubmitted(offer, listing, Guid.NewGuid(), "مُقَدِّمُ عَرض", 90m, null, 24.7, 46.7, now.AddDays(3), now));
        await s.SaveChangesAsync();
        return (seller, listing, offer);
    }

    private static async Task CleanAsync(Marten.IDocumentStore store, string slug, params Guid[] streams)
    {
        await using (var s = store.LightweightSession(slug))
        {
            s.DeleteWhere<Offer>(o => o.Id != Guid.Empty);
            s.DeleteWhere<ACommerce.Kit.Listings.Listing>(l => l.Id != Guid.Empty);
            foreach (var id in streams) s.Events.ArchiveStream(id);
            await s.SaveChangesAsync();
        }
        await LiveZeroRoleAdminProofTests.CleanAsync(store, slug);
    }

    private static (string, string) Confirm()
        => ("confirm", ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "account.delete.confirm_word"));

    /// <summary><b>المُوجَب</b>: كاتِبٌ مُوازٍ (قَبولُ شُروطٍ مِن جِهازٍ آخَر) بَينَ قِراءَةِ الحَذفِ وحِفظِه يَرُدُّ الحِفظَ الأَوَّل، فَيُعيدُ البابُ مِن قِراءَةٍ جَديدَةٍ مَرَّةً
    /// ويَنجَح: حِفظانِ لِلحَذفِ بِالعَدّ، وما كَتَبَه المُوازي باقٍ (القِراءَةُ الثانِيَةُ جَديدَة)، والحِسابُ مَمحُوٌّ مَرَّةً (الإصدارُ 1)، والإعلانُ مَسحوبٌ بِحَدَثٍ واحِد،
    /// والعَرضُ عَلَيه مَرفوضٌ بِحَدَثٍ واحِد.</summary>
    [Fact]
    public async Task A_concurrent_account_write_between_load_and_save_is_retried_on_a_fresh_read_and_erases_once_on_Postgres()
    {
        if (!Enabled) return;
        var slug = "zz-deldoor-" + Guid.NewGuid().ToString("N")[..8];
        Guid sellerId = Guid.Empty;
        Marten.IDocumentStore? hostStore = null;
        var listener = new ConflictOnDeletionSave(() => Task.CompletedTask, everyTime: false);
        listener.Write = () => ACommerce.Kit.Auth.Server.AccountWrites.UpdateAsync<User>(hostStore!, slug, sellerId, u =>
        {
            u.AcceptedTermsVersion = 99;
            return true;
        });
        await using var host = LiveTemplateDoorHost.Build(o => o.Listeners.Add(listener));
        hostStore = host.Store;
        var (seller, listing, offer) = await SeedAsync(host.Store, slug);
        sellerId = seller.Id;
        try
        {
            var token = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(seller.Id, slug, seller.SessionVersion);
            var (status, location) = await host.PostFormAsync(DeleteDoor, new Dictionary<string, string> { ["slug"] = slug }, slug, token, Confirm());

            await using var q = host.Store.QuerySession(slug);
            var after = (await q.LoadAsync<User>(seller.Id))!;
            var listingEvents = await q.Events.FetchStreamAsync(listing);
            var offerEvents = await q.Events.FetchStreamAsync(offer);
            var withdrawals = listingEvents.Count(e => e.Data is ACommerce.Kit.Listings.ListingDeleted);
            var rejections = offerEvents.Count(e => e.Data is OfferRejected);
            output.WriteLine($"البابُ: {status} → {location} · حِفظُ جَلسَةِ الحَذف: {listener.DeletionSaves} · ما كَتَبَه المُوازي باقٍ: {after.AcceptedTermsVersion == 99} · " +
                             $"DeletedAt {(after.DeletedAt is null ? "فارِغ" : "قائِم")} · الاسم {after.FullName} · الإصدار {after.SessionVersion} · سَحبُ الإعلان: {withdrawals} · رَفضُ العَرض: {rejections}");
            Assert.Equal((302, $"/{slug}?deleted=1"), (status, location));
            Assert.Equal(2, listener.DeletionSaves);
            Assert.Equal(99, after.AcceptedTermsVersion);
            Assert.NotNull(after.DeletedAt);
            Assert.Equal((AccountDeletion.ErasedName, 1), (after.FullName, after.SessionVersion));
            Assert.Equal((1, 1), (withdrawals, rejections));
        }
        finally { await CleanAsync(host.Store, slug, listing, offer); }
    }

    /// <summary><b>السالِب — يَفشَلُ مُغلَقاً</b>: كاتِبٌ مُوازٍ يَسبِقُ المُحاوَلَتَينِ كِلتَيهِما — يَرمي البابُ <c>ConcurrencyException</c> بَعدَ حِفظَينِ مَردودَين
    /// (‏<c>AccountWrites.Attempts</c>)، ولا يُكتَبُ شَيء: الحِسابُ قائِمٌ بِاسمِه، والإعلانُ قائِم، والعَرضُ مُعَلَّق.</summary>
    [Fact]
    public async Task A_concurrent_write_before_both_attempts_fails_the_deletion_closed_and_writes_nothing_on_Postgres()
    {
        if (!Enabled) return;
        var slug = "zz-deldoor-" + Guid.NewGuid().ToString("N")[..8];
        Guid sellerId = Guid.Empty;
        Marten.IDocumentStore? hostStore = null;
        var listener = new ConflictOnDeletionSave(() => Task.CompletedTask, everyTime: true);
        listener.Write = () => ACommerce.Kit.Auth.Server.AccountWrites.UpdateAsync<User>(hostStore!, slug, sellerId, u =>
        {
            u.AcceptedTermsVersion++;
            return true;
        });
        await using var host = LiveTemplateDoorHost.Build(o => o.Listeners.Add(listener));
        hostStore = host.Store;
        var (seller, listing, offer) = await SeedAsync(host.Store, slug);
        sellerId = seller.Id;
        try
        {
            var token = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(seller.Id, slug, seller.SessionVersion);
            var thrown = await Record.ExceptionAsync(() => host.PostFormAsync(DeleteDoor, new Dictionary<string, string> { ["slug"] = slug }, slug, token, Confirm()));

            await using var q = host.Store.QuerySession(slug);
            var after = (await q.LoadAsync<User>(seller.Id))!;
            var l = (await q.LoadAsync<ACommerce.Kit.Listings.Listing>(listing))!;
            var o = (await q.LoadAsync<Offer>(offer))!;
            output.WriteLine($"البابُ: {thrown?.GetType().Name ?? "لَم يَرمِ"} · حِفظُ جَلسَةِ الحَذف: {listener.DeletionSaves} · DeletedAt {(after.DeletedAt is null ? "فارِغ" : "قائِم")} · " +
                             $"الاسم {after.FullName} · الإعلانُ مَحذوف: {l.IsDeleted} · العَرض: {o.Status}");
            Assert.IsType<JasperFx.ConcurrencyException>(thrown);
            Assert.Equal(ACommerce.Kit.Auth.Server.AccountWrites.Attempts, listener.DeletionSaves);
            Assert.Null(after.DeletedAt);
            Assert.Equal(seller.FullName, after.FullName);
            Assert.Equal((false, OfferStatus.Pending), (l.IsDeleted, o.Status));
        }
        finally { await CleanAsync(host.Store, slug, listing, offer); }
    }

    // ─── رِحلَةٌ جارِيَةٌ عِندَ الحَذف (‏2026-10-30، الموجَة ١٢ F2 · ٣ — F2B-1) ───────────────────────────────────────────────────────

    private const string OffersDoor = "/{slug}/listings/{id:guid}/offers";

    /// <summary>مَتجَرٌ بِدَورَي الراكِبِ والسائِقِ مِن الكاتالوج، وراكِبٌ نَشَرَ مِشواراً قَبِلَ فيه عَرضَ سائِق (‏<see cref="ListingMatch"/> فَعّالَة)، ومالِكٌ ثالِثٌ قائِمٌ
    /// لِإعلانٍ آخَرَ يَعرِضُ عَلَيه السائِق. والمُطابَقَةُ وَثيقَةٌ كَما يَكتُبُها بابُ القَبول.</summary>
    private static async Task<(User Rider, User Driver, Guid Trip, Guid Other)> SeedTripAsync(Marten.IDocumentStore store, string slug)
    {
        var rider = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "راكِبٌ يُغلِقُ حِسابَه", Phone = "+966500000571", PhoneVerified = true, ActiveRole = "rider" };
        var driver = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "سائِقُ الرِحلَة", Phone = "+966500000572", PhoneVerified = true, ActiveRole = "driver" };
        var third = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "راكِبٌ آخَر", Phone = "+966500000573", PhoneVerified = true, ActiveRole = "rider" };
        var (trip, other) = (Guid.NewGuid(), Guid.NewGuid());
        var now = DateTime.UtcNow;
        await using (var g = store.LightweightSession())
        {
            g.Store(new ACommerce.Kit.Tenants.Tenant
            {
                Id = slug, Name = "مَتجَرُ المَشاوير", OwnerUserId = Guid.NewGuid(),
                Roles = new List<ACommerce.Kit.Roles.Role>
                {
                    ACommerce.Kit.Roles.RoleCatalog.InstantiateRole(ACommerce.Kit.Roles.RoleCatalog.Find("rider")!, 0),
                    ACommerce.Kit.Roles.RoleCatalog.InstantiateRole(ACommerce.Kit.Roles.RoleCatalog.Find("driver")!, 1),
                },
            });
            await g.SaveChangesAsync();
        }
        await using var s = store.LightweightSession(slug);
        s.Store(rider, driver, third);
        s.Events.StartStream<ACommerce.Kit.Listings.Listing>(trip, new ACommerce.Kit.Listings.ListingCreated(trip, slug, "مِشوارُ الراكِب", null, 30m, "rides",
            null, null, new Dictionary<string, string> { [ListingEditService.OwnerAttribute] = rider.Id.ToString(), ["accepts_offers"] = "true" }, now));
        s.Events.StartStream<ACommerce.Kit.Listings.Listing>(other, new ACommerce.Kit.Listings.ListingCreated(other, slug, "مِشوارٌ آخَر", null, 40m, "rides",
            null, null, new Dictionary<string, string> { [ListingEditService.OwnerAttribute] = third.Id.ToString(), ["accepts_offers"] = "true" }, now));
        s.Store(new ListingMatch { Id = trip, AcceptedOfferId = Guid.NewGuid(), OffererId = driver.Id, OffererName = driver.FullName, AcceptedPrice = 30m, MatchedAt = now });
        await s.SaveChangesAsync();
        return (rider, driver, trip, other);
    }

    private static async Task CleanTripAsync(Marten.IDocumentStore store, string slug, params Guid[] streams)
    {
        await using (var s = store.LightweightSession(slug))
        {
            s.DeleteWhere<ListingMatch>(m => m.Id != Guid.Empty);
            s.DeleteWhere<Offer>(o => o.Id != Guid.Empty);
            s.DeleteWhere<ACommerce.Kit.Listings.Listing>(l => l.Id != Guid.Empty);
            foreach (var id in streams) s.Events.ArchiveStream(id);
            foreach (var o in await Marten.QueryableExtensions.ToListAsync(s.Query<Offer>())) s.Events.ArchiveStream(o.Id);
            await s.SaveChangesAsync();
        }
        await LiveZeroRoleAdminProofTests.CleanAsync(store, slug);
    }

    private static (string, string)[] OfferForm()
        => new[] { ("price", "25"), ("lat", "24.7"), ("lng", "46.7"), ("ttl_minutes", "15"), ("as", "driver") };

    /// <summary><b>تَوصيفٌ قَبلَ التَبديل (القاعِدَة ٣) — F2B-1</b>: راكِبٌ لَه رِحلَةٌ جارِيَةٌ (مُطابَقَةٌ فَعّالَةٌ بِلا صَفقَة) يَحذِفُ حِسابَه فَيَمُرّ — <c>open_deal</c> يَعُدُّ
    /// الصَفَقاتِ وَحدَها — فَيُسحَبُ مِشوارُه وتَبقى المُطابَقَةُ فَعّالَة؛ ثُمَّ كُلُّ عَرضٍ لِلسائِقِ عَلى إعلانٍ آخَرَ يُحَوَّلُ إلى الإعلانِ المَحذوفِ بِـ<c>active_trip</c>، وصَفحَةُ
    /// الإعلانِ المَحذوفِ تَرسُمُ «حُذِف» وَحدَه بِلا استِمارَةِ إنهاءٍ ولا إلغاء — حالٌ مَيِّتَةٌ لا تُخرِجُ مِنها نَقرَة. يَنقَلِبُ بِاسمِه في كوميتِ التَبديل.</summary>
    [Fact]
    public async Task Today_a_rider_with_an_active_trip_deletes_and_the_driver_is_sent_to_the_deleted_listing_on_every_offer_on_Postgres()
    {
        if (!Enabled) return;
        var slug = "zz-trip-" + Guid.NewGuid().ToString("N")[..8];
        await using var host = LiveTemplateDoorHost.Build();
        var (rider, driver, trip, other) = await SeedTripAsync(host.Store, slug);
        try
        {
            var riderToken = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(rider.Id, slug, rider.SessionVersion);
            var (delStatus, delAt) = await host.PostFormAsync(DeleteDoor, new Dictionary<string, string> { ["slug"] = slug }, slug, riderToken, Confirm());
            var driverToken = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(driver.Id, slug, driver.SessionVersion);
            var (offStatus, offAt) = await host.PostFormAsync(OffersDoor, new Dictionary<string, string> { ["slug"] = slug, ["id"] = other.ToString() }, slug, driverToken, OfferForm());

            await using var q = host.Store.QuerySession(slug);
            var listing = (await q.LoadAsync<ACommerce.Kit.Listings.Listing>(trip))!;
            var match = (await q.LoadAsync<ListingMatch>(trip))!;
            var page = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
                "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "TenantListingDetail.razor")));
            var deletedBranch = page.IndexOf("else if (listing.IsDeleted)", StringComparison.Ordinal);
            var branch = page[deletedBranch..page.IndexOf("else", deletedBranch + "else if (listing.IsDeleted)".Length, StringComparison.Ordinal)];
            output.WriteLine($"حَذفُ الراكِب: {delStatus} → {delAt} · مِشوارُه مَحذوف: {listing.IsDeleted} · المُطابَقَة: {match.Status} · " +
                             $"عَرضُ السائِقِ عَلى إعلانٍ آخَر: {offStatus} → {offAt} · فَرعُ الإعلانِ المَحذوفِ يَرسُمُ استِمارَةَ رِحلَة: {branch.Contains("/trips/", StringComparison.Ordinal)}");
            Assert.Equal((302, $"/{slug}?deleted=1"), (delStatus, delAt));
            Assert.True(listing.IsDeleted);
            Assert.Equal(ACommerce.Kit.Offers.TripStatus.Active, match.Status);
            Assert.Equal((302, $"/{slug}/listings/{trip}?err=active_trip"), (offStatus, offAt));
            Assert.Contains("listings.detail.deleted", branch, StringComparison.Ordinal);
            Assert.DoesNotContain("/trips/", branch, StringComparison.Ordinal);
        }
        finally { await CleanTripAsync(host.Store, slug, trip, other); }
    }
}
