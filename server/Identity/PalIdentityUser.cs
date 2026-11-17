using Microsoft.AspNetCore.Identity;

namespace Palsys.Server.Identity;

public sealed class PalIdentityUser : IdentityUser<long>
{
    public long? PalUserId { get; set; }
}

public sealed class PalIdentityRole : IdentityRole<long>
{
}
