using Data.Entities;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Services
{
    public static class OlxParser
    {
        public static async Task<PostData> ParseOlxAsync(string url)
        {
            var web = new HtmlWeb();
            var doc = await web.LoadFromWebAsync(url);

            // ---------- Title ----------
            string title = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(title))
            {
                var titleNode = doc.DocumentNode.SelectSingleNode("//title");
                title = titleNode?.InnerText?.Trim() ?? "Без назви";
            }

            // ---------- Price ----------
            string price = "";

            var priceNode = doc.DocumentNode
                .Descendants()
                .FirstOrDefault(n => n.InnerText.Contains("грн") || n.InnerText.Contains("₴"));

            if (priceNode != null)
            {
                var priceMatch = Regex.Match(priceNode.InnerText, @"([\d\s]+(?:[.,]\d+)?)\s*(грн|₴)");
                if (priceMatch.Success)
                    price = priceMatch.Groups[1].Value.Replace(" ", "") + " грн";
            }

            if (string.IsNullOrWhiteSpace(price))
            {
                var m = Regex.Match(title, @"([\d\s]+(?:[.,]\d+)?)\s*грн", RegexOptions.IgnoreCase);
                if (m.Success)
                    price = m.Groups[1].Value.Replace(" ", "") + " грн";
            }

            if (string.IsNullOrWhiteSpace(price))
                price = "Ціна не вказана";

            // ---------- Description ----------
            string description = "";

            var descNode = doc.DocumentNode.SelectSingleNode("//div[@data-testid='ad_description']");

            if (descNode != null)
            {
                var textNode = descNode.SelectSingleNode(".//div[contains(@class, 'css-19duwlz')]");
                if (textNode != null)
                {
                    string rawHtml = textNode.InnerHtml;
                    rawHtml = rawHtml.Replace("<br>", "\n").Replace("<br/>", "\n").Replace("<br />", "\n");
                    var descriptionRaw = HtmlEntity.DeEntitize(Regex.Replace(rawHtml, "<.*?>", ""));
                    description = Clean(descriptionRaw);
                }
            }

            if (string.IsNullOrWhiteSpace(description))
                description = "Без опису";

            // ---------- Image ----------
            string? imageUrl = doc.DocumentNode
                .SelectSingleNode("//meta[@name='twitter:image']")?
                .GetAttributeValue("content", null);

            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                imageUrl = doc.DocumentNode
                    .SelectSingleNode("//meta[@property='og:image']")?
                    .GetAttributeValue("content", null);
            }

            return new PostData
            {
                Title = Clean(title),
                Price = price,
                Description = description,
                ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl,
                SourceUrl = url,
            };
        }

        private static string Clean(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            var cleaned = HtmlEntity.DeEntitize(text);
            cleaned = Regex.Replace(cleaned, @"{[^}]*}", " ");
            cleaned = Regex.Replace(cleaned, @"[^\w\s\d.,–:!?""\-—()]", " ");
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            return cleaned;
        }
        
    }
}
