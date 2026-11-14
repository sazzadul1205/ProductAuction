namespace ProductAuction.API.Services
{
  public static class EmailTemplates
  {
    public static string BuildPasswordResetEmail(string resetLink, string appName = "ProductAuction")
    {
      var safeLink = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(resetLink);
      var safeApp = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(appName);
      return Wrap($@"
                <h1 style=""margin:0 0 16px;font-size:22px;line-height:1.3;color:#111827;"">Reset your password</h1>
                <p style=""margin:0 0 16px;font-size:15px;line-height:1.6;color:#374151;"">
                    We received a request to reset the password for your <strong>{safeApp}</strong> account.
                </p>
                <p style=""margin:0 0 24px;font-size:15px;line-height:1.6;color:#374151;"">
                    Click the button below to choose a new password. For your security, this link can be used only once and may expire.
                </p>
                <p style=""margin:0 0 24px;"">
                    <a href=""{safeLink}"" style=""display:inline-block;background:#2563eb;color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:8px;font-weight:600;font-size:15px;"">Reset Password</a>
                </p>
                <p style=""margin:0 0 8px;font-size:13px;line-height:1.6;color:#6b7280;"">
                    If the button does not work, copy and paste this link into your browser:
                </p>
                <p style=""margin:0 0 20px;font-size:13px;line-height:1.6;word-break:break-all;color:#2563eb;"">{safeLink}</p>
                <hr style=""border:none;border-top:1px solid #e5e7eb;margin:24px 0;"" />
                <p style=""margin:0;font-size:13px;line-height:1.6;color:#6b7280;"">
                    If you did not request a password reset, you can safely ignore this email. Your password will not be changed.
                </p>", safeApp);
    }

    public static string BuildAccountConfirmationEmail(string confirmLink, string appName = "ProductAuction")
    {
      var safeLink = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(confirmLink);
      var safeApp = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(appName);
      return Wrap($@"
                <h1 style=""margin:0 0 16px;font-size:22px;line-height:1.3;color:#111827;"">Confirm your email address</h1>
                <p style=""margin:0 0 16px;font-size:15px;line-height:1.6;color:#374151;"">
                    Thank you for creating your <strong>{safeApp}</strong> account.
                </p>
                <p style=""margin:0 0 24px;font-size:15px;line-height:1.6;color:#374151;"">
                    To activate your account, please confirm your email address by clicking the button below. You must complete this verification before you can sign in.
                </p>
                <p style=""margin:0 0 24px;"">
                    <a href=""{safeLink}"" style=""display:inline-block;background:#16a34a;color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:8px;font-weight:600;font-size:15px;"">Confirm Email</a>
                </p>
                <p style=""margin:0 0 8px;font-size:13px;line-height:1.6;color:#6b7280;"">
                    If the button does not work, copy and paste this link into your browser:
                </p>
                <p style=""margin:0 0 20px;font-size:13px;line-height:1.6;word-break:break-all;color:#2563eb;"">{safeLink}</p>
                <hr style=""border:none;border-top:1px solid #e5e7eb;margin:24px 0;"" />
                <p style=""margin:0;font-size:13px;line-height:1.6;color:#6b7280;"">
                    If you did not create this account, you can ignore this email.
                </p>", safeApp);
    }

    private static string Wrap(string content, string appName)
    {
      return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
  <title>{appName}</title>
</head>
<body style=""margin:0;padding:0;background-color:#f3f4f6;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;"">
  <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" style=""background-color:#f3f4f6;padding:24px 12px;"">
    <tr>
      <td align=""center"">
        <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" style=""max-width:600px;background:#ffffff;border:1px solid #e5e7eb;border-radius:12px;overflow:hidden;"">
          <tr>
            <td style=""padding:20px 28px;background:#111827;color:#ffffff;font-size:18px;font-weight:700;"">{appName}</td>
          </tr>
          <tr>
            <td style=""padding:28px;"">{content}</td>
          </tr>
          <tr>
            <td style=""padding:16px 28px;background:#f9fafb;color:#6b7280;font-size:12px;line-height:1.6;"">
              &copy; {DateTime.UtcNow.Year} {appName}. This is an automated message, please do not reply.
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>";
    }
  }
}
