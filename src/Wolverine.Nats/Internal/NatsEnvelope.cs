using NATS.Client.Core;
using NATS.Client.JetStream;
using Wolverine.Runtime;

namespace Wolverine.Nats.Internal;

public class NatsEnvelope : Envelope
{
    public NatsEnvelope(NatsMsg<byte[]>? coreMsg, NatsJSMsg<byte[]>? jetStreamMsg)
    {
        CoreMsg = coreMsg;
        JetStreamMsg = jetStreamMsg;
    }

    public NatsMsg<byte[]>? CoreMsg { get; }
    public NatsJSMsg<byte[]>? JetStreamMsg { get; }
}
