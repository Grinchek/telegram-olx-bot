using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using Data.Entities;

namespace Services
{
    public static class BesplatkaParser
    {
        /// <summary>
        /// Парсер сторінок Besplatka/BON (besplatka.ua, bon.ua).
        /// Повертає PostData з Title/Description/Price/ImageUrl/SourceUrl.
        /// </summary>
        public static async Task<PostData> ParseAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("Порожнє посилання.");

            var normalizedUrl = NormalizeUrl(url);
            if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var baseUri))
                throw new ArgumentException("Невалідне посилання.");

            var host = baseUri.Host.ToLowerInvariant();
            if (!(host.Contains("besplatka.ua") || host.Contains("bon.ua")))
                throw new InvalidOperationException("Це не сторінка Besplatka/BON.");

            var web = new HtmlWeb
            {
                PreRequest = req =>
                {
                    req.UserAgent =
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
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
                throw new Exception("Не вдалося завантажити сторінку Besplatka/BON.", ex);
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

            // JSON-LD (image / image.url / array)
            foreach (var img in JsonLdImages(doc))
                Add(img);

            // DOM fallbacks (галерея/товарні фото)
            // 1) активний слайд у галереї
            Add(Attr(doc, "(//div[contains(@class,'slick-current') or contains(@class,'swiper-slide-active')]//img[@src or @data-src or @srcset])[1]", "src"));
            Add(Attr(doc, "(//div[contains(@class,'slick-current') or contains(@class,'swiper-slide-active')]//img[@src or @data-src or @srcset])[1]", "data-src"));

            // 2) перше «адекватне» зображення в картці/контенті (не іконка)
            var imgXpath = "(//img[not(contains(@class,'icon')) and not(contains(@class,'sprite')) and (@src or @data-src or @srcset)])[1]";
            var rawSrc = Attr(doc, imgXpath, "src") ?? Attr(doc, imgXpath, "data-src");
            if (string.IsNullOrWhiteSpace(rawSrc))
            {
                // srcset (візьмемо перший url)
                var srcset = Attr(doc, imgXpath, "srcset");
                rawSrc = FirstFromSrcset(srcset);
            }
            Add(rawSrc);

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

            if (string.IsNullOrWhiteSpace(post.Title))
                throw new Exception("Не вдалося визначити назву оголошення на Besplatka/BON.");

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

            // обробка //cdn...
            if (maybe.StartsWith("//")) maybe = baseUri.Scheme + ":" + maybe;

            if (Uri.TryCreate(maybe, UriKind.Absolute, out var abs))
                return abs.ToString();

            if (maybe.StartsWith("/"))
            {
                var joined = new Uri(baseUri, maybe);
                return joined.ToString();
            }

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
            // Поширені варіанти блоків опису
            var xpathCandidates = new[]
            {
                "//div[contains(@class,'description')]",
                "//div[contains(@class,'product-description')]",
                "//section[contains(@class,'description')]",
                "//section[contains(@class,'product') and contains(@class,'details')]",
                "(//p[string-length(normalize-space(.))>80])[1]"
            };

            foreach (var xp in xpathCandidates)
            {
                var node = doc.DocumentNode.SelectSingleNode(xp);
                if (node == null) continue;
                var t = node.InnerText ?? "";
                t = HtmlEntity.DeEntitize(t);
                t = Regex.Replace(t, @"\s+", " ").Trim();
                if (!string.IsNullOrWhiteSpace(t)) return t;
            }
            return null;
        }

        private static string? ExtractPrice(string allText)
        {
            if (string.IsNullOrWhiteSpace(allText)) return null;

            // Варіанти: "1 200 грн", "1200₴", "1,200 UAH"
            var patterns = new[]
            {
                @"(?<!\d)([\d\s]{2,}(?:[.,]\d{1,2})?)\s*(грн|₴)", // 1 200 грн / 1200₴
                @"(?<!\d)([\d\s]{2,}(?:[.,]\d{1,2})?)\s*UAH"      // 1 200 UAH
            };

            foreach (var p in patterns)
            {
                var m = Regex.Match(allText, p, RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    var num = m.Groups[1].Value.Replace(" ", "");
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

        private static string? FirstFromSrcset(string? srcset)
        {
            if (string.IsNullOrWhiteSpace(srcset)) return null;
            // Формати типу: "https://... 1x, https://... 2x" або "url 320w, url 640w"
            var parts = srcset.Split(',').Select(p => p.Trim()).ToList();
            foreach (var p in parts)
            {
                var space = p.IndexOf(' ');
                var url = space > 0 ? p.Substring(0, space) : p;
                if (!string.IsNullOrWhiteSpace(url)) return url;
            }
            return null;
        }
    }
}
