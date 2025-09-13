using Data.Entities;
using HtmlAgilityPack;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

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
            static string? FirstFromSrcset(string? srcset)
     => string.IsNullOrWhiteSpace(srcset) ? null
        : srcset.Split(',')[0].Trim().Split(' ')[0];

            static string? FromImgNode(HtmlNode img)
            {
                return img.GetAttributeValue("data-product-photo", null)
                    ?? img.GetAttributeValue("data-full", null)
                    ?? img.GetAttributeValue("data-large", null)
                    ?? img.GetAttributeValue("data-original", null)
                    ?? img.GetAttributeValue("data-lazy", null)
                    ?? img.GetAttributeValue("data-src", null)
                    ?? img.GetAttributeValue("src", null)
                    ?? FirstFromSrcset(img.GetAttributeValue("data-srcset", null))
                    ?? FirstFromSrcset(img.GetAttributeValue("srcset", null));
            }

            static string? FromStyleBg(string? style)
            {
                if (string.IsNullOrWhiteSpace(style)) return null;
                // шукаємо url("...") або url('...') або url(...)
                var m = Regex.Match(style, @"url\((['""]?)(?<u>[^)'""]+)\1\)", RegexOptions.IgnoreCase);
                return m.Success ? m.Groups["u"].Value : null;
            }

            static void AddCandidate(List<(string url, int score, string note)> bag, string? url, int score, string note)
            {
                if (string.IsNullOrWhiteSpace(url)) return;
                // інколи бувають відносні шляхи – ігноруємо їх
                if (!Uri.IsWellFormedUriString(url, UriKind.Absolute)) return;
                if (!bag.Any(c => c.url == url))
                    bag.Add((url, score, note));
            }

            string? imageUrl = null;
            var candidates = new List<(string url, int score, string note)>();

            // A) JSON-LD: зазвичай містить перше головне фото
            var jsonLdNodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
            if (jsonLdNodes != null)
            {
                foreach (var n in jsonLdNodes)
                {
                    try
                    {
                        var json = n.InnerText;
                        if (string.IsNullOrWhiteSpace(json)) continue;

                        var token = JToken.Parse(json);

                        // буває масив об’єктів
                        IEnumerable<JToken> roots = token is JArray arr ? arr : new[] { token };

                        foreach (var root in roots)
                        {
                            var imageToken = root.SelectToken("image"); // може бути string або array
                            if (imageToken == null) continue;

                            if (imageToken.Type == JTokenType.String)
                            {
                                AddCandidate(candidates, imageToken.Value<string>(), 1000, "ld+json string");
                            }
                            else if (imageToken.Type == JTokenType.Array)
                            {
                                var imgs = imageToken.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                                if (imgs.Count > 0)
                                {
                                    // перше — з максимальним пріоритетом
                                    AddCandidate(candidates, imgs[0], 1000, "ld+json image[0]");
                                    // решта — нижчим пріоритетом
                                    for (int i = 1; i < imgs.Count; i++)
                                        AddCandidate(candidates, imgs[i], 750 - i, $"ld+json image[{i}]");
                                }
                            }
                            else if (imageToken.Type == JTokenType.Object)
                            {
                                var imgUrl = imageToken["url"]?.Value<string>();
                                AddCandidate(candidates, imgUrl, 950, "ld+json object.url");
                            }
                        }
                    }
                    catch { /* пропускаємо парсинг-помилки */ }
                }
            }

            // B) OG/Twitter — часто вказує головне
            AddCandidate(candidates,
                doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", null),
                900, "og:image");
            AddCandidate(candidates,
                doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:image']")?.GetAttributeValue("content", null),
                880, "twitter:image");

            // C) Галерея: беремо img з явним індексом 0 або мінімальним індексом
            var galleryImgs = doc.DocumentNode.SelectNodes("//img[@data-index or @data-photo-index or @data-order]");
            if (galleryImgs != null)
            {
                // спроба взяти data-index == 0
                var index0 = galleryImgs.FirstOrDefault(n =>
                {
                    var t = n.GetAttributeValue("data-index", n.GetAttributeValue("data-photo-index",
                             n.GetAttributeValue("data-order", null)));
                    return t != null && int.TryParse(t, out var i) && i == 0;
                });
                if (index0 != null)
                    AddCandidate(candidates, FromImgNode(index0), 850, "gallery data-index=0");

                // якщо точного 0 немає — знайдемо мінімальний індекс
                var withIndex = galleryImgs
                    .Select(n => new
                    {
                        Node = n,
                        idxText = n.GetAttributeValue("data-index", n.GetAttributeValue("data-photo-index",
                                   n.GetAttributeValue("data-order", null)))
                    })
                    .Where(x => int.TryParse(x.idxText, out _))
                    .Select(x => new { x.Node, idx = int.Parse(x.idxText) })
                    .OrderBy(x => x.idx)
                    .ToList();

                if (withIndex.Count > 0)
                    AddCandidate(candidates, FromImgNode(withIndex.First().Node), 820, "gallery min-index");
            }

            // D) Активний слайд (на випадок коли сайт не дає індексу)
            var activeImg = doc.DocumentNode.SelectSingleNode(
                "(//div[contains(@class,'slick-current') or contains(@class,'swiper-slide-active') or @aria-current='true']//img)[1]");
            if (activeImg != null)
                AddCandidate(candidates, FromImgNode(activeImg), 700, "active slide");

            // E) Перший кандидат з не-прев’юшок
            var firstNonThumb = doc.DocumentNode.SelectSingleNode(
                "(//img[not(contains(@class,'thumb')) and ( @data-product-photo or @data-src or @data-original or @data-lazy or @srcset or @src )])[1]");
            if (firstNonThumb != null)
                AddCandidate(candidates, FromImgNode(firstNonThumb), 680, "first non-thumb <img>");

            // F) Фони з background-image
            var bgNodes = doc.DocumentNode.SelectNodes("//*[contains(@style,'background-image')]");
            if (bgNodes != null)
            {
                foreach (var node in bgNodes)
                    AddCandidate(candidates, FromStyleBg(node.GetAttributeValue("style", null)), 640, "style background-image");
            }

            // G) Regex-фолбек по всьому HTML (розширено на image.shafastatic.net)
            if (!candidates.Any())
            {
                var html = doc.DocumentNode.InnerHtml;
                var m = Regex.Match(html, @"https?:\/\/(?:image|image-thumbs)\.shafastatic\.net\/[^\s""'<>)]+", RegexOptions.IgnoreCase);
                if (m.Success) AddCandidate(candidates, m.Value, 600, "regex shafastatic");
            }

            // H) Якщо все одно нічого — залишимо null (потім плейсхолдер)
            var best = candidates.OrderByDescending(c => c.score).FirstOrDefault();
            imageUrl = best.url;

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
