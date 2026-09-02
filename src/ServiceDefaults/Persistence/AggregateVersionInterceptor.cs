using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.ServiceDefaults.Persistence;

/// <summary>
/// Increments <see cref="IVersionedAggregate.Version"/> on every aggregate
/// root being written, so the concurrency token mapped by each context's
/// EF configuration actually changes between writes (ADR-0113).
///
/// <para>
/// Without this the token is inert: EF emits
/// <c>WHERE version = @original</c> but the application writes the same
/// value back, so two concurrent writers both match and the last write
/// silently wins.
/// </para>
///
/// <para>
/// The bump sets only <c>CurrentValue</c>. EF puts <c>OriginalValue</c> in
/// the <c>WHERE</c> clause and <c>CurrentValue</c> in the <c>SET</c>, so
/// leaving the original alone is what makes the predicate target the row
/// as it was loaded. Setting the property on an otherwise-<c>Unchanged</c>
/// root also promotes it to <c>Modified</c> — which is how a root whose
/// own columns did not change still gets an <c>UPDATE</c>, and therefore a
/// concurrency check, when only an owned child row was touched.
/// </para>
/// </summary>
public sealed class AggregateVersionInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Ensure.That(eventData).IsNotNull();

        BumpVersions(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Ensure.That(eventData).IsNotNull();

        BumpVersions(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Applies the version bump to every dirty aggregate root tracked by
    /// <paramref name="context"/>. Exposed directly so the behaviour can be
    /// asserted without a database round-trip.
    /// </summary>
    public static void BumpVersions(DbContext context)
    {
        if (context is null)
        {
            return;
        }

        EntityEntry[] roots = context.ChangeTracker.Entries()
            .Where(entry => entry.Entity is IVersionedAggregate)
            .ToArray();

        foreach (EntityEntry root in roots.Where(root => RequiresBump(context, root)))
        {
            Bump(root);
        }
    }

    private static bool RequiresBump(DbContext context, EntityEntry root)
    {
        // Added roots start at version 0 and have no prior row to guard.
        // Deleted roots are removed outright; EF still applies the token to
        // the DELETE using the original value.
        if (root.State is EntityState.Added or EntityState.Deleted or EntityState.Detached)
        {
            return false;
        }

        return root.State == EntityState.Modified || HasDirtyOwnedDescendant(context, root);
    }

    private static void Bump(EntityEntry root)
    {
        PropertyEntry version = root.Property(nameof(IVersionedAggregate.Version));

        // OriginalValue carries the *model* value, so this casts to
        // AggregateVersion rather than to int. Casting to int compiled fine while
        // Version was an int and would have thrown InvalidCastException on every
        // save the moment it became a value object -- a runtime failure the
        // compiler cannot see.
        AggregateVersion original = (AggregateVersion)version.OriginalValue;
        version.CurrentValue = AggregateVersion.From(original.Value + 1);
    }

    /// <summary>
    /// True when any owned entity beneath <paramref name="owner"/> is added,
    /// modified or deleted.
    ///
    /// <para>
    /// Driven from the model rather than from the owner's navigation
    /// collections: a removed child is no longer in its parent collection,
    /// but it is still tracked as <c>Deleted</c>, so a collection walk would
    /// miss exactly the deletion case.
    /// </para>
    /// </summary>
    private static bool HasDirtyOwnedDescendant(DbContext context, EntityEntry owner)
    {
        foreach (INavigation navigation in OwnedNavigations(owner))
        {
            foreach (EntityEntry child in ChildrenOf(context, owner, navigation))
            {
                if (child.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                {
                    return true;
                }

                if (HasDirtyOwnedDescendant(context, child))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<INavigation> OwnedNavigations(EntityEntry owner) =>
        owner.Metadata.GetNavigations().Where(navigation => navigation.TargetEntityType.IsOwned());

    private static IEnumerable<EntityEntry> ChildrenOf(DbContext context, EntityEntry owner, INavigation navigation)
    {
        IEntityType target = navigation.TargetEntityType;
        IForeignKey ownership = target.FindOwnership();

        if (ownership is null)
        {
            return [];
        }

        return context.ChangeTracker.Entries()
            .Where(entry => entry.Metadata == target)
            .Where(entry => IsOwnedBy(entry, ownership, owner));
    }

    private static bool IsOwnedBy(EntityEntry child, IForeignKey ownership, EntityEntry owner)
    {
        for (int index = 0; index < ownership.Properties.Count; index++)
        {
            object childValue = ValueOf(child, ownership.Properties[index].Name);
            object ownerValue = ValueOf(owner, ownership.PrincipalKey.Properties[index].Name);

            if (!Equals(childValue, ownerValue))
            {
                return false;
            }
        }

        return true;
    }

    // A deleted entry's CurrentValue is unreliable; its OriginalValue still
    // carries the key it was loaded with.
    private static object ValueOf(EntityEntry entry, string propertyName)
    {
        PropertyEntry property = entry.Property(propertyName);

        return entry.State == EntityState.Deleted ? property.OriginalValue : property.CurrentValue;
    }
}
