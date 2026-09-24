namespace ERP.Core.Contracts.Shared;

public class UnauthorizedAppException : ErpException
{
    public UnauthorizedAppException(string message = "بيانات الدخول غير صحيحة") : base(message) { }
    public override int StatusCode => 401;
}
