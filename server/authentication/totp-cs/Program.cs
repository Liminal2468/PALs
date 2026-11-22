using TotpPrototype;

var config = TotpConfig.Load();
var store = new PendingLoginStore(config);
var userId = args.Length > 0 ? args[0] : Console.ReadLine();
if (string.IsNullOrWhiteSpace(userId))
{
    throw new ArgumentException("A student email or user ID is required.");
}

var attemptId = store.StartLogin(userId);
Console.WriteLine($"Attempt ID: {attemptId}");
Console.Write("Enter the emailed code: ");
var submittedCode = Console.ReadLine() ?? string.Empty;
Console.WriteLine(store.VerifyLogin(attemptId, submittedCode)
    ? "Login verified."
    : "Login failed.");
