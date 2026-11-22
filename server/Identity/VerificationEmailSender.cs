using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Amazon;
using System.Text.Encodings.Web;

namespace Palsys.Server.Identity;

public sealed class VerificationEmailSender(IConfiguration configuration)
{
    public async Task SendAsync(string recipient, string code, CancellationToken cancellationToken)
    {
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
            throw new ArgumentException("Verification code must contain exactly six digits.", nameof(code));

        var sender = configuration["SENDER_EMAIL"]?.Trim();
        if (string.IsNullOrWhiteSpace(sender))
            throw new InvalidOperationException("SENDER_EMAIL must be configured for SES delivery.");

        var regionName = configuration["AWS_REGION"];
        if (string.IsNullOrWhiteSpace(regionName))
            regionName = "ap-southeast-2";
        using var ses = new AmazonSimpleEmailServiceV2Client(
            new AmazonSimpleEmailServiceV2Config
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(regionName),
            });

        const string verificationCodePlaceholder = "{{ verification_code }}";
        var templatePath = Path.Combine(AppContext.BaseDirectory, "Identity", "email", "email.html");
        var htmlTemplate = await File.ReadAllTextAsync(templatePath, cancellationToken);
        if (!htmlTemplate.Contains(verificationCodePlaceholder, StringComparison.Ordinal))
            throw new InvalidOperationException("The verification email template is missing its code placeholder.");

        var safeCode = HtmlEncoder.Default.Encode(code);
        var htmlBody = htmlTemplate.Replace(verificationCodePlaceholder, safeCode, StringComparison.Ordinal);
        var response = await ses.SendEmailAsync(new SendEmailRequest
        {
            FromEmailAddress = sender,
            Destination = new Destination
            {
                ToAddresses = [recipient],
            },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content
                    {
                        Data = "Your PALs verification code",
                        Charset = "UTF-8",
                    },
                    Body = new Body
                    {
                        Text = new Content
                        {
                            Data = $"Your PALs verification code is {code}. It expires shortly. Never share this code with anyone.",
                            Charset = "UTF-8",
                        },
                        Html = new Content
                        {
                            Data = htmlBody,
                            Charset = "UTF-8",
                        },
                    },
                },
            },
        }, cancellationToken);

        if (string.IsNullOrWhiteSpace(response.MessageId))
            throw new InvalidOperationException("SES did not confirm the verification email submission.");
    }
}
