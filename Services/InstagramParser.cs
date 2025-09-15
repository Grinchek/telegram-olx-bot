using Data.Entities;
using HtmlAgilityPack;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Services
{
    public static class InstagramParser
    {
        public static async Task<PostData> ParseInstagramAsync(string url)
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

            // ---------- Title / Caption ----------
            var title = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", "")
                        ?? doc.DocumentNode.SelectSingleNode("//title")?.InnerText
                        ?? "Без назви";

            // ---------- Description ----------
            var description = doc.DocumentNode.SelectSingleNode("//meta[@property='og:description']")?.GetAttributeValue("content", "")
                               ?? "Без опису";

            // ---------- Image ----------
            var imageUrl = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", null);

            // Instagram зазвичай не показує ціну → ставимо заглушку
            var price = "Ціна не вказана";

            return new PostData
            {
                Title = title.Trim(),
                Price = price,
                Description = description.Trim(),
                ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl,
                SourceUrl = url
            };
        }
    }
}
