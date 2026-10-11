using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>ينفّذ عملاً من عدة خطوات داخل معاملة قاعدة بيانات واحدة (كل شيء أو لا شيء).</summary>
public interface ITransactionRunner
{
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default);
    Task RunAsync(Func<CancellationToken, Task> work, CancellationToken ct = default);
}
