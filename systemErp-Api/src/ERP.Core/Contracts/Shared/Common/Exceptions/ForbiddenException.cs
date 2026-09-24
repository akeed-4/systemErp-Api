namespace ERP.Core.Contracts.Shared;

public class ForbiddenException : ErpException
{
    public ForbiddenException(string message = "ليس لديك صلاحية لتنفيذ هذه العملية") : base(message) { }
    public override int StatusCode => 403;
}
