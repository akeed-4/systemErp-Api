namespace ERP.Core.Contracts.Shared;

public class NotFoundException : ErpException
{
    public NotFoundException(string message = "العنصر المطلوب غير موجود") : base(message) { }
    public override int StatusCode => 404;
}
