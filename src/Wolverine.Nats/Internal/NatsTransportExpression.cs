using Wolverine.Configuration;
using Wolverine.Nats.Configuration;
using Wolverine.Transports;

namespace Wolverine.Nats.Internal;

public class NatsTransportExpression
    : BrokerExpression<
        NatsTransport,
        NatsEndpoint,
        NatsEndpoint,
        NatsListenerConfiguration,
        NatsSubscriberConfiguration,
        NatsTransportExpression
    >
{
    public NatsTransportExpression(NatsTransport transport, WolverineOptions options)
        : base(transport, options) { }

    /// <summary>
    ///     Configure JetStream options
    /// </summary>
    public NatsTransportExpression UseJetStream(Action<JetStreamDefaults> configure)
    {
        configure(Transport.Configuration.JetStreamDefaults);
        Transport.Configuration.EnableJetStream = true;
        return this;
    }

    /// <summary>
    ///     Set the identifier prefix for all NATS subjects
    /// </summary>
    public NatsTransportExpression WithSubjectPrefix(string prefix)
    {
        Transport.IdentifierPrefix = prefix;
        return this;
    }

    /// <summary>
    ///     Configure TLS settings
    /// </summary>
    public NatsTransportExpression UseTls(bool insecureSkipVerify = false)
    {
        Transport.Configuration.EnableTls = true;
        Transport.Configuration.TlsInsecure = insecureSkipVerify;
        return this;
    }

    /// <summary>
    ///     Configure authentication
    /// </summary>
    public NatsTransportExpression WithCredentials(string username, string password)
    {
        Transport.Configuration.Username = username;
        Transport.Configuration.Password = password;
        return this;
    }

    /// <summary>
    ///     Configure token authentication
    /// </summary>
    public NatsTransportExpression WithToken(string token)
    {
        Transport.Configuration.Token = token;
        return this;
    }

    /// <summary>
    ///     Configure NKey authentication
    /// </summary>
    public NatsTransportExpression WithNKey(string nkeyFile)
    {
        Transport.Configuration.NKeyFile = nkeyFile;
        return this;
    }

    /// <summary>
    ///     Set the JetStream domain
    /// </summary>
    public NatsTransportExpression UseJetStreamDomain(string domain)
    {
        Transport.Configuration.JetStreamDomain = domain;
        return this;
    }

    /// <summary>
    ///     Configure connection timeouts
    /// </summary>
    public NatsTransportExpression ConfigureTimeouts(
        TimeSpan connectTimeout,
        TimeSpan requestTimeout
    )
    {
        Transport.Configuration.ConnectTimeout = connectTimeout;
        Transport.Configuration.RequestTimeout = requestTimeout;
        return this;
    }

    protected override NatsListenerConfiguration createListenerExpression(
        NatsEndpoint listenerEndpoint
    )
    {
        return new NatsListenerConfiguration(listenerEndpoint);
    }

    protected override NatsSubscriberConfiguration createSubscriberExpression(
        NatsEndpoint subscriberEndpoint
    )
    {
        return new NatsSubscriberConfiguration(subscriberEndpoint);
    }
}
