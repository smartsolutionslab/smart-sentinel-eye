using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;

public sealed class CreateLayoutDraftCommandHandler(
    ILayoutRepository layouts,
    IClock clock,
    ILogger<CreateLayoutDraftCommandHandler> log)
    : ICommandHandler<CreateLayoutDraftCommand, Result<LayoutIdentifier, CreateLayoutDraftError>>
{
    public async Task<Result<LayoutIdentifier, CreateLayoutDraftError>> HandleAsync(
        CreateLayoutDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Option<Layout> existing = await layouts
            .GetByNameAsync(command.Name, cancellationToken)
            .ConfigureAwait(false);
        if (existing.HasValue)
        {
            return Result<LayoutIdentifier, CreateLayoutDraftError>.Failure(
                new CreateLayoutDraftError.LayoutNameTaken(command.Name.Value));
        }

        Layout layout = Layout.CreateDraft(command.Name, command.Camera, command.CreatedBy, clock, command.Overlay);
        layouts.Add(layout);
        await layouts.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Created layout {Layout} '{Name}' (Draft) by {Operator}.",
            layout.Id, command.Name, command.CreatedBy);

        return Result<LayoutIdentifier, CreateLayoutDraftError>.Success(layout.Id);
    }
}
