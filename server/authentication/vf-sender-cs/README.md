# vf-sender-cs

Small Amazon SES v2 test sender using `AWSSDK.SimpleEmailV2`.

Set the sender, recipient, and optional region before running:

```sh
export SENDER_EMAIL="noreply@noteriety.top"
export RECIPIENT_EMAIL="your-email@gmail.com"
export AWS_REGION="ap-southeast-2"
dotnet run
```

Each run generates a random six-digit verification code and substitutes it into
`email.htm`. The PALs logo is loaded from its HTTPS image URL in the template.

The project-local `.aws/credentials` file is used when
`AWS_SHARED_CREDENTIALS_FILE` is not already set. Replace its placeholder
values locally and do not commit that file.
