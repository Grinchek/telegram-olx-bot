using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using Data.Entities;

namespace Services
{
    public static class KidstaffParser
    {
        /// <summary>
        /// Парсер сторінок KidStaff (kidstaff.com.ua).
        /// Повертає PostData з Title/Description/Price/ImageUrl/SourceUrl.
        /// </summary>
        public static async Task<PostData> ParseAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("Порожнє посилання.");

            var normalizedUrl = NormalizeUrl(url);
            if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var baseUri))
                throw new ArgumentException("Невалідне посилання.");

            if (!baseUri.Host.Contains("kidstaff.com.ua", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Це не сторінка KidStaff.");

            var web = new HtmlWeb
            {
                PreRequest = req =>
                {
                    req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                                    "AppleWebKit/537.36 (KHTML, like Gecko) " +
                                    "Chrome/119.0.0.0 Safari/537.36";
                    req.Headers["Accept-Language"] = "uk-UA,uk;q=0.9,en;q=0.8";
                    return true;
                }
            };

            HtmlDocument doc;
            try
            {
                doc = await web.LoadFromWebAsync(normalizedUrl);
            }
            catch (Exception ex)
            {
                throw new Exception("Не вдалося завантажити сторінку KidStaff.", ex);
            }

            // ===== Title =====
            var title = Meta(doc, "property", "og:title")
                        ?? Text(doc, "//title")
                        ?? Text(doc, "//h1")
                        ?? "Без назви";

            title = Clean(title);

            // ===== Description =====
            var description = Meta(doc, "property", "og:description")
                              ?? Meta(doc, "name", "description")
                              ?? GuessDescription(doc)
                              ?? "Без опису";

            description = Clean(description);

            // ===== Price =====
            var price = ExtractPrice(doc.DocumentNode.InnerText) ?? "Ціна не вказана";

            // ===== Image =====
            var imageCandidates = new List<string>();

            void Add(string? u)
            {
                if (string.IsNullOrWhiteSpace(u)) return;
                var abs = MakeAbsoluteUrl(baseUri, u.Trim());
                if (abs != null && !imageCandidates.Contains(abs))
                    imageCandidates.Add(abs);
            }

            // OG / Twitter
            Add(Meta(doc, "property", "og:image"));
            Add(Meta(doc, "name", "twitter:image"));
            Add(Meta(doc, "name", "twitter:image:src"));

            // JSON-LD (image / image.url / first array item)
            foreach (var img in JsonLdImages(doc))
                Add(img);

            // DOM fallbacks (перше адекватне зображення)
            Add(Attr(doc, "(//img[@src])[1]", "src"));
            Add(Attr(doc, "(//meta[@itemprop='image' or @property='image' or @name='image'])[1]", "content"));

            var imageUrl = imageCandidates.FirstOrDefault();

            // ===== Compose =====
            var post = new PostData
            {
                Title = title,
                Description = description,
                Price = price,
                ImageUrl = imageUrl,
                SourceUrl = normalizedUrl
            };

            // Минимальна валідація — хоча б тайтл
            if (string.IsNullOrWhiteSpace(post.Title))
                throw new Exception("Не вдалося визначити назву товару на KidStaff.");

            return post;
        }

        // ----------------- Helpers -----------------

        private static string NormalizeUrl(string url)
        {
            var t = url.Trim();
            if (!t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                t = "https://" + t;
            }
            return t;
        }

        private static string? MakeAbsoluteUrl(Uri baseUri, string? maybe)
        {
            if (string.IsNullOrWhiteSpace(maybe)) return null;

            // деякі сайти віддають //img.domain/...
            if (maybe.StartsWith("//")) maybe = baseUri.Scheme + ":" + maybe;

            if (Uri.TryCreate(maybe, UriKind.Absolute, out var abs))
                return abs.ToString();

            if (maybe.StartsWith("/"))
            {
                var joined = new Uri(baseUri, maybe);
                return joined.ToString();
            }

            // інші відносні шляхи
            try
            {
                var joined = new Uri(baseUri, maybe);
                return joined.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string? Meta(HtmlDocument doc, string attrName, string attrValue)
        {
            return doc.DocumentNode
                      .SelectSingleNode($"//meta[@{attrName}='{attrValue}']")
                      ?.GetAttributeValue("content", null);
        }

        private static string? Attr(HtmlDocument doc, string xpath, string attr)
        {
            return doc.DocumentNode.SelectSingleNode(xpath)?.GetAttributeValue(attr, null);
        }

        private static string? Text(HtmlDocument doc, string xpath)
        {
            var node = doc.DocumentNode.SelectSingleNode(xpath);
            if (node == null) return null;
            var t = node.InnerText ?? "";
            t = HtmlEntity.DeEntitize(t);
            t = Regex.Replace(t, @"\s+", " ").Trim();
            return string.IsNullOrWhiteSpace(t) ? null : t;
        }

        private static string Clean(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var t = HtmlEntity.DeEntitize(s);
            t = Regex.Replace(t, @"\s+", " ").Trim();
            return t;
        }

        private static string? GuessDescription(HtmlDocument doc)
        {
            // Часто опис знаходиться в блоках <div> навколо контенту.
            // Візьмемо перший «великий» параграф, якщо meta немає.
            var node = doc.DocumentNode.SelectSingleNode("(//p[string-length(normalize-space(.))>60])[1]")
                       ?? doc.DocumentNode.SelectSingleNode("(//div[string-length(normalize-space(.))>120])[1]");
            if (node == null) return null;
            var t = node.InnerText ?? "";
            t = HtmlEntity.DeEntitize(t);
            t = Regex.Replace(t, @"\s+", " ").Trim();
            return string.IsNullOrWhiteSpace(t) ? null : t;
        }

        private static string? ExtractPrice(string allText)
        {
            if (string.IsNullOrWhiteSpace(allText)) return null;

            // Поширені варіанти: "1 200 грн", "1200₴", "1,200 UAH"
            var patterns = new[]
            {
                @"(?<!\d)([\d\s]{2,}(?:[.,]\d{1,2})?)\s*(грн|₴)",   // 1 200 грн / 1200₴
                @"(?<!\d)([\d\s]{2,}(?:[.,]\d{1,2})?)\s*UAH"        // 1 200 UAH
            };

            foreach (var p in patterns)
            {
                var m = Regex.Match(allText, p, RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    var num = m.Groups[1].Value.Replace(" ", "");
                    // уніфікуємо роздільник
                    num = num.Replace(",", ".");
                    return $"{num} грн";
                }
            }

            return null;
        }

        private static IEnumerable<string> JsonLdImages(HtmlDocument doc)
        {
            var list = new List<string>();
            var nodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
            if (nodes == null) return list;

            foreach (var n in nodes)
            {
                try
                {
                    var json = n.InnerText;
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    var token = JToken.Parse(json);
                    var roots = token is JArray arr ? arr.ToArray() : new[] { token };

                    foreach (var r in roots)
                    {
                        var img = r.SelectToken("image");
                        if (img == null) continue;

                        switch (img.Type)
                        {
                            case JTokenType.String:
                                list.Add(img.Value<string>()!);
                                break;

                            case JTokenType.Array:
                                foreach (var s in img.Values<string>())
                                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                                break;

                            case JTokenType.Object:
                                var u = img["url"]?.Value<string>();
                                if (!string.IsNullOrWhiteSpace(u)) list.Add(u);
                                break;
                        }
                    }
                }
                catch
                {
                    // ігноруємо некоректний JSON-LD
                }
            }

            return list;
        }
    }
}
