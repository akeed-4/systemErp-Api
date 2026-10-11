namespace ERP.Core.Contracts.Shared;

/// <summary>أساس أخطاء الأعمال المعروفة - تُترجم إلى استجابة HTTP موحّدة في طبقة الـ API.</summary>
public abstract class ErpException : Exception
{
    protected ErpException(string message) : base(message) { }
    public abstract int StatusCode { get; }
}
