using Amazon;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using System.Security.Cryptography;

const string defaultRegion = "ap-southeast-2";
const string subject = "Test v3.4";
const string verificationCodePlaceholder = "{{ verification_code }}";

var projectDirectory = Directory.GetCurrentDirectory();
LoadDotEnv(Path.Combine(projectDirectory, ".env"));
var htmlTemplate = File.ReadAllText(Path.Combine(projectDirectory, "email.htm"));

var projectCredentialsPath = Path.Combine(projectDirectory, ".aws", "credentials");
if (File.Exists(projectCredentialsPath) &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AWS_SHARED_CREDENTIALS_FILE")))
{
    Environment.SetEnvironmentVariable("AWS_SHARED_CREDENTIALS_FILE", projectCredentialsPath);
    LoadProjectCredentials(projectCredentialsPath);
}

var sender = RequiredEnvironment("SENDER_EMAIL");
var recipient = RequiredEnvironment("RECIPIENT_EMAIL");
var verificationCode = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");
if (htmlTemplate.Split(verificationCodePlaceholder, StringSplitOptions.None).Length != 2)
{
    throw new InvalidOperationException(
        $"email.htm must contain exactly one {verificationCodePlaceholder} placeholder");
}

var htmlBody = htmlTemplate.Replace(verificationCodePlaceholder, verificationCode, StringComparison.Ordinal);
var textBody = $"""
    Your PALs verification code is {verificationCode}.

    This code is for one-time use. Never share it with anyone, including PALs staff.
    If you did not request this code, ignore this email and contact PALs through your usual official support channel.
    """;
var regionName = Environment.GetEnvironmentVariable("AWS_REGION")
    ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
    ?? defaultRegion;

var ses = new AmazonSimpleEmailServiceV2Client(
    new AmazonSimpleEmailServiceV2Config
    {
        RegionEndpoint = RegionEndpoint.GetBySystemName(regionName)
    });

try
{
    var response = await ses.SendEmailAsync(new SendEmailRequest
    {
        FromEmailAddress = sender,
        Destination = new Destination
        {
            ToAddresses = [recipient]
        },
        Content = new EmailContent
        {
            Simple = new Message
            {
                Subject = new Content
                {
                    Data = subject,
                    Charset = "UTF-8"
                },
                Body = new Body
                {
                    Text = new Content
                    {
                        Data = textBody,
                        Charset = "UTF-8"
                    },
                    Html = new Content
                    {
                        Data = htmlBody,
                        Charset = "UTF-8"
                    }
                }
            }
        }
    });

    Console.WriteLine("Email successfully submitted!");
    Console.WriteLine($"Message ID: {response.MessageId}");
}
catch (AmazonSimpleEmailServiceV2Exception error)
{
    Console.Error.WriteLine("Email delivery request failed:");
    Console.Error.WriteLine($"{error.ErrorCode}: {error.Message}");
    Environment.ExitCode = 1;
}
finally
{
    ses.Dispose();
}

static string RequiredEnvironment(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Missing required environment variable: {name}");
    }

    return value;
}

static void LoadDotEnv(string path)
{
    if (!File.Exists(path))
    {
        return;
    }

    foreach (var rawLine in File.ReadLines(path))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            continue;
        }

        var separator = line.IndexOf('=');
        if (separator <= 0)
        {
            continue;
        }

        var key = line[..separator].Trim();
        var value = line[(separator + 1)..].Trim();
        if (value.Length >= 2 && value[0] == value[^1] && value[0] is '\'' or '"')
        {
            value = value[1..^1];
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}

static void LoadProjectCredentials(string path)
{
    var inDefaultProfile = false;
    foreach (var rawLine in File.ReadLines(path))
    {
        var line = rawLine.Trim();
        if (line.StartsWith('[') && line.EndsWith(']'))
        {
            inDefaultProfile = line.Equals("[default]", StringComparison.OrdinalIgnoreCase);
            continue;
        }

        if (!inDefaultProfile || line.Length == 0 || line.StartsWith('#'))
        {
            continue;
        }

        var separator = line.IndexOf('=');
        if (separator <= 0)
        {
            continue;
        }

        var key = line[..separator].Trim();
        var value = line[(separator + 1)..].Trim().Trim('"', '\'');
        var environmentKey = key switch
        {
            "aws_access_key_id" => "AWS_ACCESS_KEY_ID",
            "aws_secret_access_key" => "AWS_SECRET_ACCESS_KEY",
            "aws_session_token" => "AWS_SESSION_TOKEN",
            _ => null
        };

        if (environmentKey is not null &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentKey)))
        {
            Environment.SetEnvironmentVariable(environmentKey, value);
        }
    }
}
