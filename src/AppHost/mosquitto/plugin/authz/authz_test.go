// Package authz_test pins the publish-scope decision table (spec 330,
// #2286): every AS-5 and AS-6 row from plan.md §5 T-C, plus both overwrite
// directions on Grants.Record. It uses only the standard testing package —
// this repo has no Go dependency management beyond the stdlib.
package authz_test

import (
	"testing"

	"smartsentineleye.local/mosquitto-jwt-auth/authz"
)

// AS-5: scope parsing is exact.
func Test_HasScope_reports_whether_the_scope_claim_carries_the_exact_token(t *testing.T) {
	cases := []struct {
		name  string
		claim any
		want  bool
	}{
		{"a_scope_token_among_others_grants_publish", "openid sse.events.publish profile", true},
		{"the_sole_scope_token_grants_publish", "sse.events.publish", true},
		{"a_token_with_an_extra_trailing_word_does_not_grant", "sse.events.publisher", false},
		{"a_token_with_a_leading_character_does_not_grant", "xsse.events.publish", false},
		{"a_different_case_token_does_not_grant", "SSE.EVENTS.PUBLISH", false},
		{"an_empty_string_claim_does_not_grant", "", false},
		{"a_missing_claim_does_not_grant", nil, false},
		{"a_json_array_claim_does_not_grant", []any{"sse.events.publish"}, false},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			got := authz.HasScope(testCase.claim, authz.PublishScope)
			if got != testCase.want {
				t.Errorf("HasScope(%#v, %q) = %v, want %v", testCase.claim, authz.PublishScope, got, testCase.want)
			}
		})
	}
}

// AS-6, first four rows: the verdict table.
func Test_Grants_Decide_answers_the_verdict_table(t *testing.T) {
	t.Run("a_write_by_an_identity_last_authenticated_without_the_scope_is_denied", func(t *testing.T) {
		grants := authz.NewGrants()
		grants.Record("event-ingestion", false)

		got := grants.Decide("event-ingestion", true)

		if got != authz.Deny {
			t.Errorf("Decide(write, no scope) = %v, want Deny", got)
		}
	})

	t.Run("a_write_by_an_identity_last_authenticated_with_the_scope_defers", func(t *testing.T) {
		grants := authz.NewGrants()
		grants.Record("scenario-simulator", true)

		got := grants.Decide("scenario-simulator", true)

		if got != authz.Defer {
			t.Errorf("Decide(write, has scope) = %v, want Defer", got)
		}
	})

	t.Run("a_write_by_an_identity_never_seen_by_the_jwt_path_defers", func(t *testing.T) {
		grants := authz.NewGrants()

		got := grants.Decide("password-file-user", true)

		if got != authz.Defer {
			t.Errorf("Decide(write, unknown username) = %v, want Defer", got)
		}
	})

	t.Run("a_read_defers_regardless_of_scope", func(t *testing.T) {
		withoutScope := authz.NewGrants()
		withoutScope.Record("event-ingestion", false)
		withScope := authz.NewGrants()
		withScope.Record("scenario-simulator", true)
		unknown := authz.NewGrants()

		cases := []struct {
			name   string
			grants *authz.Grants
			user   string
		}{
			{"recorded_without_scope", withoutScope, "event-ingestion"},
			{"recorded_with_scope", withScope, "scenario-simulator"},
			{"never_recorded", unknown, "password-file-user"},
		}

		for _, testCase := range cases {
			got := testCase.grants.Decide(testCase.user, false)
			if got != authz.Defer {
				t.Errorf("Decide(%s, read) = %v, want Defer", testCase.name, got)
			}
		}
	})
}

// AS-6, last row: the most recent authentication replaces the earlier
// verdict, in both directions.
func Test_Grants_Record_replaces_the_earlier_verdict_in_both_directions(t *testing.T) {
	t.Run("a_second_record_without_the_scope_overwrites_an_earlier_grant", func(t *testing.T) {
		grants := authz.NewGrants()
		grants.Record("scenario-simulator", true)
		grants.Record("scenario-simulator", false)

		got := grants.Decide("scenario-simulator", true)

		if got != authz.Deny {
			t.Errorf("Decide after with-scope-then-without-scope = %v, want Deny", got)
		}
	})

	t.Run("a_second_record_with_the_scope_overwrites_an_earlier_denial", func(t *testing.T) {
		grants := authz.NewGrants()
		grants.Record("event-ingestion", false)
		grants.Record("event-ingestion", true)

		got := grants.Decide("event-ingestion", true)

		if got != authz.Defer {
			t.Errorf("Decide after without-scope-then-with-scope = %v, want Defer", got)
		}
	})
}
