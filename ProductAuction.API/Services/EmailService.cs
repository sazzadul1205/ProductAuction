using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ProductAuction.API.Services
{
    public class EmailService : IEmailService
    {
        private readonly SmtpSettings _settings;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IOptions<SmtpSettings> settings, ILogger<EmailService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            if (string.IsNullOrWhiteSpace(_settings.Host))
            {
                throw new InvalidOperationException("SMTP host is not configured.");
            }

            var message = new MimeMessage();
            var fromEmail = string.IsNullOrWhiteSpace(_settings.FromEmail) ? _settings.User : _settings.FromEmail;
            message.From.Add(new MailboxAddress(_settings.FromName, fromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

            using var client = new SmtpClient();
            var secure = _settings.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
            if (_settings.EnableSsl && _settings.Port == 465)
            {
                secure = SecureSocketOptions.SslOnConnect;
            }

            await client.ConnectAsync(_settings.Host, _settings.Port, secure);
            if (!string.IsNullOrWhiteSpace(_settings.User))
            {
                await client.AuthenticateAsync(_settings.User, _settings.Password);
            }
            await ((IMailTransport)client).SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent to {Email} with subject {Subject}", toEmail, subject);
        }
    }
}
