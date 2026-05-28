using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartSentinelEye.Automation.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AutomationDbContext>
{
    public AutomationDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<AutomationDbContext> builder = new();
        builder.UseNpgsql("Host=localhost;Database=design-time;Username=design;Password=design");
        return new AutomationDbContext(builder.Options);
    }
}
