namespace ERP.Core.Contracts.Shared;

public class UnauthorizedAppException : ErpException
{
    public UnauthorizedAppException(string? message = null) : base(message ?? Messages.InvalidCredentials) { }
    public override int StatusCode => 401;
}
