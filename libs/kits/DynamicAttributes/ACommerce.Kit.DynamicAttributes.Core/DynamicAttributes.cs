namespace ACommerce.Kit.DynamicAttributes;

/// <summary>قيمَة سِمَة (key→string). تَظهَر داخل <c>Listing.Attributes</c>
/// كَ Dictionary&lt;string,string&gt;. الـ template كَيف تَظهر يُحَدِّده
/// <c>Tenant.Categories[i].Attributes</c> (مَوجود).</summary>
public sealed record AttributeValue(string Key, string Label, string Type, string? Value);
