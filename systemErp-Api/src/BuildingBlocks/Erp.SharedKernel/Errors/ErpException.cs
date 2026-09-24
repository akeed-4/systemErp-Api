namespace Erp.SharedKernel.Errors;

/// <summary>
/// A business/application error with a stable code and bilingual messages.
/// Rendered by the web layer as ApiResponse + RFC 7807 problem.
/// </summary>
public class ErpException : Exception
{
    public ErpException(
        string code,
        int status,
        string messageEn,
        string messageAr,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null)
        : base(messageEn)
    {
        Code = code;
        Status = status;
        MessageAr = messageAr;
        FieldErrors = fieldErrors;
    }

    public string Code { get; }

    public int Status { get; }

    public string MessageAr { get; }

    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; }

    public static ErpException NotFound(string entityEn, string entityAr) =>
        new("not_found", 404, $"{entityEn} was not found.", $"{entityAr} غير موجود.");

    public static ErpException Validation(string messageEn, string messageAr, IReadOnlyDictionary<string, string[]>? fieldErrors = null) =>
        new("validation_failed", 400, messageEn, messageAr, fieldErrors);

    public static ErpException Conflict(string code, string messageEn, string messageAr) =>
        new(code, 409, messageEn, messageAr);

    public static ErpException Forbidden(string code, string messageEn, string messageAr) =>
        new(code, 403, messageEn, messageAr);

    public static ErpException Unauthorized(string code, string messageEn, string messageAr) =>
        new(code, 401, messageEn, messageAr);
}
