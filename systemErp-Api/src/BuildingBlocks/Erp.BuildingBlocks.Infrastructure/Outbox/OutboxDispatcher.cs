using System.Text.Json;
using Erp.BuildingBlocks.Infrastructure.Options;
using Erp.BuildingBlocks.Infrastructure.Tenancy;
using Erp.Catalog.Contracts;
using Erp.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Erp.BuildingBlocks.Infrastructure.Outbox;

internal sealed class OutboxWriter(PlatformDbContext db, TimeProvider clock) : IOutbox
{
    public void Enqueue<TMessage>(TMessage message)
        where TMessage : IOutboxMessage =>
        db.OutboxMessages.Add(new OutboxMessage(
            TMessage.MessageType,
            JsonSerializer.Serialize(message, OutboxJson.Options),
            clock.GetUtcNow()));
}

/// <summary>
/// Processes pending outbox rows in the shared database and in every dedicated database. Each message runs in a new
/// scope bound to the message's tenant, so handlers see the same tenant context the writer had.
/// </summary>
public sealed class OutboxDispatcher(
    ITenantScopeFactory scopes,
    ITenantDirectory directory,
    TimeProvider clock,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger)
{
    private readonly OutboxOptions _options = options.Value;

    public async Task<int> DispatchAllAsync(CancellationToken cancellationToken)
    {
        var processed = 0;
        foreach (var database in await directory.ListDatabasesAsync(cancellationToken))
        {
            try
            {
                processed += await DispatchDatabaseAsync(database.ConnectionString, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox dispatch failed for database {Database}", database.Name);
            }
        }

        return processed;
    }

    public async Task<int> DispatchDatabaseAsync(string connectionString, CancellationToken cancellationToken)
    {
        List<OutboxMessage> batch;
        await using (var scope = scopes.CreateForDatabase(connectionString, "outbox scan"))
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            batch = await db.OutboxMessages.AsNoTracking()
                .Where(m => m.ProcessedAt == null && m.Attempts < _options.MaxAttempts)
                .OrderBy(m => m.OccurredAt)
                .Take(_options.BatchSize)
                .ToListAsync(cancellationToken);
        }

        foreach (var message in batch)
        {
            string? error = null;
            try
            {
                await HandleAsync(message, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                error = ex.Message;
                logger.LogError(ex, "Outbox message {MessageId} ({MessageType}) failed for tenant {TenantId}", message.Id, message.Type, message.TenantId);
            }

            await MarkAsync(connectionString, message.Id, error, cancellationToken);
        }

        return batch.Count;
    }

    private async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        await using var scope = await scopes.CreateForTenantAsync(message.TenantId, requireServable: false, cancellationToken);
        var handler = scope.ServiceProvider.GetServices<IOutboxMessageHandler>()
            .SingleOrDefault(h => h.MessageType == message.Type)
            ?? throw new InvalidOperationException($"No outbox handler registered for '{message.Type}'.");

        using (logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = message.TenantId }))
        {
            await handler.HandleAsync(message.Payload, cancellationToken);
        }
    }

    private async Task MarkAsync(string connectionString, Guid messageId, string? error, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateForDatabase(connectionString, "outbox mark");
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var now = clock.GetUtcNow();
        var query = db.OutboxMessages.Where(m => m.Id == messageId);

        if (error is null)
        {
            await query.ExecuteUpdateAsync(
                s => s.SetProperty(m => m.ProcessedAt, now).SetProperty(m => m.Attempts, m => m.Attempts + 1),
                cancellationToken);
        }
        else
        {
            var truncated = error.Length > 2000 ? error[..2000] : error;
            await query.ExecuteUpdateAsync(
                s => s.SetProperty(m => m.Attempts, m => m.Attempts + 1).SetProperty(m => m.LastError, truncated),
                cancellationToken);
        }
    }
}

internal sealed class OutboxBackgroundService(
    OutboxDispatcher dispatcher,
    IOptions<OutboxOptions> options,
    ILogger<OutboxBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await dispatcher.DispatchAllAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox dispatch loop failed");
            }
        }
    }
}
