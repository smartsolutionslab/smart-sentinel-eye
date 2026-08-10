using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SmartSentinelEye.MigrationRunner;

/// <summary>
/// Attaches <see cref="PostgresNoticeLoggingInterceptor"/> to one context's
/// options.
///
/// <para>
/// Per context type rather than once globally, because each
/// <c>Add&lt;Context&gt;Persistence</c> builds its own
/// <c>DbContextOptionsBuilder</c> and EF offers no untyped hook into all of
/// them. <see cref="IDbContextOptionsConfiguration{TContext}"/> runs after
/// that delegate, so this adds to those options rather than replacing them —
/// the <c>AggregateVersionInterceptor</c> each module registers is untouched.
/// </para>
/// </summary>
internal static class PostgresNoticeLoggingRegistration
{
    public static IHostApplicationBuilder AddPostgresNoticeLogging<TContext>(this IHostApplicationBuilder builder)
        where TContext : DbContext
    {
        builder.Services.AddSingleton<IDbContextOptionsConfiguration<TContext>>(
            new NoticeLoggingOptionsConfiguration<TContext>());

        return builder;
    }

    private sealed class NoticeLoggingOptionsConfiguration<TContext> : IDbContextOptionsConfiguration<TContext>
        where TContext : DbContext
    {
        public void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.AddInterceptors(
                serviceProvider.GetRequiredService<PostgresNoticeLoggingInterceptor>());
        }
    }
}
