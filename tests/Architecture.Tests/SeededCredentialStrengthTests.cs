using System.Globalization;
using System.Text.Json;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Turns #2510's arithmetic ("a top-1000 list is exhaustible within the
/// lockout's budget") into something a reader can check, over
/// <c>smart-sentinel-eye-realm.json</c> alone (spec 219 Half A).
///
/// <para>
/// <b>Seven of the realm's twelve human accounts carry a password that is a
/// mechanical transform of that account's own username</b> — public data,
/// printed in the realm import itself. The candidate set an attacker needs for
/// those seven is not a thousand entries; it is, per account, on the order of
/// one, and the realm's own declared policy admits every one of them.
/// </para>
///
/// <para>
/// <b>The policy predicate is parsed out of the realm's <c>passwordPolicy</c>
/// string, never hard-coded.</b> An unrecognised clause throws, naming the
/// clause, rather than warning, skipping, or defaulting to <c>true</c> — a
/// silently-ignored clause would make every fact here pass for the wrong
/// reason on the day the policy changes.
/// </para>
///
/// <para>
/// <b>What this cannot see.</b> It is a guard that reads a design artefact,
/// which proves the design was written down and not that it holds — the realm
/// import can drift from what Keycloak actually imported. Spec 219 Half B
/// (<c>LockoutThroughputMeasurementTests</c>) asks the running server whether
/// it agreed; this file answers only from the file.
/// </para>
/// </summary>
public class SeededCredentialStrengthTests
{
    /// <summary>
    /// Assumption G1 (spec.md §Assumptions): the canonical "capitalise and
    /// append digits" mangling rule — the shape every published
    /// wordlist-mangling ruleset opens with. Written out here, not buried in
    /// prose, so a reader who disagrees with the rule can edit these three
    /// strings and re-run rather than re-derive the finding. It does not
    /// affect the finding's arithmetic: the finding is that the candidate set
    /// is derivable from public data at all.
    /// </summary>
    private static readonly string[] DerivationSuffixes = ["1234", "-1234", "_1234"];

    /// <summary>
    /// The password that reaches six accounts across four fab groups
    /// (spec.md §*And the reuse makes one guess cross a fab boundary*).
    /// Computed from the same derivation rule as <see cref="IsUsernameDerived"/>
    /// rather than typed as a literal — phase-6 review (spec 219): a literal
    /// constant is exactly what a human "fixing" this test after a password
    /// rotation would reach for, backwards, without reconsidering whether the
    /// underlying reuse itself needs fixing.
    /// </summary>
    private static readonly string SeededOperatorPassword = Capitalise("operator") + "1234";

    /// <summary>
    /// The password held by both accounts carrying the realm role "admin".
    /// Computed the same way, for the same reason.
    /// </summary>
    private static readonly string SeededAdminPassword = Capitalise("admin") + "1234";

    /// <summary>
    /// The drift guard every other number in this file rests on. Excludes
    /// <c>service-account-*</c> entries (five of them), which carry no
    /// <c>credentials</c> block; including them would make this census wrong
    /// in a way nobody would notice.
    /// </summary>
    [Fact]
    public void The_realm_seeds_twelve_human_credential_blocks_and_six_distinct_passwords()
    {
        SeededAccount[] accounts = HumanAccounts();
        string[] distinctPasswords = [.. accounts.Select(account => account.Password).Distinct(StringComparer.Ordinal)];

        accounts.Length.ShouldBe(
            12,
            customMessage: $"expected 12 seeded human credential blocks; found {accounts.Length}. Every "
            + "other number in this file rests on this census — a seeded account was added or removed "
            + "without updating it.");

        distinctPasswords.Length.ShouldBe(
            6,
            customMessage: $"expected 6 distinct seeded passwords; found {distinctPasswords.Length}: "
            + $"{string.Join(", ", distinctPasswords)}");
    }

    /// <summary>SC-1 — the policy admits every password the realm seeds.</summary>
    [Fact]
    public void Every_seeded_password_satisfies_the_realms_own_declared_policy()
    {
        string declared = DeclaredPolicy();
        PolicyPredicate policy = ParsePolicy(declared);
        SeededAccount[] accounts = HumanAccounts();
        int distinctCount = accounts.Select(account => account.Password).Distinct(StringComparer.Ordinal).Count();

        foreach (SeededAccount account in accounts)
        {
            policy.IsSatisfiedBy(account.Password, account.Username).ShouldBeTrue(
                customMessage: $"'{account.Username}' carries a password that does not satisfy the "
                + $"realm's own declared policy '{declared}'; checked {accounts.Length} credential "
                + $"blocks, {distinctCount} distinct values.");
        }
    }

    /// <summary>
    /// SC-4 — the counterfactual. Without this, SC-1 is satisfied by a
    /// predicate that returns <c>true</c> unconditionally. Each password below
    /// is built to break exactly one clause of the realm's declared policy
    /// (<c>length(8) and upperCase(1) and lowerCase(1) and digits(1)</c>), and
    /// the assertion checks it is refused for that specific clause — so a
    /// predicate that refuses everything would also fail this.
    /// </summary>
    [Fact]
    public void The_parsed_policy_refuses_a_password_that_breaks_each_clause()
    {
        string declared = DeclaredPolicy();
        PolicyPredicate policy = ParsePolicy(declared);
        const string probeUsername = "counterfactual-probe";

        (string Password, string ExpectedClause)[] counterfactuals =
        [
            ("alllowercase1", "upperCase(1)"),
            ("ALLUPPERCASE1", "lowerCase(1)"),
            ("NoDigitsHere", "digits(1)"),
            ("Ab1", "length(8)"),
        ];

        foreach ((string password, string expectedClause) in counterfactuals)
        {
            policy.IsSatisfiedBy(password, probeUsername).ShouldBeFalse(
                customMessage: $"'{password}' should be refused by the realm's declared policy "
                + $"'{declared}', or the policy predicate is vacuously true");

            PolicyClause? failed = policy.FirstUnsatisfiedClause(password, probeUsername);

            failed.ShouldNotBeNull(customMessage: $"'{password}' was refused but no clause reported why");
            failed.Name.ShouldBe(
                expectedClause,
                customMessage: $"'{password}' should fail clause '{expectedClause}' specifically, not "
                + $"'{failed.Name}' — a predicate that refuses everything for the wrong reason would "
                + "also pass this fact's first half");
        }
    }

    /// <summary>SC-2 — most of those passwords are already implied by the usernames.</summary>
    [Fact]
    public void Seven_of_the_twelve_seeded_accounts_have_a_username_derived_password()
    {
        SeededAccount[] accounts = HumanAccounts();
        SeededAccount[] derived = [.. accounts.Where(IsUsernameDerived)];
        string[] derivedUsernames = [.. derived.Select(account => account.Username)];

        derived.Length.ShouldBe(
            7,
            customMessage: $"expected 7 of {accounts.Length} seeded accounts to carry a password of "
            + $"Capitalise(local-part(username)) + one of [{string.Join(", ", DerivationSuffixes)}]; "
            + $"found {derived.Length}: {string.Join(", ", derivedUsernames)}");

        derivedUsernames.ShouldContain("admin", customMessage: "both admin-roled accounts should be caught");
        derivedUsernames.ShouldContain("admin@munich.test", customMessage: "both admin-roled accounts should be caught");
        derivedUsernames.ShouldContain("wall-munich");
        derivedUsernames.ShouldContain("wall-dresden");
        derivedUsernames.ShouldContain("wall-berlin");
        derivedUsernames.ShouldContain("wall-hamburg");
    }

    /// <summary>
    /// SC-3 — one guessed value crosses fab boundaries.
    /// <c>op-multi@smart-sentinel-eye.test</c> is in two fab groups at once, so
    /// the six accounts reach only four distinct groups, not six.
    /// </summary>
    [Fact]
    public void One_seeded_password_authenticates_six_accounts_across_four_fabs()
    {
        SeededAccount[] accounts = HumanAccounts();
        SeededAccount[] operatorAccounts = [
            .. accounts.Where(account => string.Equals(account.Password, SeededOperatorPassword, StringComparison.Ordinal)),
        ];
        string[] fabGroups = [.. operatorAccounts.SelectMany(account => account.Groups).Distinct(StringComparer.Ordinal)];

        operatorAccounts.Length.ShouldBe(
            6,
            customMessage: $"expected '{SeededOperatorPassword}' to authenticate 6 accounts; found "
            + $"{operatorAccounts.Length}: {string.Join(", ", operatorAccounts.Select(account => account.Username))}");

        fabGroups.Length.ShouldBe(
            4,
            customMessage: $"expected '{SeededOperatorPassword}' to reach 4 fab groups; found "
            + $"{fabGroups.Length}: {string.Join(", ", fabGroups)}");
    }

    /// <summary>SC-3 — the same guessed value crosses the realm's "admin" role too.</summary>
    [Fact]
    public void One_seeded_password_authenticates_both_realm_admin_accounts()
    {
        SeededAccount[] accounts = HumanAccounts();
        SeededAccount[] adminAccounts = [
            .. accounts.Where(account => string.Equals(account.Password, SeededAdminPassword, StringComparison.Ordinal)),
        ];

        adminAccounts.Length.ShouldBe(
            2,
            customMessage: $"expected '{SeededAdminPassword}' to authenticate 2 accounts; found "
            + $"{adminAccounts.Length}: {string.Join(", ", adminAccounts.Select(account => account.Username))}");

        adminAccounts.ShouldAllBe(
            account => account.RealmRoles.Contains("admin"),
            "both accounts opened by this password should carry the realm role 'admin', or the "
            + "reuse does not actually cross a privilege boundary");
    }

    /// <summary>
    /// SC-3, value-agnostic — phase-6 review (spec 219), the highest-severity
    /// finding of that round. The two facts above pin literal password
    /// strings, which is a good census-drift guard but the wrong instrument
    /// for the invariant they are supposed to protect: if the human decision
    /// is a password <b>rotation</b> (a new shared value, still reused across
    /// the same six accounts and four fabs) rather than de-duplication, both
    /// pinned facts fail with "found 0", and the obvious repair — update the
    /// two constants to the new literal — would make them pass again while
    /// the cross-fab reuse survives untouched, now re-certified by a green
    /// test that never had to look at the actual architecture.
    ///
    /// <para>
    /// This fact never names a literal password. It finds <b>whichever</b>
    /// password currently spans the most fab groups and asserts its shape
    /// directly — six accounts, four fabs. A lazy rotation changes nothing
    /// this fact reads, so it stays correctly green with no edit. A genuine
    /// de-duplication fix would drop every group's fab-span to one, which
    /// this fact would then catch as a value it has to be updated for, rather
    /// than something a constant swap can silently paper over.
    /// </para>
    ///
    /// <para>
    /// <c>OrderByDescending(...).First()</c> breaks a tie by group
    /// enumeration order, not by any property of the accounts. After a
    /// genuine de-duplication (every group's fab-span drops to one), the
    /// assertion below still fails correctly — <c>4</c> is expected and no
    /// group reaches it — but the failure message would name whichever
    /// single-fab password happened to enumerate first, not a specially
    /// meaningful one. The catch is real; the message in that edge case may
    /// not be the most informative one available.
    /// </para>
    /// </summary>
    [Fact]
    public void The_password_shared_across_the_most_fabs_still_spans_four_fabs_and_six_accounts()
    {
        SeededAccount[] accounts = HumanAccounts();

        IGrouping<string, SeededAccount> widest = accounts
            .GroupBy(account => account.Password, StringComparer.Ordinal)
            .OrderByDescending(group => group.SelectMany(account => account.Groups).Distinct(StringComparer.Ordinal).Count())
            .First();
        string[] fabGroups = [.. widest.SelectMany(account => account.Groups).Distinct(StringComparer.Ordinal)];

        fabGroups.Length.ShouldBe(
            4,
            customMessage: $"expected the most widely fab-shared seeded password to reach 4 fab groups, "
            + $"whatever it is currently spelled; the widest one ('{widest.Key}') reaches "
            + $"{fabGroups.Length}: {string.Join(", ", fabGroups)}");
        widest.Count().ShouldBe(
            6,
            customMessage: $"expected the most widely fab-shared seeded password to authenticate 6 "
            + $"accounts; the widest one ('{widest.Key}') authenticates {widest.Count()}: "
            + $"{string.Join(", ", widest.Select(account => account.Username))}");
    }

    /// <summary>
    /// SC-3, value-agnostic, the admin half — same reasoning as
    /// <see cref="The_password_shared_across_the_most_fabs_still_spans_four_fabs_and_six_accounts"/>,
    /// applied to the realm role rather than the fab groups. Same tie-breaking
    /// caveat: <c>OrderByDescending(...).First()</c> picks by enumeration
    /// order among equal candidates, so a post-de-duplication failure message
    /// would name an arbitrary account rather than a specially meaningful
    /// one — the assertion itself still fails correctly either way.
    /// </summary>
    [Fact]
    public void The_password_shared_by_the_most_admin_accounts_still_reaches_two_of_them()
    {
        SeededAccount[] accounts = HumanAccounts();

        var widest = accounts
            .GroupBy(account => account.Password, StringComparer.Ordinal)
            .Select(group => new
            {
                Password = group.Key,
                Usernames = group.Select(account => account.Username).ToArray(),
                AdminHolders = group.Count(account => account.RealmRoles.Contains("admin")),
            })
            .OrderByDescending(candidate => candidate.AdminHolders)
            .First();

        widest.AdminHolders.ShouldBe(
            2,
            customMessage: $"expected the seeded password shared by the most 'admin'-roled accounts to "
            + $"reach 2 of them, whatever it is currently spelled; the widest one ('{widest.Password}') "
            + $"reaches {widest.AdminHolders}: {string.Join(", ", widest.Usernames)}");
    }

    private static bool IsUsernameDerived(SeededAccount account)
    {
        string capitalisedLocalPart = Capitalise(LocalPart(account.Username));

        return DerivationSuffixes.Any(suffix =>
            string.Equals(account.Password, capitalisedLocalPart + suffix, StringComparison.Ordinal));
    }

    private static string LocalPart(string username)
    {
        int at = username.IndexOf('@', StringComparison.Ordinal);
        return at >= 0 ? username[..at] : username;
    }

    private static string Capitalise(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private sealed record SeededAccount(string Username, string Password, string[] Groups, string[] RealmRoles);

    private static SeededAccount[] HumanAccounts()
    {
        List<SeededAccount> accounts = [];

        foreach (JsonElement user in Realm().GetProperty("users").EnumerateArray())
        {
            string username = user.GetProperty("username").GetString() ?? string.Empty;
            if (username.StartsWith("service-account-", StringComparison.Ordinal))
            {
                continue;
            }

            accounts.Add(new SeededAccount(
                username,
                SeededPasswordOf(user),
                Strings(user, "groups"),
                Strings(user, "realmRoles")));
        }

        return [.. accounts];
    }

    private static string SeededPasswordOf(JsonElement user) =>
        user.GetProperty("credentials").EnumerateArray()
            .First(credential => credential.GetProperty("type").GetString() == "password")
            .GetProperty("value").GetString() ?? string.Empty;

    private static string[] Strings(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value)
            ? [.. value.EnumerateArray().Select(item => item.GetString() ?? string.Empty)]
            : [];

    /// <summary>
    /// One clause of a Keycloak <c>passwordPolicy</c> string, parsed to a
    /// predicate over a password (and, for <c>notUsername</c>, the username it
    /// belongs to). <see cref="Name"/> is the clause exactly as declared, so a
    /// failure message names the real clause rather than a paraphrase of it.
    /// </summary>
    private sealed record PolicyClause(string Name, Func<string, string, bool> IsSatisfied);

    /// <summary>
    /// The conjunction of every clause a <c>passwordPolicy</c> string
    /// declares.
    /// </summary>
    private sealed class PolicyPredicate(IReadOnlyList<PolicyClause> clauses)
    {
        public bool IsSatisfiedBy(string password, string username) =>
            clauses.All(clause => clause.IsSatisfied(password, username));

        public PolicyClause? FirstUnsatisfiedClause(string password, string username) =>
            clauses.FirstOrDefault(clause => !clause.IsSatisfied(password, username));
    }

    private static PolicyPredicate ParsePolicy(string declared)
    {
        PolicyClause[] clauses = [.. declared.Split(" and ", StringSplitOptions.TrimEntries).Select(ParseClause)];
        return new PolicyPredicate(clauses);
    }

    /// <summary>
    /// An unrecognised clause throws, naming the clause. It does not warn,
    /// skip, or default to <c>true</c> — a silently-ignored clause would make
    /// <see cref="Every_seeded_password_satisfies_the_realms_own_declared_policy"/>
    /// pass for the wrong reason on the day the policy changes (plan.md §2.2).
    /// <c>specialChars</c> and <c>notUsername</c> are modelled although today's
    /// policy uses neither, because they are the two clauses a human raising
    /// the policy is most likely to add.
    /// </summary>
    private static PolicyClause ParseClause(string raw)
    {
        int open = raw.IndexOf('(', StringComparison.Ordinal);
        string name = open >= 0 ? raw[..open] : raw;
        string argument = open >= 0 && raw.EndsWith(')') ? raw[(open + 1)..^1] : string.Empty;

        return name switch
        {
            "length" => new PolicyClause(raw, (password, _) => password.Length >= ParseInt(argument)),
            "upperCase" => new PolicyClause(raw, (password, _) => password.Count(char.IsUpper) >= ParseInt(argument)),
            "lowerCase" => new PolicyClause(raw, (password, _) => password.Count(char.IsLower) >= ParseInt(argument)),
            "digits" => new PolicyClause(raw, (password, _) => password.Count(char.IsDigit) >= ParseInt(argument)),
            "specialChars" => new PolicyClause(
                raw,
                (password, _) => password.Count(character => !char.IsLetterOrDigit(character)) >= ParseInt(argument)),
            "notUsername" => new PolicyClause(raw, (password, username) => !string.Equals(password, username, StringComparison.Ordinal)),
            _ => throw new InvalidOperationException(
                $"passwordPolicy clause '{raw}' is not modelled by {nameof(ParseClause)}; add it here before "
                + "trusting this file's numbers"),
        };
    }

    private static int ParseInt(string value) => int.Parse(value, CultureInfo.InvariantCulture);

    private static string DeclaredPolicy() => Realm().GetProperty("passwordPolicy").GetString() ?? string.Empty;

    private static JsonElement Realm() => RealmDocument.RootElement;

    /// <summary>
    /// Parsed once and held: a <see cref="JsonElement"/> is only valid while
    /// its document is alive, and every fact here reads the same file.
    /// </summary>
    private static readonly JsonDocument RealmDocument = ReadRealm();

    private static JsonDocument ReadRealm()
    {
        DirectoryInfo root = RepositorySource.Root();

        string path = Path.Combine(root.FullName, "src", "AppHost", "Realms", "smart-sentinel-eye-realm.json");
        File.Exists(path).ShouldBeTrue($"the realm should be at {path}");

        // The file carries a byte-order mark, which the JSON reader rejects.
        string text = File.ReadAllText(path).TrimStart('﻿');
        return JsonDocument.Parse(text);
    }
}
