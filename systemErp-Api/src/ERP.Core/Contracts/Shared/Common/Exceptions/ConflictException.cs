namespace ERP.Core.Contracts.Shared;

public class ConflictException : ErpException
{
    public ConflictException(string message) : base(message) { }
    public override int StatusCode => 409;
}
