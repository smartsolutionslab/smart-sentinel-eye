using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Ingress;

/// <summary>
/// Builds a managed MQTT client wired up with the broker endpoint +
/// credentials from <see cref="MosquittoOptions"/>. Persistent
/// session (cleanSession=false) so a process restart resumes any
/// QoS 1 messages the broker still holds.
/// </summary>
public sealed class MosquittoConnectionFactory(IOptions<MosquittoOptions> options)
{
    public IManagedMqttClient Create()
    {
        MosquittoOptions opts = options.Value;

        MqttClientOptionsBuilder clientOptions = new MqttClientOptionsBuilder()
            .WithClientId(opts.ClientId)
            .WithTcpServer(opts.Host, opts.Port)
            .WithCleanSession(false)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));

        if (opts.UseTls)
        {
            clientOptions = clientOptions.WithTlsOptions(builder => builder.UseTls());
        }

        if (!string.IsNullOrEmpty(opts.Username))
        {
            clientOptions = clientOptions.WithCredentials(opts.Username, opts.Password);
        }

        ManagedMqttClientOptions managed = new ManagedMqttClientOptionsBuilder()
            .WithClientOptions(clientOptions.Build())
            .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
            .Build();

        IManagedMqttClient client = new MqttFactory().CreateManagedMqttClient();
        // The hosted service calls StartAsync on Initialize so the
        // managed options reach the wire; we return the unstarted
        // client + the options so the caller can subscribe + start
        // atomically.
        _ = client.StartAsync(managed);
        return client;
    }
}
