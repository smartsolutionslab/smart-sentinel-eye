using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartSentinelEye.ServiceDefaults;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// Spec 021. Where a publish goes depends on where the caller is, and getting it
/// wrong loses messages with no error and no row — which is why this is tested
/// rather than reasoned about.
///
/// <para>
/// The first version of <see cref="OutboxEventBus{TDbContext}"/> sent everything
/// to the <c>IDbContextOutbox</c>. That is right for a repository write and
/// wrong for a Wolverine message handler, whose ambient context is enrolled and
/// flushed by Wolverine and whose <c>DbContext</c> nobody saves. It would have
/// taken out rule fan-out — a PLC event fires a rule and neither the variable
/// set nor the overlay highlight is ever published — and the entire integration
/// suite passed, because nothing exercises that path end to end.
/// </para>
/// </summary>
public class OutboxEventBusTests
{
    /// <summary>
    /// Inside a handler: the ambient context is already enrolled, so the publish
    /// belongs there and the DbContext outbox must not see it.
    /// </summary>
    [Fact]
    public async Task Inside_a_message_handler_it_publishes_through_the_ambient_context()
    {
        Mock<IMessageContext> ambient = new();
        ambient.SetupGet(context => context.Envelope).Returns(new Envelope());
        Mock<IDbContextOutbox<FakeDbContext>> outbox = new();

        OutboxEventBus<FakeDbContext> bus = new(
            outbox.Object, ambient.Object, NullLogger<OutboxEventBus<FakeDbContext>>.Instance);

        await bus.PublishAsync("an-announcement", CancellationToken.None);

        ambient.Verify(context => context.PublishAsync("an-announcement", null), Times.Once);
        outbox.Verify(
            box => box.PublishAsync(It.IsAny<string>(), It.IsAny<DeliveryOptions>()),
            Times.Never,
            "a handler's publish captured into the DbContext outbox goes where nothing "
            + "flushes it, and is lost when the scope disposes");
    }

    /// <summary>
    /// Outside a handler — a repository write — nothing enrols anything, so the
    /// publish must go to the outbox that the commit will flush. This is the gap
    /// issue #1605 was about.
    /// </summary>
    [Fact]
    public async Task Outside_a_message_handler_it_captures_into_the_outbox()
    {
        Mock<IMessageContext> ambient = new();
        ambient.SetupGet(context => context.Envelope).Returns((Envelope)null!);
        Mock<IDbContextOutbox<FakeDbContext>> outbox = new();

        OutboxEventBus<FakeDbContext> bus = new(
            outbox.Object, ambient.Object, NullLogger<OutboxEventBus<FakeDbContext>>.Instance);

        await bus.PublishAsync("an-announcement", CancellationToken.None);

        outbox.Verify(box => box.PublishAsync("an-announcement", null), Times.Once);
        ambient.Verify(
            context => context.PublishAsync(It.IsAny<string>(), It.IsAny<DeliveryOptions>()),
            Times.Never,
            "publishing straight to the broker is what left an announcement outside "
            + "its write's transaction in the first place");
    }

    /// <summary>
    /// Public because Moq proxies IDbContextOutbox<T> and Castle cannot build a
    /// proxy over a type it cannot see.
    /// </summary>
    public sealed class FakeDbContext : DbContext;
}
