using System.Net;
using System.Text;
using MimeKit;
using Sweetch.Core.Operations;
using MsgStorage = MsgReader.Outlook.Storage;
using static Sweetch.Core.Loc;

namespace Sweetch.Core.Backends;

/// <summary>
/// Letters (EML, Outlook MSG) and saved web pages (MHTML) as one HTML file, PDF (through LibreOffice), EML
/// or their attachments. Everything goes through a MimeKit message: MSG is converted to one first.
/// </summary>
public static class MailBackend
{
    public static MimeMessage Load(string path) => Formats.KeyOf(path) == "msg" ? FromMsg(path) : MimeMessage.Load(path);

    /// <summary>Outlook's own format → a MIME message with the same people, dates, bodies and attachments.</summary>
    public static MimeMessage FromMsg(string path)
    {
        using var msg = new MsgStorage.Message(path, FileAccess.Read);
        var mime = new MimeMessage { Subject = msg.Subject ?? "" };
        if (msg.Sender is { } sender && (sender.Email ?? sender.DisplayName) is not null)
            mime.From.Add(new MailboxAddress(sender.DisplayName ?? sender.Email, sender.Email ?? ""));
        foreach (var r in msg.Recipients)
        {
            var address = new MailboxAddress(r.DisplayName ?? r.Email, r.Email ?? "");
            (r.Type == MsgReader.Outlook.RecipientType.Cc ? mime.Cc : r.Type == MsgReader.Outlook.RecipientType.Bcc ? mime.Bcc : mime.To).Add(address);
        }
        if (msg.SentOn is { } sent)
            mime.Date = sent;
        var body = new BodyBuilder { TextBody = msg.BodyText, HtmlBody = msg.BodyHtml };
        foreach (var item in msg.Attachments)
        {
            if (item is MsgStorage.Attachment a && a.Data is { } data)
            {
                var part = a.IsInline && a.ContentId is not null ? body.LinkedResources.Add(a.FileName ?? a.ContentId, data) : body.Attachments.Add(a.FileName ?? "attachment", data);
                if (a.ContentId is not null)
                    part.ContentId = a.ContentId;
            }
            else if (item is MsgStorage.Message inner)
            {
                // A forwarded letter: kept as its own .msg, as Outlook shows it.
                using var stream = new MemoryStream();
                inner.Save(stream);
                body.Attachments.Add((inner.Subject is { Length: > 0 } s ? s : "message") + ".msg", stream.ToArray());
            }
        }
        mime.Body = body.ToMessageBody();
        return mime;
    }

    /// <summary>
    /// The letter as a page: a header with the people and the date, then the body. Pictures the body
    /// refers to (cid:…, or the page's own URLs in MHTML) come from <paramref name="source"/>.
    /// </summary>
    public static string ToHtml(MimeMessage message, Func<MimePart, string> source, bool page = false)
    {
        var resources = message.BodyParts.OfType<MimePart>().ToList();
        var html = message.HtmlBody;
        if (html is not null)
        {
            // Pictures and fonts first, then stylesheets, which may point at them in any order.
            var links = new Dictionary<string, string>();
            void Link(MimePart part, string url)
            {
                if (part.ContentId is { } id)
                    links["cid:" + id] = url;
                if (part.ContentLocation is { } location)
                    links[location.OriginalString] = url;
            }
            var styles = resources.Where(p => p.ContentType.MimeType == "text/css").OfType<TextPart>().ToList();
            foreach (var part in resources.Where(p => p.ContentType.MimeType != "text/html" && !styles.Contains(p)))
                Link(part, source(part));
            foreach (var css in styles)
                Link(css, "data:text/css;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(Replace(css.Text, links))));
            html = Replace(html, links);
        }
        else
        {
            html = $"<pre style=\"white-space:pre-wrap;font:inherit\">{WebUtility.HtmlEncode(message.TextBody ?? "")}</pre>";
        }
        if (page)
            return html;

        var sb = new StringBuilder("<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\"><title>")
            .Append(WebUtility.HtmlEncode(message.Subject ?? "")).Append("</title></head>\n<body style=\"font-family:Segoe UI,Arial,sans-serif\">\n")
            .Append("<table style=\"border-collapse:collapse;margin-bottom:16px;font-size:14px\">\n");
        void Row(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                sb.Append("<tr><td style=\"color:#666;padding:2px 12px 2px 0;vertical-align:top\">").Append(label).Append("</td><td>").Append(WebUtility.HtmlEncode(value)).Append("</td></tr>\n");
        }
        Row(T("Subject", "Тема"), message.Subject);
        Row(T("From", "От"), message.From.ToString());
        Row(T("To", "Кому"), message.To.ToString());
        Row(T("Cc", "Копия"), message.Cc.ToString());
        Row(T("Date", "Дата"), message.Date == default ? null : message.Date.ToLocalTime().ToString("f", System.Globalization.CultureInfo.CurrentCulture));
        Row(T("Attachments", "Вложения"), string.Join(", ", message.Attachments.Select(AttachmentName)));
        sb.Append("</table>\n<hr>\n").Append(html).Append("\n</body></html>\n");
        return sb.ToString();
    }

    private static string Replace(string text, Dictionary<string, string> links)
    {
        // Longest first, so "a.png?x=1" isn't half-replaced by "a.png".
        foreach (var (from, to) in links.OrderByDescending(kv => kv.Key.Length))
            text = text.Replace(from, to, StringComparison.Ordinal);
        return text;
    }

    public static string DataUri(MimePart part)
    {
        using var stream = new MemoryStream();
        part.Content?.DecodeTo(stream);
        return $"data:{part.ContentType.MimeType};base64,{Convert.ToBase64String(stream.ToArray())}";
    }

    private static string AttachmentName(MimeEntity entity) => entity switch
    {
        MimePart part => part.FileName ?? "attachment",
        MessagePart { Message: { } inner } => (inner.Subject is { Length: > 0 } s ? s : "message") + ".eml",
        _ => "attachment",
    };

    public static Task<string> ToHtmlAsync(FileItem item, OperationContext ctx) =>
        OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), item.BaseName, "html", tmp => Task.Run(() =>
            File.WriteAllText(tmp, ToHtml(Load(item.Path), DataUri, IsPage(item)), new UTF8Encoding(false)), ctx.CancellationToken));

    /// <summary>LibreOffice prints the page; pictures go next to it as files, which it reads more reliably than data: links.</summary>
    public static async Task<string> ToPdfAsync(FileItem item, OperationContext ctx)
    {
        var temp = OutputPaths.TempDirectory();
        try
        {
            var html = Path.Combine(temp, item.BaseName + ".html");
            var n = 0;
            await Task.Run(() => File.WriteAllText(html, ToHtml(Load(item.Path), part =>
            {
                var ext = Path.GetExtension(part.FileName ?? "");
                var name = $"r{n++}{(ext.Length is > 0 and < 6 ? ext : "")}";
                using (var file = File.Create(Path.Combine(temp, name)))
                    part.Content?.DecodeTo(file);
                return name;
            }, IsPage(item)), new UTF8Encoding(false)), ctx.CancellationToken).ConfigureAwait(false);
            return await OfficeBackend.ConvertAsync(new FileItem(html), "pdf", new OperationContextOverride(ctx, ctx.OutputDirFor(item.Path)).Context, "HTML (StarWriter)").ConfigureAwait(false);
        }
        finally
        {
            OutputPaths.TryDelete(temp);
        }
    }

    private static bool IsPage(FileItem item) => item.Format == "mht";

    public static Task<string> ToEmlAsync(FileItem item, OperationContext ctx) =>
        OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), item.BaseName, "eml", tmp => Task.Run(() => FromMsg(item.Path).WriteTo(tmp), ctx.CancellationToken));

    /// <summary>Saves every attachment into "name (attachments)"; a letter without any says so.</summary>
    public static async Task<string> SaveAttachmentsAsync(FileItem item, OperationContext ctx)
    {
        var message = await Task.Run(() => Load(item.Path), ctx.CancellationToken).ConfigureAwait(false);
        var attachments = message.Attachments.ToList();
        if (attachments.Count == 0)
            throw new ConversionException(T("The letter has no attachments.", "В письме нет вложений."));
        var folder = OutputPaths.UniqueDirectory(ctx.OutputDirFor(item.Path), $"{item.BaseName} ({T("attachments", "вложения")})");
        Directory.CreateDirectory(folder);
        foreach (var entity in attachments)
        {
            var name = string.Concat(AttachmentName(entity).Split(Path.GetInvalidFileNameChars()));
            var target = OutputPaths.Unique(folder, Path.GetFileNameWithoutExtension(name), Path.GetExtension(name).TrimStart('.'));
            await using var file = File.Create(target);
            if (entity is MimePart part)
                await part.Content!.DecodeToAsync(file, ctx.CancellationToken).ConfigureAwait(false);
            else if (entity is MessagePart { Message: { } inner })
                await inner.WriteToAsync(file, ctx.CancellationToken).ConfigureAwait(false);
        }
        return folder;
    }
}
