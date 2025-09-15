using Data.Entities;
using System.Net;

namespace Bot;

public static class CaptionBuilder
{
    public static string Build(PostData data, bool paid, string botUsername)
    {
        var title = Escape(data.Title);
        var price = Escape(data.Price);
        var description = Escape(data.Description);
        var url = Escape(data.SourceUrl);

        var templateStart = $"<b>{title}</b>\n{price}\n\n";
        var templateEnd =
            $"\n👉 <a href=\"{url}\">Детальніше</a>\n🧾 Розміщено через {botUsername}";

        var maxLength = 1024 - (templateStart.Length + templateEnd.Length);

        if (description.Length > maxLength)
            description = description.Substring(0, maxLength - 3) + "...";

        return templateStart + description + templateEnd;
    }



    private static string Escape(string? input)
    {
        return string.IsNullOrWhiteSpace(input)
            ? ""
            : WebUtility.HtmlEncode(input);
    }
}
