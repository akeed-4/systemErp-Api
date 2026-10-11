namespace ERP.Core.Contracts.Shared;

public class NotFoundException : ErpException
{
    public NotFoundException(string? message = null) : base(message ?? Messages.ItemNotFound) { }
    public override int StatusCode => 404;
}
