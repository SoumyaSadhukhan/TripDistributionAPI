using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TripDistribution.Model.Settings;

namespace TripDistribution.Api.Services;

public interface IEmailService
{
    Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string purpose, string? recipientName = null);
}

public class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _logger = logger;
        _settings = new EmailSettings();
        configuration.GetSection("EmailSettings").Bind(_settings);

        // Support environment variable overrides
        var envPassword = Environment.GetEnvironmentVariable("EmailSettings__SmtpPassword") 
                       ?? Environment.GetEnvironmentVariable("EmailSettings__Password");
        if (!string.IsNullOrWhiteSpace(envPassword))
        {
            _settings.Password = envPassword;
        }

        var envUsername = Environment.GetEnvironmentVariable("EmailSettings__Username");
        if (!string.IsNullOrWhiteSpace(envUsername))
        {
            _settings.Username = envUsername;
            if (string.IsNullOrWhiteSpace(_settings.FromEmail) || _settings.FromEmail.Contains("example.com"))
            {
                _settings.FromEmail = envUsername;
            }
        }
    }

    public async Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string purpose, string? recipientName = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(toEmail) || !toEmail.Contains('@'))
            {
                _logger.LogWarning("Invalid destination email address: {ToEmail}", toEmail);
                return false;
            }

            if (string.IsNullOrWhiteSpace(_settings.Username) || 
                string.IsNullOrWhiteSpace(_settings.Password) || 
                _settings.Username.Contains("example.com"))
            {
                _logger.LogWarning("SMTP credentials not configured (Username/Password empty or placeholder). Mocking email delivery to {ToEmail}. OTP Code is: {OtpCode}", toEmail, otpCode);
                return false;
            }

            var displayName = !string.IsNullOrWhiteSpace(recipientName) ? recipientName : "Valued Member";
            var actionTitle = purpose.ToUpper() switch
            {
                "LOGIN" => "Login Verification",
                "FORGOT_PASSWORD" => "Password Reset",
                "REGISTER" => "Account Registration",
                _ => "Verification"
            };

            var subject = $"[{_settings.FromName}] Your {actionTitle} OTP: {otpCode}";
            var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f3f4f6; margin: 0; padding: 20px; }}
        .card {{ max-width: 500px; margin: 0 auto; background: #ffffff; border-radius: 12px; padding: 32px; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1); border: 1px solid #e5e7eb; }}
        .brand {{ color: #4f46e5; font-size: 24px; font-weight: 800; text-align: center; margin-bottom: 8px; }}
        .title {{ color: #111827; font-size: 18px; font-weight: 600; text-align: center; margin-bottom: 24px; }}
        .otp-box {{ background: #eef2ff; border: 2px dashed #6366f1; border-radius: 8px; padding: 18px; text-align: center; margin: 24px 0; }}
        .otp-code {{ font-size: 34px; font-weight: 800; letter-spacing: 8px; color: #4338ca; }}
        .text {{ color: #4b5563; font-size: 14px; line-height: 1.6; margin-bottom: 12px; }}
        .footer {{ font-size: 12px; color: #9ca3af; text-align: center; margin-top: 32px; border-top: 1px solid #f3f4f6; padding-top: 16px; }}
    </style>
</head>
<body>
    <div class='card'>
        <div class='brand'>{_settings.FromName}</div>
        <div class='title'>Trip Distribution & Bill Split</div>
        <p class='text'>Hello <strong>{displayName}</strong>,</p>
        <p class='text'>You have requested an OTP for <strong>{actionTitle}</strong>. Use the code below to complete your verification:</p>
        <div class='otp-box'>
            <div class='otp-code'>{otpCode}</div>
        </div>
        <p class='text'>This one-time passcode is valid for <strong>10 minutes</strong>. For your security, please do not share this code with anyone.</p>
        <p class='text'>If you did not make this request, please disregard this email or contact support.</p>
        <div class='footer'>
            &copy; {DateTime.UtcNow.Year} {_settings.FromName} Trip Distribution. All rights reserved.
        </div>
    </div>
</body>
</html>";

            using var smtp = new SmtpClient(_settings.Host, _settings.Port)
            {
                EnableSsl = true,
                UseDefaultCredentials = false,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Credentials = new NetworkCredential(_settings.Username, _settings.Password),
                Timeout = 15000
            };

            var mail = new MailMessage
            {
                From = new MailAddress(_settings.FromEmail, _settings.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            mail.To.Add(toEmail);

            await smtp.SendMailAsync(mail);
            _logger.LogInformation("Successfully sent OTP email to {ToEmail} for purpose: {Purpose}", toEmail, purpose);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send OTP email to {ToEmail}", toEmail);
            return false;
        }
    }
}

