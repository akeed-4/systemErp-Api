namespace ERP.Core.Contracts.Shared;

public class ForbiddenException : ErpException
{
    public ForbiddenException(string? message = null) : base(message ?? Messages.Forbidden) { }
    public override int StatusCode => 403;
}
