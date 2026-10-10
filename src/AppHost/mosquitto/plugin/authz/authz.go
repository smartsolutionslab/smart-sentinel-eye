// Package authz is the enforcement half of ADR-0100's publish rule (spec
// 330, #2286): a PUBLISH is refused when the connecting identity's token
// did not carry sse.events.publish. It is pure Go (stdlib only) and knows
// nothing about cgo or Mosquitto, so it can be unit-tested with
// CGO_ENABLED=0 (jwt_auth.go's build tags cannot: the test binary would
// inherit CGO_LDFLAGS="-shared …" and link as a shared object).
package authz

import "sync"

// PublishScope is a fifth spelling of Scope.Sse.Events.Publish (Go cannot
// import a C# const); AS-4's architecture guard and the realm JSON hold the
// C# side.
const PublishScope = "sse.events.publish"

// HasScope reports whether claim — the raw `scope` value out of
// jwt.MapClaims — carries scope as an exact, space-delimited token. Mirrors
// RequireScopeExtensions.AddScopePolicies: the claim is split on spaces,
// empty tokens (leading/trailing/repeated spaces) are dropped, and the
// comparison is ordinal. Any claim that is not a string — nil, a missing
// claim, a JSON array, a number — reports false rather than panicking.
func HasScope(claim any, scope string) bool {
	value, ok := claim.(string)
	if !ok {
		return false
	}

	start := -1
	for index := 0; index <= len(value); index++ {
		atSpace := index == len(value) || value[index] == ' '
		if !atSpace && start < 0 {
			start = index
			continue
		}
		if atSpace && start >= 0 {
			if value[start:index] == scope {
				return true
			}
			start = -1
		}
	}

	return false
}

// Verdict is the only vocabulary Decide has. There is deliberately no
// "allow" member (spec finding 2): the strongest thing the ACL callback can
// say is "not refused by me" — acl.txt still has to grant the topic.
type Verdict int

const (
	// Defer lets the next MOSQ_EVT_ACL_CHECK callback (acl.txt) decide.
	Defer Verdict = iota
	// Deny refuses the write outright.
	Deny
)

// Grants remembers, per MQTT username, whether that identity's most recent
// successful JWT authentication carried PublishScope. It is keyed by
// username rather than by connection because a client's Will is checked
// after its disconnect event fires (plan.md §3) — a per-connection verdict
// would already be gone by then, and the check would fail open onto
// acl.txt alone.
type Grants struct {
	mu         sync.RWMutex
	byUsername map[string]bool
}

// NewGrants returns an empty, concurrency-safe Grants.
func NewGrants() *Grants {
	return &Grants{byUsername: make(map[string]bool)}
}

// Record sets username's verdict to canPublish, replacing whatever was
// recorded for it before. Only a successful JWT authentication should call
// this (spec A2): the most recent token a client mints is authoritative for
// every session it holds.
func (g *Grants) Record(username string, canPublish bool) {
	g.mu.Lock()
	defer g.mu.Unlock()
	g.byUsername[username] = canPublish
}

// Decide answers an MOSQ_EVT_ACL_CHECK for username. Every case other than
// "a write by an identity last recorded without the scope" defers: a read,
// subscribe or unsubscribe; a write by an identity never seen on the JWT
// path (a password-file user); and a write by an identity last recorded
// with the scope.
func (g *Grants) Decide(username string, isWrite bool) Verdict {
	if !isWrite {
		return Defer
	}

	g.mu.RLock()
	canPublish, known := g.byUsername[username]
	g.mu.RUnlock()

	if !known || canPublish {
		return Defer
	}

	return Deny
}
