// Mosquitto v5 auth plugin (spec 008 ADR-0100, NFR-002).
//
// Authenticates an MQTT client whose password is a Keycloak-minted
// RS256 JWT, verifying the signature against the realm's JWKS. The JWK
// set is fetched once and kept fresh in-process, so a CONNECT never
// round-trips to Keycloak — that is what keeps device connect-time
// auth inside the NFR-002 ≤ 5 ms p99 budget. keyfunc re-fetches on an
// unknown key id, so signing-key rotation is handled transparently
// (FR-006).
//
// Auth chain: a password that is not a three-segment JWT is deferred
// (MOSQ_ERR_PLUGIN_DEFER) to Mosquitto's static password_file, so the
// spec-006 seeded users (station-4, camera-12, event-ingestion) keep
// authenticating with their passwords. A JWT-shaped password is fully
// validated here and either grants (MOSQ_ERR_SUCCESS) or rejects
// (MOSQ_ERR_AUTH); it never falls through to the password file.
//
// What a JWT-shaped password must satisfy: an RS256 signature from the
// realm JWKS, an exp, an aud naming smart-sentinel-eye-api — the same
// audience the nine HTTP APIs and the WHEP hook require (spec 090
// FR-001) — an azp equal to the MQTT username, and an iss ending in the
// realm path.
//
// A successful authentication also records, per username, whether the
// token's scope claim carried sse.events.publish (spec 330, #2286). That
// verdict is read back on every PUBLISH by a second callback,
// MOSQ_EVT_ACL_CHECK: a write by an identity last authenticated without the
// scope is refused outright; every other case — a read/subscribe/
// unsubscribe, an identity never seen on this path, or a write by an
// identity that does carry the scope — defers to acl_file, which still
// decides topic authority. The callback never grants: the strongest thing
// it can say is "not refused by me" (package authz's Decide has no allow
// outcome). See authz/authz.go and plan.md §2-3 for the keying-by-username
// reasoning (a Will is checked after disconnect fires, so a per-connection
// verdict would already be gone by then).
//
// The mosquitto_plugin_* entry points are defined in mosquitto_glue.c
// with the const-correct signatures the headers require (cgo cannot
// emit `const`, so those symbols cannot be //export'd from Go). They
// forward to the exported Go functions below.
package main

/*
#include <stdlib.h>
#include <mosquitto.h>
#include <mosquitto_broker.h>
#include <mosquitto_plugin.h>

// Defined in mosquitto_glue.c.
int sse_register(mosquitto_plugin_id_t *id);
*/
import "C"

import (
	"fmt"
	"net/http"
	"os"
	"strings"
	"sync"
	"time"
	"unsafe"

	"github.com/MicahParks/keyfunc/v3"
	"github.com/golang-jwt/jwt/v5"

	"smartsentineleye.local/mosquitto-jwt-auth/authz"
)

const defaultRealmPath = "/realms/smart-sentinel-eye"

// The API every token in this realm is minted for, put on the token by
// the sse-audience client scope. A fourth spelling of a literal
// AuthenticationDefaults.ApiAudience, RealmAudienceTests and
// BearerAudienceTests already carry — Go cannot import a C# constant.
const apiAudience = "smart-sentinel-eye-api"

var (
	mutex     sync.RWMutex
	jwks      keyfunc.Keyfunc
	jwksURI   string
	realmPath = defaultRealmPath
)

// grants remembers, per MQTT username, whether that identity's most recent
// successful JWT authentication carried sse.events.publish. sseOnBasicAuth
// records into it; sseOnAclCheck reads it back on every PUBLISH.
var grants = authz.NewGrants()

//export goPluginInit
func goPluginInit(identifier *C.mosquitto_plugin_id_t, opts *C.struct_mosquitto_opt, optCount C.int) C.int {
	for _, opt := range unsafe.Slice(opts, int(optCount)) {
		switch C.GoString(opt.key) {
		case "jwt_jwks_uri":
			jwksURI = C.GoString(opt.value)
		case "jwt_realm_path":
			if value := C.GoString(opt.value); value != "" {
				realmPath = value
			}
		}
	}
	// Aspire injects the container-reachable Keycloak URL as an env var;
	// it wins over a static config value so the dev stack needs no
	// hard-coded host.
	if uri := os.Getenv("SSE_JWT_JWKS_URI"); uri != "" {
		jwksURI = uri
	}
	if jwksURI == "" {
		logf("no JWKS URI configured (plugin_opt_jwt_jwks_uri / SSE_JWT_JWKS_URI); JWT auth disabled")
	} else {
		logf("JWT auth enabled; JWKS source %s", jwksURI)
		go loadJwks()
	}
	return C.sse_register(identifier)
}

// loadJwks retries until Keycloak is reachable: the broker may start
// before Keycloak is ready, and the first fetch must not crash init.
// keyfunc.NewDefault does not fail on an unreachable endpoint (it sets
// up a background refresh and returns a keyless set), so we probe the
// endpoint first and only accept the set once the keys are fetchable.
func loadJwks() {
	for attempt := 1; ; attempt++ {
		if reachable(jwksURI) {
			set, err := keyfunc.NewDefault([]string{jwksURI})
			if err == nil {
				mutex.Lock()
				jwks = set
				mutex.Unlock()
				logf("JWKS loaded after %d attempt(s)", attempt)
				return
			}
			logf("keyfunc init failed (attempt %d): %v", attempt, err)
		} else if attempt == 1 || attempt%15 == 0 {
			logf("JWKS not reachable yet (attempt %d): %s", attempt, jwksURI)
		}
		time.Sleep(2 * time.Second)
	}
}

// reachable returns true when the JWKS endpoint answers 200 — keyfunc's
// own constructor tolerates failure, so this is what makes the retry
// loop meaningful.
func reachable(uri string) bool {
	client := http.Client{Timeout: 3 * time.Second}
	response, err := client.Get(uri)
	if err != nil {
		return false
	}
	defer response.Body.Close()
	return response.StatusCode == http.StatusOK
}

func logf(format string, args ...any) {
	fmt.Fprintf(os.Stderr, "[sse-jwt-auth] "+format+"\n", args...)
}

//export sseOnBasicAuth
func sseOnBasicAuth(event C.int, eventData unsafe.Pointer, userData unsafe.Pointer) (result C.int) {
	// A panic crossing the cgo boundary would crash the broker.
	defer func() {
		if recover() != nil {
			result = C.MOSQ_ERR_AUTH
		}
	}()

	auth := (*C.struct_mosquitto_evt_basic_auth)(eventData)
	username := C.GoString(auth.username)
	password := C.GoString(auth.password)

	// Not a JWT → defer to the static password_file (legacy users).
	if strings.Count(password, ".") != 2 {
		return C.MOSQ_ERR_PLUGIN_DEFER
	}

	mutex.RLock()
	set := jwks
	mutex.RUnlock()
	if set == nil {
		return C.MOSQ_ERR_AUTH // JWKS not loaded yet
	}

	token, err := jwt.Parse(
		password,
		set.Keyfunc,
		jwt.WithValidMethods([]string{"RS256"}),
		jwt.WithExpirationRequired(),
		// Any-of, and the claim becomes required once an expected
		// audience is configured — so a token carrying no aud at all
		// fails here rather than passing. Any-of is what is wanted:
		// Keycloak client_credentials tokens routinely carry "account"
		// alongside the mapper audience, and requiring all of them
		// would refuse every real token.
		jwt.WithAudience(apiAudience),
	)
	if err != nil || !token.Valid {
		return C.MOSQ_ERR_AUTH
	}
	claims, ok := token.Claims.(jwt.MapClaims)
	if !ok {
		return C.MOSQ_ERR_AUTH
	}

	// Bind the credential to the connecting identity: a Keycloak
	// client_credentials token's azp is the device's client_id, which
	// the device presents as its MQTT username.
	if authorizedParty, _ := claims["azp"].(string); authorizedParty != username {
		return C.MOSQ_ERR_AUTH
	}
	// The token must originate from our realm.
	if issuer, _ := claims["iss"].(string); !strings.HasSuffix(issuer, realmPath) {
		return C.MOSQ_ERR_AUTH
	}

	// Spec 330 (#2286): remember whether this identity's token carried the
	// publish scope, so sseOnAclCheck can answer the PUBLISH this CONNECT
	// is about to be followed by. Only a successful authentication reaches
	// here, so a rejected token never overwrites a prior verdict.
	canPublish := authz.HasScope(claims["scope"], authz.PublishScope)
	grants.Record(username, canPublish)
	if !canPublish {
		logf("%s authenticated without %s; its publishes will be refused", username, authz.PublishScope)
	}
	return C.MOSQ_ERR_SUCCESS
}

// sseOnAclCheck answers an MOSQ_EVT_ACL_CHECK. It never grants: the
// strongest verdict it can return is MOSQ_ERR_PLUGIN_DEFER, leaving
// acl_file to decide topic authority (spec finding 2, plan.md §2.1). It
// only ever refuses a write, and only for an identity whose last successful
// JWT authentication lacked sse.events.publish.
//
//export sseOnAclCheck
func sseOnAclCheck(event C.int, eventData unsafe.Pointer, userData unsafe.Pointer) (result C.int) {
	// A panic crossing the cgo boundary would crash the broker; fail
	// closed rather than let an unexpected shape through as a defer.
	defer func() {
		if recover() != nil {
			result = C.MOSQ_ERR_ACL_DENIED
		}
	}()

	ev := (*C.struct_mosquitto_evt_acl_check)(eventData)
	name := C.mosquitto_client_username(ev.client)
	if name == nil {
		// No identity to look up (e.g. during teardown, after a Will has
		// already been handled). Nothing this plugin can refuse or allow.
		return C.MOSQ_ERR_PLUGIN_DEFER
	}

	switch grants.Decide(C.GoString(name), ev.access == C.MOSQ_ACL_WRITE) {
	case authz.Deny:
		return C.MOSQ_ERR_ACL_DENIED
	default:
		return C.MOSQ_ERR_PLUGIN_DEFER
	}
}

func main() {}
