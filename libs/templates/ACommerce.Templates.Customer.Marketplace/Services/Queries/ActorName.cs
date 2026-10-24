namespace ACommerce.Templates.Customer.Marketplace.Services.Queries;

/// <summary>
/// <para><b>اسمُ الفاعِلِ الَّذي يُكتَبُ في سِجِلٍّ يَراه غَيرُه — مِن وَثيقَتِه وَقتَ الكِتابَة</b> (‏2026-10-24، تَدقيقُ الإطلاق lens-security-11).
/// كانَت النِقاطُ تَأخُذُه مِن كوكي <c>.name</c> — خارِجَ رَمزِ HMAC، يَكتُبُه العَميلُ كَما يَشاء — فَيَصيرُ سَطرَ الخَطِّ الزَمَنِيِّ في الصَفقَة،
/// واسمَ كاتِبِ تَقييمٍ «موثَّق»، وفاعِلَ سَطرِ تَدقيق. صارَ يُقرَأُ مِن <c>User.FullName</c> في مَتجَرِه (<c>AccountQueries.ActorNameAsync</c>)
/// أَو <c>StudioUser.FullName</c> (<c>StudioQueries.ActorNameAsync</c>)، وهذِه القاعِدَةُ الواحِدَةُ بَينَهُما: الاسمُ مَقصوصاً، والفارِغُ غِياب —
/// فَيَسقُطُ كُلُّ بابٍ إلى ما كانَ يَسقُطُ إلَيه حينَ يَغيبُ الكوكي.</para>
///
/// <para><b>والكوكي باقٍ لِلعَرضِ وَحدَه</b>: <c>AuthSession.UserName</c> و<c>StudioAuth.UserName</c> يُرسَمانِ لِصاحِبِ الجَلسَةِ نَفسِه («مَرحَباً
/// …»)، ولا يُكتَبانِ في سِجِلٍّ يَراه غَيرُه — مَحروسٌ في <c>ActorNameSourceTests</c>.</para>
/// </summary>
public static class ActorName
{
    public static string? Of(string? fullName)
        => string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
}
