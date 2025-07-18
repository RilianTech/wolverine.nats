using System;
using System.Reflection;
using NATS.Client.Core;
using NATS.Client.JetStream;
using Wolverine;
using Wolverine.Runtime;

namespace Wolverine.Nats.Internals;

internal static class EnvelopeMapper
{
    private static readonly PropertyInfo IdProperty = typeof(Envelope).GetProperty("Id")!;
    private static readonly PropertyInfo DataProperty = typeof(Envelope).GetProperty("Data")!;
    private static readonly PropertyInfo SourceProperty = typeof(Envelope).GetProperty("Source")!;
    private static readonly PropertyInfo ReplyUriProperty = typeof(Envelope).GetProperty("ReplyUri")!;
    private static readonly PropertyInfo ContentTypeProperty = typeof(Envelope).GetProperty("ContentType")!;
    private static readonly PropertyInfo ConversationIdProperty = typeof(Envelope).GetProperty("ConversationId")!;
    private static readonly PropertyInfo CorrelationIdProperty = typeof(Envelope).GetProperty("CorrelationId")!;
    private static readonly PropertyInfo ParentIdProperty = typeof(Envelope).GetProperty("ParentId")!;
    private static readonly PropertyInfo SentAtProperty = typeof(Envelope).GetProperty("SentAt")!;
    private static readonly PropertyInfo TenantIdProperty = typeof(Envelope).GetProperty("TenantId")!;

    public static Envelope ToEnvelope(NatsMsg<byte[]> natsMsg, IWolverineRuntime runtime)
    {
        var envelope = Envelope.ForPing(new Uri($"nats://{runtime.Options.ServiceName}"));
        
        DataProperty.SetValue(envelope, natsMsg.Data);
        SourceProperty.SetValue(envelope, runtime.Options.ServiceName);
        
        if (natsMsg.Headers != null)
        {
            if (natsMsg.Headers.TryGetValue("MessageId", out var messageId) && Guid.TryParse(messageId, out var id))
                IdProperty.SetValue(envelope, id);
                
            if (natsMsg.Headers.TryGetValue("ConversationId", out var conversationId) && Guid.TryParse(conversationId, out var convId))
                ConversationIdProperty.SetValue(envelope, convId);
                
            if (natsMsg.Headers.TryGetValue("CorrelationId", out var correlationId))
                CorrelationIdProperty.SetValue(envelope, correlationId);
                
            if (natsMsg.Headers.TryGetValue("ParentId", out var parentId))
                ParentIdProperty.SetValue(envelope, parentId);
                
            if (natsMsg.Headers.TryGetValue("ContentType", out var contentType))
                ContentTypeProperty.SetValue(envelope, contentType);
                
            if (natsMsg.Headers.TryGetValue("ReplyTo", out var replyTo))
                ReplyUriProperty.SetValue(envelope, new Uri($"nats://{replyTo}"));
                
            if (natsMsg.Headers.TryGetValue("SentAt", out var sentAt) && DateTimeOffset.TryParse(sentAt, out var sentAtDate))
                SentAtProperty.SetValue(envelope, sentAtDate);
                
            if (natsMsg.Headers.TryGetValue("TenantId", out var tenantId))
                TenantIdProperty.SetValue(envelope, tenantId);
                
            // Copy any additional headers
            foreach (var header in natsMsg.Headers)
            {
                if (!IsReservedHeader(header.Key))
                {
                    envelope.Headers[header.Key] = header.Value;
                }
            }
        }
        
        envelope.Destination = new Uri($"nats://{natsMsg.Subject}");
        
        return envelope;
    }

    public static Envelope ToEnvelope(NatsJSMsg<byte[]> natsJsMsg, IWolverineRuntime runtime)
    {
        // Create a new envelope from scratch for JetStream messages
        var envelope = Envelope.ForPing(new Uri($"nats://{runtime.Options.ServiceName}"));
        
        DataProperty.SetValue(envelope, natsJsMsg.Data);
        SourceProperty.SetValue(envelope, runtime.Options.ServiceName);
        
        if (natsJsMsg.Headers != null)
        {
            if (natsJsMsg.Headers.TryGetValue("MessageId", out var messageId) && Guid.TryParse(messageId, out var id))
                IdProperty.SetValue(envelope, id);
                
            if (natsJsMsg.Headers.TryGetValue("ConversationId", out var conversationId) && Guid.TryParse(conversationId, out var convId))
                ConversationIdProperty.SetValue(envelope, convId);
                
            if (natsJsMsg.Headers.TryGetValue("CorrelationId", out var correlationId))
                CorrelationIdProperty.SetValue(envelope, correlationId);
                
            if (natsJsMsg.Headers.TryGetValue("ParentId", out var parentId))
                ParentIdProperty.SetValue(envelope, parentId);
                
            if (natsJsMsg.Headers.TryGetValue("ContentType", out var contentType))
                ContentTypeProperty.SetValue(envelope, contentType);
                
            if (natsJsMsg.Headers.TryGetValue("ReplyTo", out var replyTo))
                ReplyUriProperty.SetValue(envelope, new Uri($"nats://{replyTo}"));
                
            if (natsJsMsg.Headers.TryGetValue("SentAt", out var sentAt) && DateTimeOffset.TryParse(sentAt, out var sentAtDate))
                SentAtProperty.SetValue(envelope, sentAtDate);
                
            if (natsJsMsg.Headers.TryGetValue("TenantId", out var tenantId))
                TenantIdProperty.SetValue(envelope, tenantId);
                
            // Copy any additional headers
            foreach (var header in natsJsMsg.Headers)
            {
                if (!IsReservedHeader(header.Key))
                {
                    envelope.Headers[header.Key] = header.Value;
                }
            }
        }
        
        envelope.Destination = new Uri($"nats://{natsJsMsg.Subject}");
        
        return envelope;
    }

    public static NatsHeaders ToNatsHeaders(Envelope envelope)
    {
        var headers = new NatsHeaders
        {
            ["MessageId"] = envelope.Id.ToString(),
            ["ContentType"] = envelope.ContentType ?? "application/json",
            ["SentAt"] = envelope.SentAt.ToString("O")
        };

        if (envelope.ConversationId != Guid.Empty)
            headers["ConversationId"] = envelope.ConversationId.ToString();
            
        if (!string.IsNullOrEmpty(envelope.CorrelationId))
            headers["CorrelationId"] = envelope.CorrelationId;
            
        if (!string.IsNullOrEmpty(envelope.ParentId))
            headers["ParentId"] = envelope.ParentId;
            
        if (envelope.ReplyUri != null)
            headers["ReplyTo"] = envelope.ReplyUri.Host;
            
        if (!string.IsNullOrEmpty(envelope.TenantId))
            headers["TenantId"] = envelope.TenantId;
            
        if (!string.IsNullOrEmpty(envelope.Source))
            headers["Source"] = envelope.Source;

        // Copy any additional headers
        foreach (var header in envelope.Headers)
        {
            headers[header.Key] = header.Value;
        }

        return headers;
    }

    private static bool IsReservedHeader(string headerName)
    {
        return headerName switch
        {
            "MessageId" => true,
            "ConversationId" => true,
            "CorrelationId" => true,
            "ParentId" => true,
            "ContentType" => true,
            "ReplyTo" => true,
            "SentAt" => true,
            "TenantId" => true,
            "Source" => true,
            _ => false
        };
    }
}