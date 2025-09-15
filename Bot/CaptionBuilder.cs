using Data.Entities;
using System.Text.RegularExpressions;

namespace Bot
{
    public static class CaptionBuilder
    {
        private const int TelegramCaptionLimit = 1024;

        public static string Build(PostData data, bool paid, string botUsername)
        {
            var title = Sanitize(data.Title);
            var price = Sanitize(data.Price);
            var description = Sanitize(data.Description);
            var url = data.SourceUrl ?? "";

            string templateEnd =
                $"\n👉 <a href=\"{url}\">Детальніше</a>\n🧾 Розміщено через {botUsername}";

            string BuildCaption(string t, string d) => $"<b>{t}</b>\n{price}\n\n{d}{templateEnd}";

            // 1) як є
            var caption = BuildCaption(title, description);
            if (caption.Length <= TelegramCaptionLimit) return caption;

            // 2) обрізати опис
            var overheadWithoutDesc = BuildCaption(title, "").Length;
            var allowedDesc = TelegramCaptionLimit - overheadWithoutDesc;
            if (allowedDesc > 0)
            {
                var trimmedDesc = description.Length > allowedDesc
                    ? description.Substring(0, allowedDesc - 3) + "..."
                    : description;

                caption = BuildCaption(title, trimmedDesc);
                if (caption.Length <= TelegramCaptionLimit) return caption;
            }

            // 3) прибрати опис
            caption = BuildCaption(title, "");
            if (caption.Length <= TelegramCaptionLimit) return caption;

            // 4) обрізати заголовок
            var staticOverhead = BuildCaption("", "").Length;
            var allowedTitle = TelegramCaptionLimit - staticOverhead;
            var safeTitle = allowedTitle <= 0 ? "" :
                (title.Length > allowedTitle ? title.Substring(0, allowedTitle - 3) + "..." : title);

            caption = BuildCaption(safeTitle, "");
            if (caption.Length <= TelegramCaptionLimit) return caption;

            // 5) fallback plain
            var plain = $"{safeTitle}\n{price}\n\n{templateEnd}";
            return plain.Length <= TelegramCaptionLimit ? plain : plain.Substring(0, TelegramCaptionLimit);
        }

        private static string Sanitize(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            var s = input.Trim();

            // Прибираємо HTML-теги, залишаємо текст
            s = Regex.Replace(s, "<.*?>", " ");

            // Прибираємо зайві пробіли
            s = Regex.Replace(s, @"\s+", " ").Trim();

            return s;
        }
    }
}
