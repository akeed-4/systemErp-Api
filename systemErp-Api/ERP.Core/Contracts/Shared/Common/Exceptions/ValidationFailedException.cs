namespace ERP.Core.Contracts.Shared;

public class ValidationFailedException : ErpException
{
    public IReadOnlyList<string> Errors { get; }
    public ValidationFailedException(string message, IEnumerable<string>? errors = null) : base(message)
        => Errors = (errors ?? new[] { message }).ToList();
    public override int StatusCode => 400;
}
