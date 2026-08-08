namespace ACommerce.Widgets;

/// <summary>
/// تَحويل الإيموجي المُلَوَّن (المُخَزَّن في DB لِلفِئات/الأَدوار) إلى اسم
/// أَيقونَة AcIcon خَطِّيَّة SVG. القاعِدَة: لا إيموجي مُلَوَّن في الواجِهَة —
/// كُلّ الأَيقونات خَطِّيَّة مُوَحَّدَة (ذَوق Linear/Notion).
///
/// إن لَم يُعرَف الإيموجي، نَرجِع أَيقونَة عامَّة ("tag") بَدَلاً مِن عَرض
/// الإيموجي الخامّ. لِلأَسماء النَّصِّيَّة الأَصلِيَّة ("home") نُعيدها كَما هي.
/// </summary>
public static class AcIconMap
{
    private static readonly Dictionary<string, string> _emojiToIcon = new()
    {
        ["🏠"] = "home",      ["🏡"] = "house-heart", ["🏢"] = "building",
        ["🏪"] = "store",     ["🚪"] = "house-add",   ["🛌"] = "house-heart",
        ["🚗"] = "car",       ["📦"] = "package",     ["💼"] = "bag",
        ["🛒"] = "cart",      ["🎉"] = "star",        ["🔎"] = "search",
        ["🔍"] = "search",    ["☕"] = "cup",         ["🍰"] = "cup",
        ["🥐"] = "cup",       ["🍽️"] = "cup",        ["🍽"] = "cup",
        ["🥤"] = "cup",       ["👤"] = "user",        ["👥"] = "people",
        ["🧍"] = "user",      ["🚙"] = "car",         ["🛍️"] = "bag",
        ["🛍"] = "bag",       ["📋"] = "receipt",     ["🎫"] = "tag",
        ["💡"] = "lightbulb", ["⭐"] = "star",        ["★"] = "star",
        ["🗂️"] = "list",      ["🗂"] = "list",        ["🗺️"] = "map",
        ["🗺"] = "map",       ["🧬"] = "sliders",     ["📱"] = "phone",
        ["🚚"] = "truck",     ["🚛"] = "truck",       ["📅"] = "calendar",
        ["💳"] = "credit-card", ["💰"] = "wallet",    ["💵"] = "cash",
        ["📄"] = "file",      ["📃"] = "file",        ["🖼️"] = "image",
        ["🖼"] = "image",     ["📷"] = "image",       ["👋"] = "hand-wave",
        ["✨"] = "sparkle",   ["✦"] = "sparkle",      ["🎯"] = "target",
        ["⚠️"] = "alert-circle", ["⚠"] = "alert-circle", ["🪪"] = "key",
        ["🔄"] = "refresh",   ["⬇️"] = "download",    ["📈"] = "trending-up",
        ["📉"] = "bar-chart", ["🛡️"] = "shield",      ["🛡"] = "shield",
        ["✏️"] = "edit",      ["✏"] = "edit",         ["🗑️"] = "trash",
        ["🗑"] = "trash",     ["✅"] = "check",        ["✓"] = "check",
        ["✔️"] = "check",     ["✔"] = "check",        ["✕"] = "x",
        ["✖️"] = "x",         ["✖"] = "x",            ["❌"] = "x",
        ["🍵"] = "cup",       ["🎫"] = "ticket",      ["🧾"] = "receipt",
        ["📊"] = "bar-chart",
    };

    /// <summary>اسم أَيقونَة SVG مُقابِل إيموجي أَو اسم نَصّيّ. الافتِراضيّ
    /// عِندَ مَجهول: <paramref name="fallback"/> (لا الإيموجي الخام).</summary>
    public static string Resolve(string? icon, string fallback = "tag")
    {
        if (string.IsNullOrWhiteSpace(icon)) return fallback;
        icon = icon.Trim();
        // اسم نَصّيّ بِالفِعل (أَحرُف لاتينيَّة/أَرقام/شَرطَة) → كَما هو.
        if (System.Text.RegularExpressions.Regex.IsMatch(icon, "^[a-z0-9-]+$"))
            return icon;
        // إيموجي مَعروف → أَيقونَتُه؛ وإلّا الافتِراضيّ.
        return _emojiToIcon.TryGetValue(icon, out var name) ? name : fallback;
    }
}
