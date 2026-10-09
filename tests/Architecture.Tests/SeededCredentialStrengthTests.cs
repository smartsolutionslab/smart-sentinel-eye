using System.Globalization;
using System.Text.Json;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 325 (issue #2510) — inverts spec 219's characterisation of the
/// defect this file used to prove. Spec 219 measured that the dev realm's
/// policy admits a guess space the lockout's own budget (spec 207, #2285)
/// outlasts, and that seven of twelve seeded accounts carry a password a
/// username alone implies. This file now asserts the <b>remedy</b> (plan.md
/// §2-§3): a policy raised to <c>length(15) and upperCase(1) and
/// lowerCase(1) and digits(1) and specialChars(1) and notContainsUsername
/// and notEmail</c>, and twelve seeded accounts with twelve distinct,
/// non-derived passwords.
///
/// <para>
/// <b>Colour (tasks.md phase 4a):</b> every fact below except
/// <see cref="Every_seeded_password_satisfies_the_realms_own_declared_policy"/>
/// (SC-1, the guard for assumption A1) is <b>red</b> against the unpatched
/// realm — phase 4b has not landed the new policy string or the new
/// credentials yet.
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
/// which proves the design was written down and not that it holds — the
/// realm import can drift from what Keycloak actually imported.
/// <c>PasswordPolicyEnforcementIntegrationTests</c> (AS-3) asks the running
/// server whether it agreed; this file answers only from the file.
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
    /// The six usernames whose retired password was
    /// <c>Capitalise(username) + suffix</c> (spec 219's finding; plan.md §4's
    /// "six retired literals") — the four wall accounts took the
    /// <c>"-1234"</c> member of <see cref="DerivationSuffixes"/>, the two
    /// role accounts the bare <c>"1234"</c> member. Computed here, not typed
    /// as the six literal passwords themselves — same reasoning as
    /// <see cref="IsUsernameDerived"/>: a literal constant is exactly what a
    /// human "fixing" this test after a rotation would reach for, backwards,
    /// without reconsidering whether the underlying weakness is actually
    /// fixed.
    /// </summary>
    private static readonly (string Username, string Suffix)[] RetiredSeededCredentials =
    [
        ("wall-munich", "-1234"),
        ("wall-dresden", "-1234"),
        ("wall-berlin", "-1234"),
        ("wall-hamburg", "-1234"),
        ("admin", "1234"),
        ("operator", "1234"),
    ];

    /// <summary>
    /// The probe identity shared by every SC-4 counterfactual below. Long
    /// enough on its own that no counterfactual password can embed it and
    /// still read as a short, obviously-contrived probe.
    /// </summary>
    private const string ProbeUsername = "counterfactual-probe";

    private const string ProbeEmail = "counterfactual-probe@policy.test";

    /// <summary>
    /// Built to be class-compliant (upper, lower, digit, special, 15+ chars)
    /// and to <b>not</b> contain <see cref="ProbeUsername"/>, so using it as
    /// both the counterfactual's email and its password isolates the
    /// <c>notEmail</c> clause from <c>notContainsUsername</c>.
    /// </summary>
    private const string EmailOnlyCounterfactualPassword = "Policy-NotEmail-Probe9@class.test";

    /// <summary>
    /// The drift guard every other number in this file rests on. Excludes
    /// <c>service-account-*</c> entries (five of them), which carry no
    /// <c>credentials</c> block; including them would make this census wrong
    /// in a way nobody would notice.
    /// </summary>
    [Fact]
    public void The_realm_seeds_twelve_human_credential_blocks_with_twelve_distinct_passwords()
    {
        SeededAccount[] accounts = HumanAccounts();
        string[] distinctPasswords = [.. accounts.Select(account => account.Password).Distinct(StringComparer.Ordinal)];

        accounts.Length.ShouldBe(
            12,
            customMessage: $"expected 12 seeded human credential blocks; found {accounts.Length}. Every "
            + "other number in this file rests on this census — a seeded account was added or removed "
            + "without updating it.");

        distinctPasswords.Length.ShouldBe(
            12,
            customMessage: $"expected 12 distinct seeded passwords (plan.md §3 — twelve accounts, "
            + $"twelve distinct secrets, D1); found {distinctPasswords.Length}: "
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
            policy.IsSatisfiedBy(account.Password, new Candidate(account.Username, account.Email)).ShouldBeTrue(
                customMessage: $"'{account.Username}' carries a password that does not satisfy the "
                + $"realm's own declared policy '{declared}'; checked {accounts.Length} credential "
                + $"blocks, {distinctCount} distinct values.");
        }
    }

    /// <summary>
    /// SC-4 — the counterfactual. Without this, SC-1 is satisfied by a
    /// predicate that returns <c>true</c> unconditionally. Each password
    /// below is built to be 15+ characters and to break exactly one clause
    /// of the realm's declared policy, and the assertion checks it is
    /// refused for that specific clause — so a predicate that refuses
    /// everything would also fail this. Rebuilt for spec 325: every probe
    /// below is long enough that it cannot trip <c>length(15)</c> by
    /// accident, except the one built to prove that clause.
    /// </summary>
    [Fact]
    public void The_parsed_policy_refuses_a_password_that_breaks_each_clause()
    {
        string declared = DeclaredPolicy();
        PolicyPredicate policy = ParsePolicy(declared);
        Candidate probe = new(ProbeUsername, ProbeEmail);
        Candidate emailProbe = new(ProbeUsername, EmailOnlyCounterfactualPassword);

        (string Password, Candidate Candidate, string ExpectedClause)[] counterfactuals =
        [
            ("Abcdef-1", probe, "length(15)"),
            ("abcdefghijk-123", probe, "upperCase(1)"),
            ("ABCDEFGHIJK-123", probe, "lowerCase(1)"),
            ("Abcdefghijk-lmn", probe, "digits(1)"),
            ("Abcdefghijklmn1", probe, "specialChars(1)"),
            ($"Xy9-{ProbeUsername}", probe, "notContainsUsername"),
            (EmailOnlyCounterfactualPassword, emailProbe, "notEmail"),
        ];

        foreach ((string password, Candidate candidate, string expectedClause) in counterfactuals)
        {
            policy.IsSatisfiedBy(password, candidate).ShouldBeFalse(
                customMessage: $"'{password}' should be refused by the realm's declared policy "
                + $"'{declared}', or the policy predicate is vacuously true");

            PolicyClause? failed = policy.FirstUnsatisfiedClause(password, candidate);

            failed.ShouldNotBeNull(customMessage: $"'{password}' was refused but no clause reported why");
            failed.Name.ShouldBe(
                expectedClause,
                customMessage: $"'{password}' should fail clause '{expectedClause}' specifically, not "
                + $"'{failed.Name}' — a predicate that refuses everything for the wrong reason would "
                + "also pass this fact's first half");
        }
    }

    /// <summary>
    /// SC-2, inverted — spec 219's finding was that seven of twelve were
    /// username-derived; spec 325's remedy is that none are (plan.md §3:
    /// twelve values, none derived from any username, fab or role).
    /// </summary>
    [Fact]
    public void None_of_the_twelve_seeded_accounts_have_a_username_derived_password()
    {
        SeededAccount[] accounts = HumanAccounts();
        SeededAccount[] derived = [.. accounts.Where(IsUsernameDerived)];
        string[] derivedUsernames = [.. derived.Select(account => account.Username)];

        derived.Length.ShouldBe(
            0,
            customMessage: $"expected none of the {accounts.Length} seeded accounts to carry a "
            + $"password of Capitalise(local-part(username)) + one of "
            + $"[{string.Join(", ", DerivationSuffixes)}]; found {derived.Length}: "
            + $"{string.Join(", ", derivedUsernames)}");
    }

    /// <summary>
    /// SC-3, inverted — spec 219's phase-6 review found that a literal-pinned
    /// reuse fact and its value-agnostic counterpart (four facts in total)
    /// could each be "fixed" by a password rotation that kept the reuse. The
    /// remedy spec 325 encodes (D1) is de-duplication, not rotation, so the
    /// single fact that actually matters is simpler than any of the four it
    /// replaces: no seeded password is shared by more than one account, full
    /// stop — nothing left for a lazy rotation to slip past.
    /// </summary>
    [Fact]
    public void No_seeded_password_is_held_by_more_than_one_account()
    {
        SeededAccount[] accounts = HumanAccounts();

        var duplicates = accounts
            .GroupBy(account => account.Password, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => new
            {
                Password = group.Key,
                Usernames = group.Select(account => account.Username).ToArray(),
            })
            .ToArray();

        duplicates.ShouldBeEmpty(
            customMessage: $"expected every one of the {accounts.Length} seeded passwords to belong "
            + $"to exactly one account; found {duplicates.Length} password(s) shared by more than "
            + "one: "
            + string.Join("; ", duplicates.Select(duplicate =>
                $"'{duplicate.Password}' -> {string.Join(", ", duplicate.Usernames)}")));
    }

    /// <summary>
    /// New for spec 325 — the six retired values (spec 219's finding) must
    /// themselves now be refused by the realm's own declared policy. Built
    /// from the same derivation rule as <see cref="IsUsernameDerived"/>, not
    /// typed as literals, for the same reason.
    /// </summary>
    [Fact]
    public void Every_retired_seeded_password_is_refused_by_the_realms_declared_policy()
    {
        string declared = DeclaredPolicy();
        PolicyPredicate policy = ParsePolicy(declared);
        string[] retired = [.. RetiredSeededCredentials.Select(credential => Capitalise(credential.Username) + credential.Suffix)];
        Candidate retiredProbe = new("retired-probe", "retired-probe@policy.test");

        foreach (string password in retired)
        {
            policy.IsSatisfiedBy(password, retiredProbe).ShouldBeFalse(
                customMessage: $"expected the realm's declared policy '{declared}' to refuse the "
                + $"retired seeded password '{password}'; it was accepted.");
        }
    }

    /// <summary>
    /// New for spec 325 — the policy string itself must declare the raised
    /// minimum length and the three new clauses plan.md §2 computes
    /// (<c>specialChars</c>, <c>notContainsUsername</c>, <c>notEmail</c>).
    /// </summary>
    [Fact]
    public void The_declared_policy_requires_at_least_fifteen_characters_and_the_three_new_clauses()
    {
        string declared = DeclaredPolicy();
        (string Name, string Argument)[] rawClauses =
            [.. declared.Split(" and ", StringSplitOptions.TrimEntries).Select(SplitClause)];
        string[] clauseNames = [.. rawClauses.Select(clause => clause.Name)];

        rawClauses.ShouldContain(
            clause => clause.Name == "length",
            customMessage: $"expected a 'length(...)' clause in the declared policy '{declared}'.");
        int declaredMinimumLength = ParseInt(rawClauses.First(clause => clause.Name == "length").Argument);

        declaredMinimumLength.ShouldBeGreaterThanOrEqualTo(
            15,
            customMessage: $"expected the realm's declared policy to require at least 15 characters; "
            + $"found 'length({declaredMinimumLength})' in '{declared}'.");

        clauseNames.ShouldContain(
            "specialChars",
            customMessage: $"expected 'specialChars' in the declared policy '{declared}'.");
        clauseNames.ShouldContain(
            "notContainsUsername",
            customMessage: $"expected 'notContainsUsername' in the declared policy '{declared}'.");
        clauseNames.ShouldContain(
            "notEmail",
            customMessage: $"expected 'notEmail' in the declared policy '{declared}'.");
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

    private sealed record SeededAccount(string Username, string Email, string Password, string[] Groups, string[] RealmRoles);

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

            string email = user.TryGetProperty("email", out JsonElement emailProperty)
                ? emailProperty.GetString() ?? string.Empty
                : string.Empty;

            accounts.Add(new SeededAccount(
                username,
                email,
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
    /// The identity a clause is checked against — <c>notUsername</c> and
    /// <c>notContainsUsername</c> need the username, <c>notEmail</c> needs
    /// the email; every other clause ignores this entirely.
    /// </summary>
    private sealed record Candidate(string Username, string Email);

    /// <summary>
    /// One clause of a Keycloak <c>passwordPolicy</c> string, parsed to a
    /// predicate over a password and the <see cref="Candidate"/> it belongs
    /// to. <see cref="Name"/> is the clause exactly as declared, so a
    /// failure message names the real clause rather than a paraphrase of it.
    /// </summary>
    private sealed record PolicyClause(string Name, Func<string, Candidate, bool> IsSatisfied);

    /// <summary>
    /// The conjunction of every clause a <c>passwordPolicy</c> string
    /// declares.
    /// </summary>
    private sealed class PolicyPredicate(IReadOnlyList<PolicyClause> clauses)
    {
        public bool IsSatisfiedBy(string password, Candidate candidate) =>
            clauses.All(clause => clause.IsSatisfied(password, candidate));

        public PolicyClause? FirstUnsatisfiedClause(string password, Candidate candidate) =>
            clauses.FirstOrDefault(clause => !clause.IsSatisfied(password, candidate));
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
    ///
    /// <para>
    /// <c>notContainsUsername</c> (lower-cases both sides then
    /// <c>contains</c>) and <c>notEmail</c> (<c>equalsIgnoreCase</c>) are
    /// modelled per the verification against the pinned Keycloak 26.6.4 jar
    /// (spec.md §Decision). <c>notUsername</c> is compared
    /// <see cref="StringComparison.OrdinalIgnoreCase"/>, matching the jar's
    /// <c>equalsIgnoreCase</c> — a correction from this file's previous,
    /// case-sensitive modelling.
    /// </para>
    /// </summary>
    private static PolicyClause ParseClause(string raw)
    {
        (string name, string argument) = SplitClause(raw);

        return name switch
        {
            "length" => new PolicyClause(raw, (password, _) => password.Length >= ParseInt(argument)),
            "upperCase" => new PolicyClause(raw, (password, _) => password.Count(char.IsUpper) >= ParseInt(argument)),
            "lowerCase" => new PolicyClause(raw, (password, _) => password.Count(char.IsLower) >= ParseInt(argument)),
            "digits" => new PolicyClause(raw, (password, _) => password.Count(char.IsDigit) >= ParseInt(argument)),
            "specialChars" => new PolicyClause(
                raw,
                (password, _) => password.Count(character => !char.IsLetterOrDigit(character)) >= ParseInt(argument)),
            "notUsername" => new PolicyClause(
                raw,
                (password, candidate) => !string.Equals(password, candidate.Username, StringComparison.OrdinalIgnoreCase)),
            "notContainsUsername" => new PolicyClause(
                raw,
                (password, candidate) => !password.Contains(candidate.Username, StringComparison.OrdinalIgnoreCase)),
            "notEmail" => new PolicyClause(
                raw,
                (password, candidate) => !string.Equals(password, candidate.Email, StringComparison.OrdinalIgnoreCase)),
            _ => throw new InvalidOperationException(
                $"passwordPolicy clause '{raw}' is not modelled by {nameof(ParseClause)}; add it here before "
                + "trusting this file's numbers"),
        };
    }

    private static (string Name, string Argument) SplitClause(string raw)
    {
        int open = raw.IndexOf('(', StringComparison.Ordinal);
        string name = open >= 0 ? raw[..open] : raw;
        string argument = open >= 0 && raw.EndsWith(')') ? raw[(open + 1)..^1] : string.Empty;
        return (name, argument);
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
