using System.Text.Json;

namespace Erp.SharedKernel.Messaging;

/// <summary>A message written in the same transaction as the business change and processed after commit.</summary>
public interface IOutboxMessage
{
    static abstract string MessageType { get; }
}

/// <summary>Enqueues a message into the current tenant database's outbox. It is saved by the unit of work.</summary>
public interface IOutbox
{
    void Enqueue<TMessage>(TMessage message)
        where TMessage : IOutboxMessage;
}

/// <summary>Processes one message type. Runs inside a scope whose tenant context was restored from the message.</summary>
public interface IOutboxMessageHandler
{
    string MessageType { get; }

    Task HandleAsync(string payload, CancellationToken cancellationToken);
}

public abstract class OutboxMessageHandler<TMessage> : IOutboxMessageHandler
    where TMessage : IOutboxMessage
{
    public string MessageType => TMessage.MessageType;

    public Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Deserialize<TMessage>(payload, OutboxJson.Options)
            ?? throw new InvalidOperationException($"Outbox payload for {MessageType} is empty.");
        return HandleAsync(message, cancellationToken);
    }

    protected abstract Task HandleAsync(TMessage message, CancellationToken cancellationToken);
}

public static class OutboxJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
