using System.Net;
using System.Text;
using Souq.Application.Common.Notifications;

namespace Souq.Infrastructure.Notifications;

// ============================================================================
// قوالب البريد (المرحلة 14): مترجمة (العربية والإنجليزية بلغة المتجر الافتراضية) وبهوية المتجر — اسمه، شعاره، ولون ترويسته.
// كل قيمة تُرمَّز لـ HTML (اسم المتجر ونصوص الإدارة لا تحقن القالب)، والعنوان بلا محارف تحكّم (لا حقن ترويسات). نسخة نصّية مع
// كل رسالة لعملاء البريد بلا HTML. لغة غير مدعومة ⇒ العربية.
// ============================================================================
internal sealed class EmailComposer : IEmailComposer
{
    public ComposedEmail Compose(EmailContent content)
    {
        var culture = Texts.ContainsKey(content.Culture) ? content.Culture : "ar";
        var text = Texts[culture][content.Template];
        string Fill(string template) => content.Values.Aggregate(
            template.Replace("{store}", content.Branding.StoreName, StringComparison.Ordinal),
            (current, value) => current.Replace($"{{{value.Key}}}", value.Value, StringComparison.Ordinal));

        var title = Fill(text.Title);
        var body = Fill(text.Body);
        var footer = Fill(content.Branding.ContactEmail is null ? Footers[culture].Generic : Footers[culture].Reply);
        var details = Details(culture, content).ToList();
        var lines = content.Lines ?? [];
        var totals = Totals(culture, content).ToList();
        var subject = new string($"{Fill(text.Subject)} — {content.Branding.StoreName}".Where(c => !char.IsControl(c)).ToArray());

        return new ComposedEmail(
            subject,
            Html(culture, content.Branding, title, body, details, lines, totals,
                text.Action is null ? null : (Fill(text.Action), content.ActionUrl), Fill(text.Note), footer),
            Plain(culture, title, body, details, lines, totals, content.ActionUrl, Fill(text.Note), footer));
    }

    // إجماليات الطلب بالترتيب الذي تُقرأ به الفاتورة. الخصم والشحن يظهران حين أرسلهما المعالج فقط.
    private static IEnumerable<(string Label, string Value)> Totals(string culture, EmailContent content)
    {
        var labels = OrderLabels[culture];
        var currency = content.Values.GetValueOrDefault("currency", "");
        string Money(string key) => $"{content.Values[key]} {currency}".Trim();

        if (!content.Values.ContainsKey("total")) yield break;
        if (content.Values.ContainsKey("subtotal")) yield return (labels.Subtotal, Money("subtotal"));
        if (content.Values.ContainsKey("discount")) yield return (labels.Discount, $"-{Money("discount")}");
        if (content.Values.ContainsKey("shipping")) yield return (labels.Shipping, Money("shipping"));
        yield return (labels.Total, Money("total"));
    }

    // وصف المتغيّر بين قوسين بعد اسم المنتج ("قميص (M / أحمر)") — لقطة الطلب كما هي، ولا شيء لمنتج بلا خيارات (V3).
    private static string Variant(EmailLine line) =>
        string.IsNullOrWhiteSpace(line.Variant) ? "" : $" ({line.Variant})";

    private sealed record OrderText(string Items, string Subtotal, string Discount, string Shipping, string Total);

    private static readonly IReadOnlyDictionary<string, OrderText> OrderLabels = new Dictionary<string, OrderText>
    {
        ["ar"] = new("ما طلبته", "الإجمالي الفرعي", "الخصم", "الشحن", "الإجمالي"),
        ["en"] = new("What you ordered", "Subtotal", "Discount", "Shipping", "Total"),
    };

    // تفاصيل الشحن تحت نصّ رسالة الشحن حين تتوفّر.
    private static IEnumerable<(string Label, string Value)> Details(string culture, EmailContent content)
    {
        if (content.Template != EmailTemplate.OrderShipped) yield break;
        if (content.Values.GetValueOrDefault("trackingNumber") is { Length: > 0 } number)
            yield return (culture == "en" ? "Tracking number" : "رقم التتبّع", number);
        if (content.Values.GetValueOrDefault("carrier") is { Length: > 0 } carrier)
            yield return (culture == "en" ? "Carrier" : "شركة الشحن", carrier);
    }

    private static string Html(
        string culture, EmailBranding brand, string title, string body, IReadOnlyList<(string Label, string Value)> details,
        IReadOnlyList<EmailLine> lines, IReadOnlyList<(string Label, string Value)> totals,
        (string Label, string? Url)? action, string note, string footer)
    {
        static string E(string value) => WebUtility.HtmlEncode(value);
        var rtl = culture != "en";
        var align = rtl ? "right" : "left";
        var header = brand.LogoUrl is { } logo && Uri.TryCreate(logo, UriKind.Absolute, out var logoUri)
                                               && (logoUri.Scheme == Uri.UriSchemeHttps || logoUri.Scheme == Uri.UriSchemeHttp)
            ? $@"<img src=""{E(logo)}"" alt=""{E(brand.StoreName)}"" style=""max-height:48px;max-width:220px;"">"
            : $@"<span style=""font-size:22px;font-weight:700;color:{E(brand.OnPrimaryColor)};"">{E(brand.StoreName)}</span>";

        var html = new StringBuilder();
        html.Append($@"<!DOCTYPE html>
<html lang=""{culture}"" dir=""{(rtl ? "rtl" : "ltr")}"">
<body style=""margin:0;padding:0;background:#F4F1EC;font-family:Tajawal,Arial,sans-serif;"">
  <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#F4F1EC;padding:32px 0;"">
    <tr><td align=""center"">
      <table width=""480"" cellpadding=""0"" cellspacing=""0"" style=""background:#ffffff;border-radius:14px;overflow:hidden;"">
        <tr><td style=""background:{E(brand.PrimaryColor)};padding:24px;text-align:center;"">{header}</td></tr>
        <tr><td style=""padding:32px;text-align:{align};color:#1A2421;"">
          <h2 style=""margin:0 0 12px;color:#1A2421;"">{E(title)}</h2>
          <p style=""margin:0 0 20px;line-height:1.7;"">{E(body)}</p>");
        foreach (var (label, value) in details)
            html.Append($@"
          <p style=""margin:0 0 6px;""><b>{E(label)}:</b> <span dir=""ltr"">{E(value)}</span></p>");
        if (lines.Count > 0)
        {
            html.Append($@"
          <h3 style=""margin:24px 0 8px;font-size:15px;color:#1A2421;"">{E(OrderLabels[culture].Items)}</h3>
          <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border-collapse:collapse;font-size:14px;"">");
            foreach (var line in lines)
                html.Append($@"
            <tr><td style=""padding:6px 0;border-bottom:1px solid #EFEDE8;text-align:{align};"">{E(line.Name)}{E(Variant(line))} × {line.Quantity}</td>
                <td style=""padding:6px 0;border-bottom:1px solid #EFEDE8;text-align:{(rtl ? "left" : "right")};"" dir=""ltr"">{E(line.LineTotal)}</td></tr>");
            foreach (var (label, value) in totals)
                html.Append($@"
            <tr><td style=""padding:6px 0;text-align:{align};color:#6b736f;"">{E(label)}</td>
                <td style=""padding:6px 0;text-align:{(rtl ? "left" : "right")};"" dir=""ltr"">{E(value)}</td></tr>");
            html.Append(@"
          </table>");
        }
        if (action is { Url: { } url } button)
            html.Append($@"
          <div style=""text-align:center;margin:28px 0;"">
            <a href=""{E(url)}"" style=""background:{E(brand.PrimaryColor)};color:{E(brand.OnPrimaryColor)};padding:14px 32px;border-radius:8px;text-decoration:none;font-weight:700;display:inline-block;"">{E(button.Label)}</a>
          </div>");
        if (note.Length > 0)
            html.Append($@"
          <p style=""margin:0 0 12px;color:#6b736f;font-size:13px;line-height:1.7;"">{E(note)}</p>");
        html.Append($@"
          <p style=""margin:0;color:#6b736f;font-size:13px;line-height:1.7;"">{E(footer)}</p>
        </td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>");
        return html.ToString();
    }

    private static string Plain(
        string culture, string title, string body, IReadOnlyList<(string Label, string Value)> details,
        IReadOnlyList<EmailLine> lines, IReadOnlyList<(string Label, string Value)> totals,
        string? url, string note, string footer)
    {
        var text = new StringBuilder().AppendLine(title).AppendLine().AppendLine(body);
        foreach (var (label, value) in details) text.AppendLine($"{label}: {value}");
        if (lines.Count > 0)
        {
            text.AppendLine().AppendLine(OrderLabels[culture].Items);
            foreach (var line in lines) text.AppendLine($"- {line.Name}{Variant(line)} × {line.Quantity}: {line.LineTotal}");
            foreach (var (label, value) in totals) text.AppendLine($"{label}: {value}");
        }
        if (url is not null) text.AppendLine().AppendLine(url);
        if (note.Length > 0) text.AppendLine().AppendLine(note);
        return text.AppendLine().AppendLine(footer).ToString();
    }

    private sealed record TemplateText(string Subject, string Title, string Body, string? Action, string Note = "");

    private sealed record FooterText(string Generic, string Reply);

    private static readonly IReadOnlyDictionary<string, FooterText> Footers = new Dictionary<string, FooterText>
    {
        ["ar"] = new("رسالة من {store}.", "رسالة من {store} — للاستفسار ردّ على هذه الرسالة."),
        ["en"] = new("A message from {store}.", "A message from {store} — reply to this email with any questions."),
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<EmailTemplate, TemplateText>> Texts =
        new Dictionary<string, IReadOnlyDictionary<EmailTemplate, TemplateText>>
        {
            ["ar"] = new Dictionary<EmailTemplate, TemplateText>
            {
                [EmailTemplate.PasswordReset] = new("إعادة تعيين كلمة المرور", "إعادة تعيين كلمة المرور",
                    "وصلنا طلب لإعادة تعيين كلمة مرور حسابك. اضغط الزر أدناه لاختيار كلمة مرور جديدة. الرابط صالح لمدة ساعتين فقط.",
                    "إعادة تعيين كلمة المرور", "إن لم تطلب هذا، تجاهل هذه الرسالة — لن يتغيّر شيء في حسابك."),
                [EmailTemplate.EmailVerification] = new("تأكيد بريدك الإلكتروني", "تأكيد بريدك الإلكتروني",
                    "مرحباً بك! اضغط الزر أدناه لتأكيد أن هذا البريد لك. الرابط صالح لمدة 48 ساعة.",
                    "تأكيد البريد", "إن لم تنشئ حساباً، تجاهل هذه الرسالة."),
                [EmailTemplate.Invitation] = new("دعوة للانضمام إلى {inviter}", "دعوة للانضمام إلى {inviter}",
                    "دُعيت لإدارة «{inviter}». اضغط الزر أدناه لاختيار كلمة المرور وتفعيل حسابك. الرابط صالح لمدة 72 ساعة.",
                    "قبول الدعوة", "إن لم تتوقّع هذه الدعوة، تجاهل هذه الرسالة — لن يُفعَّل أي حساب."),
                [EmailTemplate.OrderConfirmed] = new("تأكيد الطلب #{orderNumber}", "شكراً لطلبك!",
                    "استلمنا دفع طلبك رقم #{orderNumber} بإجمالي {total} {currency}، ونجهّزه الآن.", "تتبّع الطلب"),
                [EmailTemplate.OrderShipped] = new("طلبك #{orderNumber} في الطريق", "طلبك في الطريق",
                    "شُحن طلبك رقم #{orderNumber}. تابع حالته من الرابط أدناه.", "تتبّع الطلب"),
                [EmailTemplate.OrderDelivered] = new("وصل طلبك #{orderNumber}", "وصل طلبك",
                    "سُلِّم طلبك رقم #{orderNumber}. نتمنّى أن ينال إعجابك — ويسعدنا تقييمك لما اشتريت.", "عرض الطلب"),
                [EmailTemplate.OrderCancelled] = new("أُلغي طلبك #{orderNumber}", "أُلغي طلبك",
                    "أُلغي طلبك رقم #{orderNumber}. إن كان مدفوعاً، يُعاد المبلغ إلى وسيلة الدفع نفسها.", "تفاصيل الطلب"),
            },
            ["en"] = new Dictionary<EmailTemplate, TemplateText>
            {
                [EmailTemplate.PasswordReset] = new("Reset your password", "Reset your password",
                    "We received a request to reset your account's password. Use the button below to choose a new one. The link is valid for two hours.",
                    "Reset password", "If you didn't ask for this, ignore this email — nothing will change in your account."),
                [EmailTemplate.EmailVerification] = new("Confirm your email", "Confirm your email",
                    "Welcome! Use the button below to confirm this email address is yours. The link is valid for 48 hours.",
                    "Confirm email", "If you didn't create an account, ignore this email."),
                [EmailTemplate.Invitation] = new("Invitation to join {inviter}", "Invitation to join {inviter}",
                    "You've been invited to manage “{inviter}”. Use the button below to choose a password and activate your account. The link is valid for 72 hours.",
                    "Accept invitation", "If you weren't expecting this invitation, ignore this email — no account will be activated."),
                [EmailTemplate.OrderConfirmed] = new("Order #{orderNumber} confirmed", "Thank you for your order!",
                    "We've received payment for order #{orderNumber} ({total} {currency}) and are preparing it now.", "Track order"),
                [EmailTemplate.OrderShipped] = new("Order #{orderNumber} is on its way", "Your order is on its way",
                    "Order #{orderNumber} has shipped. Follow it from the link below.", "Track order"),
                [EmailTemplate.OrderDelivered] = new("Order #{orderNumber} delivered", "Your order has arrived",
                    "Order #{orderNumber} has been delivered. We hope you enjoy it — we'd love your review.", "View order"),
                [EmailTemplate.OrderCancelled] = new("Order #{orderNumber} cancelled", "Your order was cancelled",
                    "Order #{orderNumber} was cancelled. If it was paid, the amount goes back to the same payment method.", "Order details"),
            },
        };
}
