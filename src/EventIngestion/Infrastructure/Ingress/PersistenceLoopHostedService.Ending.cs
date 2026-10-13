using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Ingress;

public sealed partial class PersistenceLoopHostedService
{
    private enum Outcome
    {
        /// <summary>It is in the database. Acknowledge it.</summary>
        Stored,

        /// <summary>No rule will ever accept it. Record it, then acknowledge.</summary>
        Rejected,

        /// <summary>Something transient. Keep it, and keep the sender's copy.</summary>
        Failed,
    }

    /// <summary>
    /// An <see cref="Outcome"/> paired with the reason it owes a record, when
    /// it owes one at all. <see cref="Rejected"/> is the only way to produce a
    /// rejected ending, and it requires a <see cref="RejectionReason"/> — so a
    /// rejection with no reason cannot be represented, let alone written
    /// (spec 213, issue #2428).
    ///
    /// <para>
    /// The constructor is private and the properties are get-only rather than
    /// the usual positional-record shape: a positional primary constructor is
    /// exactly as accessible as the type, and a record's <c>with</c> expression
    /// reaches every property regardless — both would let something inside
    /// this class build a <see cref="Outcome.Rejected"/> ending with no
    /// reason, which is the one thing this type exists to rule out.
    /// </para>
    /// </summary>
    private readonly record struct Ending
    {
        private Ending(Outcome outcome, Option<RejectionReason> reason)
        {
            Outcome = outcome;
            Reason = reason;
        }

        public Outcome Outcome { get; }

        public Option<RejectionReason> Reason { get; }

        public static Ending Stored { get; } = new(Outcome.Stored, Option<RejectionReason>.None);

        public static Ending Failed { get; } = new(Outcome.Failed, Option<RejectionReason>.None);

        public static Ending Rejected(RejectionReason reason) =>
            new(Outcome.Rejected, Option<RejectionReason>.Some(reason));
    }
}
