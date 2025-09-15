using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Services
{
    /// <summary>Єдине місце для завантаження/визначення фото/відео Instagram + відправка в Telegram.</summary>
    public static class InstagramMedia
    {
        private const string Placeholder = "https://via.placeholder.com/300";
        private const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119 Safari/537.36";

        // Якщо прямий запит вертає 403/429/400 — йдемо через цей проксі (напр., Cloudflare Worker)
        private static readonly string? MediaProxyBase = Environment.GetEnvironmentVariable("MEDIA_PROXY_BASE");

        // Живі альтернативні хости (без ddinstagram)
        private static readonly string[] AltHosts = {
            "www.gginstagram.com","gginstagram.com",
            "www.instagramez.com","instagramez.com",
            "www.kkinstagram.com","kkinstagram.com"
        };

        private static readonly Random Rng = new Random();

        public sealed class Sendable
        {
            public string Kind = "photo";                 // photo | video | document | placeholder
            public byte[]? Bytes;                         // якщо null — шлемо URL (placeholder або DirectUrl)
            public string? MediaType;                     // image/jpeg, video/mp4, ...
            public string FileName = "media.bin";
            public string? DirectUrl;                     // коли Bytes == null — напряму за URL
        }

        // ===== публічні API =====
        public static async Task<Sendable> BuildAsync(string? sourceUrl, string? fallbackImageUrl)
        {
            if (IsInstagram(sourceUrl))
            {
                // 1) спроба відео з оригінальної сторінки / альт-хостів (meta + JSON-LD)
                var vUrl = await GetVideoUrlAsync(sourceUrl!);
                if (!string.IsNullOrWhiteSpace(vUrl))
                {
                    var (b, mt) = await FetchAsync(vUrl!, sourceUrl);
                    if (b != null)
                        return new Sendable { Kind = "video", Bytes = b, MediaType = mt, FileName = "video.mp4" };
                }

                // 2) ?dl=1 на альт-хостах (якщо HTML — парсимо meta + JSON-LD та качаємо справжнє медіа)
                var (bAlt, mtAlt) = await TryAltDownloadAsync(sourceUrl!);
                if (bAlt != null)
                {
                    if (IsVideo(mtAlt))
                        return new Sendable { Kind = "video", Bytes = bAlt, MediaType = mtAlt, FileName = "video.mp4" };

                    if (IsImage(mtAlt))
                    {
                        if (IsJpegOrPng(mtAlt))
                            return new Sendable { Kind = "photo", Bytes = bAlt, MediaType = mtAlt, FileName = "photo.jpg" };

                        // webp/avif → документ (щоб уникати IMAGE_PROCESS_FAILED)
                        return new Sendable { Kind = "document", Bytes = bAlt, MediaType = mtAlt, FileName = "image.bin" };
                    }
                }

                // 3) обкладинка з альт-хостів (og:image, twitter:image, JSON-LD image)
                var altImg = await GetAltImageUrlAsync(sourceUrl!);
                if (!string.IsNullOrWhiteSpace(altImg))
                {
                    var (b, mt) = await FetchAsync(altImg!);
                    if (b != null)
                    {
                        if (IsJpegOrPng(mt))
                            return new Sendable { Kind = "photo", Bytes = b, MediaType = mt, FileName = "photo.jpg" };

                        return new Sendable { Kind = "document", Bytes = b, MediaType = mt, FileName = "image.bin" };
                    }
                }
            }

            // 4) Звичайне фото (fallbackImageUrl) — для НЕ-Instagram джерел шлемо як URL,
            // щоб Telegram показав саме фото (а не документ).
            var tryImg = string.IsNullOrWhiteSpace(fallbackImageUrl) ? null : WebUtility.HtmlDecode(fallbackImageUrl);
            if (!string.IsNullOrWhiteSpace(tryImg))
            {
                if (!IsInstagram(sourceUrl))
                {
                    // Спрощений шлях для OLX/Shafa: даємо URL напряму
                    return new Sendable
                    {
                        Kind = "photo",
                        Bytes = null,
                        DirectUrl = tryImg,
                        FileName = "photo.jpg"
                    };
                }

                // Для Instagram все ще краще тягнути байти (реферер/проксі)
                var (b, mt) = await FetchAsync(tryImg!, referer: sourceUrl);
                if (b != null)
                {
                    if (IsJpegOrPng(mt))
                        return new Sendable { Kind = "photo", Bytes = b, MediaType = mt, FileName = "photo.jpg" };

                    if (IsImage(mt))
                        return new Sendable { Kind = "document", Bytes = b, MediaType = mt, FileName = "image.bin" };
                }
            }

            return new Sendable { Kind = "placeholder", DirectUrl = Placeholder };
        }

        public static async Task SendAsync(
            ITelegramBotClient bot, ChatId chat, Sendable media,
            string caption, IReplyMarkup replyMarkup, System.Threading.CancellationToken ct)
        {
            switch (media.Kind)
            {
                case "video":
                    await using (var ms = new MemoryStream(media.Bytes!))
                        await bot.SendVideoAsync(chat, InputFile.FromStream(ms, media.FileName),
                            caption: caption, parseMode: ParseMode.Html,
                            replyMarkup: replyMarkup, supportsStreaming: true, cancellationToken: ct);
                    break;

                case "photo":
                    if (media.Bytes != null)
                    {
                        await using var ps = new MemoryStream(media.Bytes);
                        await bot.SendPhotoAsync(chat, InputFile.FromStream(ps, media.FileName),
                            caption: caption, parseMode: ParseMode.Html,
                            replyMarkup: replyMarkup, cancellationToken: ct);
                    }
                    else if (!string.IsNullOrWhiteSpace(media.DirectUrl))
                    {
                        await bot.SendPhotoAsync(chat, InputFile.FromUri(media.DirectUrl),
                            caption: caption, parseMode: ParseMode.Html,
                            replyMarkup: replyMarkup, cancellationToken: ct);
                    }
                    else
                    {
                        await bot.SendPhotoAsync(chat, InputFile.FromUri(Placeholder),
                            caption: caption, parseMode: ParseMode.Html,
                            replyMarkup: replyMarkup, cancellationToken: ct);
                    }
                    break;

                case "document": // webp/avif → документ, щоб не ловити IMAGE_PROCESS_FAILED
                    await using (var ms = new MemoryStream(media.Bytes!))
                        await bot.SendDocumentAsync(chat, InputFile.FromStream(ms, media.FileName),
                            caption: caption, parseMode: ParseMode.Html,
                            replyMarkup: replyMarkup, cancellationToken: ct);
                    break;

                default:
                    await bot.SendPhotoAsync(chat, InputFile.FromUri(media.DirectUrl ?? Placeholder),
                        caption: caption, parseMode: ParseMode.Html,
                        replyMarkup: replyMarkup, cancellationToken: ct);
                    break;
            }
        }

        // ===== Instagram helpers =====
        private static bool IsInstagram(string? u) =>
            !string.IsNullOrWhiteSpace(u) &&
            Uri.TryCreate(u.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? u : "https://" + u,
                          UriKind.Absolute, out var uri) &&
            uri.Host.Contains("instagram.com", StringComparison.OrdinalIgnoreCase);

        private static async Task<string?> GetVideoUrlAsync(string instagramUrl)
        {
            try
            {
                var web = NewHtmlWeb();

                // основна сторінка
                var doc = await web.LoadFromWebAsync(instagramUrl);
                var (ldVideo, _) = ExtractFromJsonLd(doc);
                var url = ExtractVideoFromMeta(doc) ?? ldVideo;
                if (!string.IsNullOrWhiteSpace(url)) return url;

                // альт-хости
                var u = new Uri(instagramUrl);
                foreach (var host in AltHosts)
                {
                    try
                    {
                        await Task.Delay(Rng.Next(250, 700));
                        var dd = $"https://{host}{u.PathAndQuery}";
                        var doc2 = await web.LoadFromWebAsync(dd);
                        var (ldV2, _) = ExtractFromJsonLd(doc2);
                        var v = ExtractVideoFromMeta(doc2) ?? ldV2;
                        if (!string.IsNullOrWhiteSpace(v)) return v;
                    }
                    catch { /* ignore host */ }
                }
            }
            catch { /* ignore */ }
            return null;
        }

        // повертає ДЕКОДОВАНЕ значення з meta
        private static string? ExtractVideoFromMeta(HtmlDocument doc)
        {
            string? pick(string xpath)
            {
                var node = doc.DocumentNode.SelectSingleNode(xpath);
                if (node == null) return null;
                var val = node.GetAttributeValue("content", null);
                return string.IsNullOrWhiteSpace(val) ? null : WebUtility.HtmlDecode(val);
            }

            return pick("//meta[@property='og:video']")
                ?? pick("//meta[@property='og:video:secure_url']")
                ?? pick("//meta[@name='twitter:player:stream']")
                ?? pick("//meta[contains(@content,'.mp4')]");
        }

        private static (string? video, string? image) ExtractFromJsonLd(HtmlDocument doc)
        {
            var nodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
            if (nodes == null) return (null, null);

            foreach (var n in nodes)
            {
                try
                {
                    var raw = WebUtility.HtmlDecode(n.InnerText);
                    using var j = JsonDocument.Parse(raw);
                    var root = j.RootElement;

                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in root.EnumerateArray())
                        {
                            var r = ExtractFromJsonLdObject(item);
                            if (r.video != null || r.image != null) return r;
                        }
                    }
                    else if (root.ValueKind == JsonValueKind.Object)
                    {
                        var r = ExtractFromJsonLdObject(root);
                        if (r.video != null || r.image != null) return r;
                    }
                }
                catch { /* broken JSON-LD – skip */ }
            }
            return (null, null);
        }

        private static (string? video, string? image) ExtractFromJsonLdObject(JsonElement obj)
        {
            if (obj.ValueKind != JsonValueKind.Object) return (null, null);

            // video.contentUrl
            if (obj.TryGetProperty("video", out var v))
            {
                if (v.ValueKind == JsonValueKind.Object &&
                    v.TryGetProperty("contentUrl", out var c) && c.ValueKind == JsonValueKind.String)
                    return (c.GetString(), null);
            }

            // image: string | array | object{url}
            if (obj.TryGetProperty("image", out var img))
            {
                if (img.ValueKind == JsonValueKind.String) return (null, img.GetString());
                if (img.ValueKind == JsonValueKind.Array)
                {
                    foreach (var it in img.EnumerateArray())
                        if (it.ValueKind == JsonValueKind.String) return (null, it.GetString());
                }
                if (img.ValueKind == JsonValueKind.Object &&
                    img.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
                    return (null, u.GetString());
            }

            // інколи JSON-LD лежить у @graph
            if (obj.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in graph.EnumerateArray())
                {
                    var r = ExtractFromJsonLdObject(it);
                    if (r.video != null || r.image != null) return r;
                }
            }

            return (null, null);
        }

        private static async Task<(byte[]? bytes, string? mediaType)> TryAltDownloadAsync(string instagramUrl)
        {
            try
            {
                var u = new Uri(instagramUrl);
                foreach (var host in AltHosts)
                {
                    await Task.Delay(Rng.Next(250, 700));
                    var dl = $"https://{host}{u.AbsolutePath}?dl=1";
                    var refPage = $"https://{host}{u.PathAndQuery}";
                    var (b, mt) = await FetchAsync(dl, refPage);
                    if (b == null) continue;

                    // якщо прийшла HTML-сторінка — парсимо та дістаємо реальне медіа (meta + JSON-LD)
                    if (!string.IsNullOrWhiteSpace(mt) && mt.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var html = Encoding.UTF8.GetString(b);
                            var doc = new HtmlDocument(); doc.LoadHtml(html);

                            var (ldV, ldI) = ExtractFromJsonLd(doc);
                            var cand = ExtractVideoFromMeta(doc) ?? ldV
                                       ?? doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", null)
                                       ?? doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:image']")?.GetAttributeValue("content", null)
                                       ?? ldI
                                       ?? doc.DocumentNode.SelectSingleNode("//a[contains(@href,'.mp4') or contains(@href,'.jpg') or contains(@href,'.jpeg') or contains(@href,'.png') or contains(@href,'.webp')]")
                                                           ?.GetAttributeValue("href", null);

                            cand = string.IsNullOrWhiteSpace(cand) ? null : WebUtility.HtmlDecode(cand);
                            if (!string.IsNullOrWhiteSpace(cand))
                            {
                                var abs = MakeAbsolute(cand, host);
                                var (b2, mt2) = await FetchAsync(abs, refPage);
                                if (b2 != null) return (b2, mt2);
                            }
                        }
                        catch { /* ignore */ }
                        continue;
                    }

                    return (b, mt);
                }
            }
            catch { /* ignore */ }
            return (null, null);
        }

        private static async Task<string?> GetAltImageUrlAsync(string instagramUrl)
        {
            try
            {
                var u = new Uri(instagramUrl);
                var web = NewHtmlWeb();
                foreach (var host in AltHosts)
                {
                    try
                    {
                        await Task.Delay(Rng.Next(250, 700));
                        var dd = $"https://{host}{u.PathAndQuery}";
                        var doc = await web.LoadFromWebAsync(dd);

                        var (ldV, ldI) = ExtractFromJsonLd(doc);
                        var img = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", null)
                                 ?? doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:image']")?.GetAttributeValue("content", null)
                                 ?? ldI;

                        img = string.IsNullOrWhiteSpace(img) ? null : WebUtility.HtmlDecode(img);
                        if (!string.IsNullOrWhiteSpace(img)) return img;
                    }
                    catch { /* ignore host */ }
                }
            }
            catch { /* ignore */ }
            return null;
        }

        // ===== network helpers =====
        private static async Task<(byte[]? bytes, string? mediaType)> FetchAsync(string url, string? referer = null)
        {
            try
            {
                url = WebUtility.HtmlDecode(url);

                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd(UA);
                http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("uk-UA,uk;q=0.9,en;q=0.8");
                http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/*,video/*,*/*;q=0.8");
                if (!string.IsNullOrWhiteSpace(referer)) http.DefaultRequestHeaders.Referrer = new Uri(referer);

                using var r1 = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (r1.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest or (HttpStatusCode)429)
                {
                    if (!string.IsNullOrWhiteSpace(MediaProxyBase))
                    {
                        var proxied = $"{MediaProxyBase}?url={Uri.EscapeDataString(url)}";
                        Console.WriteLine($"[MEDIA] proxy try: {proxied}");
                        using var rp = await http.GetAsync(proxied, HttpCompletionOption.ResponseHeadersRead);
                        if (!rp.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"[MEDIA] proxy failed {rp.StatusCode} for {url}");
                            rp.EnsureSuccessStatusCode();
                        }
                        Console.WriteLine($"[MEDIA] proxy used OK for {url}");
                        var mtP = rp.Content.Headers.ContentType?.MediaType;
                        var bytesP = await rp.Content.ReadAsByteArrayAsync();
                        return (bytesP, mtP);
                    }
                    else
                    {
                        Console.WriteLine("[MEDIA] proxy disabled (MEDIA_PROXY_BASE not set)");
                    }
                }

                r1.EnsureSuccessStatusCode();
                return (await r1.Content.ReadAsByteArrayAsync(), r1.Content.Headers.ContentType?.MediaType);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MEDIA] fetch fail {url} : {ex.Message}");
                return (null, null);
            }
        }

        private static HtmlWeb NewHtmlWeb() => new HtmlWeb
        {
            PreRequest = req =>
            {
                req.UserAgent = UA;
                req.Headers["Accept-Language"] = "uk-UA,uk;q=0.9,en;q=0.8";
                return true;
            }
        };

        private static string MakeAbsolute(string url, string host)
        {
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return url;
            if (url.StartsWith("//")) return "https:" + url;
            if (url.StartsWith("/")) return $"https://{host}{url}";
            return $"https://{host}/{url.TrimStart('/')}";
        }

        private static bool IsVideo(string? mt) => !string.IsNullOrWhiteSpace(mt) && mt.StartsWith("video", StringComparison.OrdinalIgnoreCase);
        private static bool IsImage(string? mt) => !string.IsNullOrWhiteSpace(mt) && mt.StartsWith("image", StringComparison.OrdinalIgnoreCase);
        private static bool IsJpegOrPng(string? mt) =>
            string.Equals(mt, "image/jpeg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mt, "image/png", StringComparison.OrdinalIgnoreCase);
    }
}
