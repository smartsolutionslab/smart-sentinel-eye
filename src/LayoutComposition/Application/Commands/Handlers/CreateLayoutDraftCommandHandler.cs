using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;

public sealed class CreateLayoutDraftCommandHandler(
    ILayoutRepository layouts,
    IClock clock,
    ILogger<CreateLayoutDraftCommandHandler> logger)
    : ICommandHandler<CreateLayoutDraftCommand, Result<LayoutIdentifier, CreateLayoutDraftError>>
{
    public async Task<Result<LayoutIdentifier, CreateLayoutDraftError>> HandleAsync(
        CreateLayoutDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (name, camera, createdBy, overlay) = command;

        Option<Layout> existing = await layouts
            .GetByNameAsync(name, cancellationToken)
            .ConfigureAwait(false);
        if (existing.HasValue)
        {
            return Result<LayoutIdentifier, CreateLayoutDraftError>.Failure(
                new CreateLayoutDraftError.LayoutNameTaken(name.Value));
        }

        Layout layout = Layout.CreateDraft(name, camera, createdBy, clock, overlay);
        layouts.Add(layout);
        await layouts.SaveAsync(cancellationToken).ConfigureAwait(false);

        Log.CreatedLayout(logger, layout.Id, name, createdBy);

        return Result<LayoutIdentifier, CreateLayoutDraftError>.Success(layout.Id);
    }
}
