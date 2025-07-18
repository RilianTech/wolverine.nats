using System;
using Microsoft.Extensions.Primitives;
using NATS.Client.Core;
using NATS.Client.JetStream;
using Wolverine.Transports;

namespace Wolverine.Nats.Internals;

public class NatsEnvelopeMapper : EnvelopeMapper<NatsMsg<byte[]>, NatsHeaders>
{
    public NatsEnvelopeMapper(NatsEndpoint endpoint)
        : base(endpoint)
    {
        // Additional mappings specific to NATS can be added here if needed
    }

    protected override void writeOutgoingHeader(NatsHeaders headers, string key, string value)
    {
        headers[key] = value;
    }

    protected override bool tryReadIncomingHeader(
        NatsMsg<byte[]> incoming,
        string key,
        out string? value
    )
    {
        value = null;

        if (incoming.Headers == null)
            return false;

        if (incoming.Headers.TryGetValue(key, out var values))
        {
            value = values.ToString();
            return true;
        }

        return false;
    }

    protected override void writeIncomingHeaders(NatsMsg<byte[]> incoming, Envelope envelope)
    {
        // Copy the data payload
        envelope.Data = incoming.Data;

        // Set the destination based on the NATS subject
        envelope.Destination = new Uri($"nats://{incoming.Subject}");

        // Copy any additional headers that aren't reserved
        if (incoming.Headers != null)
        {
            var reservedHeaders = AllHeaders();
            foreach (var header in incoming.Headers)
            {
                if (!reservedHeaders.Contains(header.Key))
                {
                    envelope.Headers[header.Key] = header.Value;
                }
            }
        }
    }
}

// Separate mapper for JetStream messages
public class JetStreamEnvelopeMapper : EnvelopeMapper<NatsJSMsg<byte[]>, NatsHeaders>
{
    public JetStreamEnvelopeMapper(NatsEndpoint endpoint)
        : base(endpoint)
    {
        // Additional mappings specific to JetStream can be added here if needed
    }

    protected override void writeOutgoingHeader(NatsHeaders headers, string key, string value)
    {
        headers[key] = value;
    }

    protected override bool tryReadIncomingHeader(
        NatsJSMsg<byte[]> incoming,
        string key,
        out string? value
    )
    {
        value = null;

        if (incoming.Headers == null)
            return false;

        if (incoming.Headers.TryGetValue(key, out var values))
        {
            value = values.ToString();
            return true;
        }

        return false;
    }

    protected override void writeIncomingHeaders(NatsJSMsg<byte[]> incoming, Envelope envelope)
    {
        // Copy the data payload
        envelope.Data = incoming.Data;

        // Set the destination based on the NATS subject
        envelope.Destination = new Uri($"nats://{incoming.Subject}");

        // Store JetStream metadata in envelope headers for potential use
        if (incoming.Metadata != null)
        {
            var metadata = incoming.Metadata.Value;
            envelope.Headers["nats-stream"] = metadata.Stream;
            envelope.Headers["nats-consumer"] = metadata.Consumer;
            envelope.Headers["nats-delivered"] = metadata.NumDelivered.ToString();
            envelope.Headers["nats-pending"] = metadata.NumPending.ToString();
            envelope.Headers["nats-stream-seq"] = metadata.Sequence.Stream.ToString();
            envelope.Headers["nats-consumer-seq"] = metadata.Sequence.Consumer.ToString();
        }

        // Copy any additional headers that aren't reserved
        if (incoming.Headers != null)
        {
            var reservedHeaders = AllHeaders();
            foreach (var header in incoming.Headers)
            {
                if (!reservedHeaders.Contains(header.Key))
                {
                    envelope.Headers[header.Key] = header.Value;
                }
            }
        }
    }
}
