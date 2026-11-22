using Microsoft.AspNetCore.Identity;

namespace Palsys.Server.Identity;

public sealed class PasswordSymbolValidator : IPasswordValidator<PalIdentityUser>
{
    public Task<IdentityResult> ValidateAsync(
        UserManager<PalIdentityUser> manager,
        PalIdentityUser user,
        string? password)
    {
        if (password is not null
            && password.Any(character => char.IsPunctuation(character) || char.IsSymbol(character)))
        {
            return Task.FromResult(IdentityResult.Success);
        }

        return Task.FromResult(IdentityResult.Failed(new IdentityError
        {
            Code = "PasswordRequiresSymbol",
            Description = "Passwords must contain at least one punctuation mark or symbol.",
        }));
    }
}
