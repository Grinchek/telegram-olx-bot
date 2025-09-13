using Data.Entities;
using HtmlAgilityPack;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Services
{
    public static class ShafaParser
    {
        public static async Task<PostData> ParseShafaAsync(string url)
        {
            var web = new HtmlWeb
            {
                PreRequest = req =>
                {
                    req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36";
                    req.Headers["Accept-Language"] = "uk-UA,uk;q=0.9,en;q=0.8";
                    return true;
                }
            };
            var doc = await web.LoadFromWebAsync(url);


            // ---------- Title ----------
            // 1) og:title (найстабільніше)
            string title =
                doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", null)
                ?? doc.DocumentNode.SelectSingleNode("//title")?.InnerText
                ?? doc.DocumentNode.SelectSingleNode("//h1")?.InnerText
                ?? "Без назви";

            title = Clean(title);

            // ---------- Price ----------
            // На shafa: <p class="azxZhr" data-product-price="12700 грн">12700 грн</p>
            string price = "";
            var priceNode = doc.DocumentNode.SelectSingleNode("//p[contains(@class,'azxZhr') and @data-product-price]")
                           ?? doc.DocumentNode.SelectSingleNode("//*[@data-product-price]");
            if (priceNode != null)
            {
                var raw = priceNode.GetAttributeValue("data-product-price", priceNode.InnerText ?? "");
                var m = Regex.Match(raw, @"([\d\s]+(?:[.,]\d+)?)\s*(грн|₴)", RegexOptions.IgnoreCase);
                if (m.Success) price = m.Groups[1].Value.Replace(" ", "") + " грн";
            }
            if (string.IsNullOrWhiteSpace(price))
            {
                // Фолбек — шукаємо будь-де «... грн/₴»
                var any = doc.DocumentNode.InnerText;
                var m = Regex.Match(any, @"([\d\s]+(?:[.,]\d+)?)\s*(грн|₴)", RegexOptions.IgnoreCase);
                price = m.Success ? m.Groups[1].Value.Replace(" ", "") + " грн" : "Ціна не вказана";
            }

            // ---------- Description ----------
            // На shafa опис часто в <p class="xWgNd3"> ... </p>; також є og:description
            string description =
                doc.DocumentNode.SelectSingleNode("//p[contains(@class,'xWgNd3')]")?.InnerText
                ?? doc.DocumentNode.SelectSingleNode("//meta[@property='og:description']")?.GetAttributeValue("content", null)
                ?? "";
            description = Clean(description);
            if (string.IsNullOrWhiteSpace(description)) description = "Без опису";

            // ---------- Image ----------
            // 1) og:image; 2) <img data-product-photo src="...">
            string? imageUrl = null;

            // 1) головне фото товару у галереї
            var imgNode = doc.DocumentNode.SelectSingleNode("//img[@data-product-photo]")
                      ?? doc.DocumentNode.SelectSingleNode("//img[contains(@src,'image-thumbs.shafastatic.net')]");

            if (imgNode != null)
            {
                imageUrl = imgNode.GetAttributeValue("data-product-photo", null)
                        ?? imgNode.GetAttributeValue("src", null);
            }

            // 2) якщо все ще немає — regex по всьому HTML
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                var html = doc.DocumentNode.InnerHtml;
                var m = Regex.Match(html, @"https?:\/\/image-thumbs\.shafastatic\.net\/[^\s""'<>)]+", RegexOptions.IgnoreCase);
                if (m.Success) imageUrl = m.Value;
            }

            // 3) фінальні фолбеки
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                imageUrl = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", null)
                        ?? doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:image']")?.GetAttributeValue("content", null);
            }
            // Якщо немає — хай буде null, як у твоєму OLX-парсері (потім ставиш плейсхолдер)
            return new PostData
            {
                Title = title,
                Price = price,
                Description = description,
                ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl,
                SourceUrl = url
            };
        }

        private static string Clean(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            // Розкодовую HTML-сущності та прибираю зайві символи — у стилі твого OlxParser
            var t = HtmlEntity.DeEntitize(text);
            t = Regex.Replace(t, @"{[^}]*}", " ");
            t = Regex.Replace(t, @"[^\w\s\d.,–:!?""\-—()]", " ");
            t = Regex.Replace(t, @"\s+", " ").Trim();
            return t;
        }
    }
}
