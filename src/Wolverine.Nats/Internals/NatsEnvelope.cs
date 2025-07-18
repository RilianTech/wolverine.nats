using NATS.Client.Core;
using NATS.Client.JetStream;
using Wolverine.Runtime;

namespace Wolverine.Nats.Internals;

public class NatsEnvelope : Envelope
{
    public NatsEnvelope(Envelope inner, NatsMsg<byte[]>? coreMsg, NatsJSMsg<byte[]>? jetStreamMsg)
        : base(inner.Data ?? Array.Empty<byte>())
    {
        // Copy properties that are settable
        Id = inner.Id;
        CorrelationId = inner.CorrelationId;
        MessageType = inner.MessageType;
        ContentType = inner.ContentType;
        Destination = inner.Destination;
        DeliverBy = inner.DeliverBy;
        Message = inner.Message;
        TenantId = inner.TenantId;
        Attempts = inner.Attempts;

        // Copy headers
        foreach (var header in inner.Headers)
        {
            Headers[header.Key] = header.Value;
        }

        CoreMsg = coreMsg;
        JetStreamMsg = jetStreamMsg;
    }

    public NatsMsg<byte[]>? CoreMsg { get; }
    public NatsJSMsg<byte[]>? JetStreamMsg { get; }
}
