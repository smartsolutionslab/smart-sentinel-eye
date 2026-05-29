using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartSentinelEye.Identity.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<IdentityDbContext> builder = new();
        builder.UseNpgsql("Host=localhost;Database=design-time;Username=design;Password=design");
        return new IdentityDbContext(builder.Options);
    }
}
