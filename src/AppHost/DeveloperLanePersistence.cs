using Aspire.Hosting.Azure;

namespace SmartSentinelEye.AppHost;

// Developer lane only: PersistentStack=false (the e2e CI job) and the integration
// fixture must not pin a container's lifetime, mount a data volume, or add
// pgAdmin. Each method does nothing unless the caller passes the developer-lane
// flag, so a call site reads the same in every lane.
internal static class DeveloperLanePersistence
{
    // Persistent means this container, once created, keeps running the args it was
    // created with — Docker does not re-read WithArgs (e.g. the max_connections set
    // in AppHost.cs) on a mere restart. Changing them needs `docker rm` of the
    // postgres container (the data volume can stay) before the next `aspire run`, or
    // the old value keeps running.
    internal static void WithPostgresDeveloperLanePersistence(
        this IResourceBuilder<PostgresServerResource> postgres,
        bool isPersistentDevStack)
    {
        if (isPersistentDevStack)
        {
            postgres
                .WithLifetime(ContainerLifetime.Persistent)
                .WithDataVolume()
                .WithPgAdmin();
        }
    }

    internal static void WithRabbitMqDeveloperLanePersistence(
        this IResourceBuilder<RabbitMQServerResource> rabbitmq,
        bool isPersistentDevStack)
    {
        if (isPersistentDevStack)
        {
            rabbitmq
                .WithLifetime(ContainerLifetime.Persistent)
                .WithDataVolume();
        }
    }

    // A persistent keycloak-data volume also keeps the retired
    // `stream-distribution-attribution` client enabled (spec 423): the realm import skips an
    // existing realm, so removing it from the realm file never reaches such a volume. Its
    // secret is dev-only and public in git history, and the client sits in both fab groups
    // with sse.cameras.read. Remedy: delete the client in the Keycloak admin console, or
    // `docker rm` the keycloak container and `docker volume rm` its data volume.
    //
    // Spec 455: `allowRemoteResourceManagement:false` on the admin-permissions client reaches only a
    // FRESH keycloak-data volume. An existing persistent dev stack keeps `true` (the protection API's
    // resource_set stays readable with a rotated admin-permissions secret) until the volume is dropped.
    //
    // Persistent means the keycloak-data volume survives AppHost restarts, so an edit to
    // either Realms/*.json file is NOT re-imported — WithRealmImport only runs
    // against a fresh volume. Drop the volume (`docker volume rm`, or its Aspire/Docker
    // Desktop equivalent) after a realm edit, or the stack looks healthy while still
    // running the old realm.
    internal static void WithKeycloakDeveloperLanePersistence(
        this IResourceBuilder<KeycloakResource> keycloak,
        bool isPersistentDevStack)
    {
        if (isPersistentDevStack)
        {
            keycloak
                .WithLifetime(ContainerLifetime.Persistent)
                .WithDataVolume();
        }
    }

    // Lifetime only, no data volume: used for mediamtx, as its own call separate from
    // its ICE port map (which the e2e lane still needs), and for camera-sim.
    internal static void WithContainerDeveloperLaneLifetime(
        this IResourceBuilder<ContainerResource> container,
        bool isPersistentDevStack)
    {
        if (isPersistentDevStack)
        {
            container.WithLifetime(ContainerLifetime.Persistent);
        }
    }

    internal static void WithMosquittoDeveloperLanePersistence(
        this IResourceBuilder<ContainerResource> mosquitto,
        bool isPersistentDevStack)
    {
        if (isPersistentDevStack)
        {
            mosquitto
                .WithLifetime(ContainerLifetime.Persistent)
                .WithVolume("mosquitto-data", "/mosquitto/data");
        }
    }

    internal static void WithAzuriteDeveloperLanePersistence(
        this IResourceBuilder<AzureStorageEmulatorResource> azurite,
        bool isPersistentDevStack)
    {
        if (isPersistentDevStack)
        {
            azurite
                .WithLifetime(ContainerLifetime.Persistent)
                .WithDataVolume();
        }
    }
}
