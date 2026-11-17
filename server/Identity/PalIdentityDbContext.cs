using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Palsys.Server.Identity;

public sealed class PalIdentityDbContext : IdentityDbContext<PalIdentityUser, PalIdentityRole, long>
{
    public PalIdentityDbContext(DbContextOptions<PalIdentityDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<PalIdentityUser>(entity =>
        {
            entity.ToTable("identity_users");
            entity.Property(user => user.PalUserId).HasColumnName("pal_user_id");
            entity.HasIndex(user => user.PalUserId).IsUnique().HasDatabaseName("ux_identity_users_pal_user_id");
        });
        builder.Entity<PalIdentityRole>().ToTable("identity_roles");
        builder.Entity<IdentityUserClaim<long>>().ToTable("identity_user_claims");
        builder.Entity<IdentityUserRole<long>>().ToTable("identity_user_roles");
        builder.Entity<IdentityUserLogin<long>>().ToTable("identity_user_logins");
        builder.Entity<IdentityRoleClaim<long>>().ToTable("identity_role_claims");
        builder.Entity<IdentityUserToken<long>>().ToTable("identity_user_tokens");
    }
}
