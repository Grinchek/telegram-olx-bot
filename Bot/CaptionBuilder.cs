using Models;
using System.Net;

namespace Bot;

public static class CaptionBuilder
{
    public static string Build(PostData data, bool paid, string botUsername)
    {
        return $"""
    <b>{data.Title}</b>
    {data.Price}

    {data.Description}

    👉 <a href="{data.SourceUrl}">Детальніше</a>
    🧾 Розміщено через {botUsername}
    """;
    }

    private static string Escape(string? input)
    {
        return string.IsNullOrWhiteSpace(input)
            ? ""
            : WebUtility.HtmlEncode(input);
    }
}
