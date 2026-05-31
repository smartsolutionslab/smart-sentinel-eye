// C entry points for the Mosquitto v5 plugin interface (spec 008
// ADR-0100). These carry the exact const-correct signatures the
// mosquitto headers declare — cgo cannot emit `const`, so the symbols
// cannot be exported directly from Go. They forward to the exported Go
// functions in jwt_auth.go. Defining them in a dedicated translation
// unit (rather than the cgo preamble) avoids duplicate-symbol errors.

#include <mosquitto.h>
#include <mosquitto_broker.h>
#include <mosquitto_plugin.h>

#include "_cgo_export.h"

int mosquitto_plugin_version(int supported_version_count, const int *supported_versions)
{
    (void)supported_version_count;
    (void)supported_versions;
    return 5;
}

int mosquitto_plugin_init(mosquitto_plugin_id_t *identifier, void **user_data,
                          struct mosquitto_opt *options, int option_count)
{
    (void)user_data;
    return goPluginInit(identifier, options, option_count);
}

int mosquitto_plugin_cleanup(void *user_data, struct mosquitto_opt *options, int option_count)
{
    (void)user_data;
    (void)options;
    (void)option_count;
    return MOSQ_ERR_SUCCESS;
}

// Registers the Go basic-auth callback; called from goPluginInit.
int sse_register(mosquitto_plugin_id_t *id)
{
    return mosquitto_callback_register(id, MOSQ_EVT_BASIC_AUTH,
                                       (MOSQ_FUNC_generic_callback)sseOnBasicAuth, NULL, NULL);
}
