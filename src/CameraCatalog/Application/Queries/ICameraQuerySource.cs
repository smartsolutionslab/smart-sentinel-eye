using System.Linq.Expressions;
using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Application.Queries;

/// <summary>
/// Read-side seam: exposes an IQueryable&lt;Camera&gt; so the query handler can
/// push sort + pagination into SQL. Implementation in Infrastructure wraps
/// the EF Core DbContext; the in-memory fake in tests wraps a list.
/// </summary>
public interface ICameraQuerySource
{
    IQueryable<Camera> Cameras { get; }

    /// <summary>
    /// A predicate matching cameras whose <b>normalised</b> name contains
    /// <paramref name="normalizedFragment"/> (spec 055).
    ///
    /// <para>
    /// <b>It comes from the seam because the two sides cannot share one
    /// expression.</b> Against EF the normalised name is a shadow property over
    /// a generated column and is reached with <c>EF.Property</c>, which the
    /// in-memory fake cannot evaluate. Against the fake it is
    /// <c>Name.NormalizedValue</c>, which EF cannot translate. A handler written
    /// for either one breaks the other, silently in the first case — the query
    /// would client-evaluate or throw depending on the provider.
    /// </para>
    ///
    /// <para>
    /// <b>The fragment arrives already normalised</b>, so the caller's
    /// normalisation and the column's are decided in one place rather than two.
    /// Matching is a plain substring containment: the implementation must treat
    /// the fragment as text, not as a pattern, so a camera called
    /// <c>50% Load</c> is found by typing <c>%</c>.
    /// </para>
    /// </summary>
    Expression<Func<Camera, bool>> NameContains(string normalizedFragment);
}
