using System.Text.RegularExpressions;

namespace ERP.Service.Services.Shared;

/// <summary>
/// فحص استيراد العملاء والموردين قبل الحفظ: صيغة الحقول، توحيد الهاتف، والتكرار مع المسجّل وداخل الملف.
/// التكرار بالكود أو الرقم الضريبي أو السجل التجاري خطأ يمنع الصف؛ والتكرار بالهاتف أو الاسم تنبيه فقط
/// (قد يكون فرعين لجهة واحدة). لا يُحدَّث طرف موجود أبداً: المطابق يُبلَّغ عنه فقط.
/// </summary>
internal static class PartyImport
{
    /// <summary>بيانات الطرف التي يُفحص بها التكرار.</summary>
    public sealed record PartyRecord(string Code, string NameAr, string? Vat, string? Cr, string? Phone);

    private static readonly Regex Mobile = new(@"^05\d{8}$", RegexOptions.Compiled);
    private static readonly Regex Landline = new(@"^01[1-7]\d{7}$", RegexOptions.Compiled);
    private static readonly Regex Unified = new(@"^(9200\d{5}|800\d{7})$", RegexOptions.Compiled);
    private static readonly Regex IbanPattern = new(@"^[A-Z]{2}\d{2}[A-Z0-9]{10,30}$", RegexOptions.Compiled);

    /// <summary>
    /// يوحّد الهاتف: الجوال السعودي بأي صيغة شائعة (05… / 5… / +9665… / 009665… / 9665…) يُحفظ 05XXXXXXXX، والثابت 01XXXXXXXX،
    /// والموحّد 9200XXXXX و800XXXXXXX؛ والدولي غير السعودي يُقبل فقط بادئاً بـ + (أو 00) ويُحفظ +أرقام. الأرقام العربية تُحوَّل.
    /// </summary>
    public static string? NormalizePhone(string? raw, out bool valid)
    {
        valid = true;
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = new string(raw.Trim().Select(c => c switch
        {
            >= '٠' and <= '٩' => (char)('0' + c - '٠'),
            >= '۰' and <= '۹' => (char)('0' + c - '۰'),
            _ => c,
        }).ToArray());
        if (s.Any(c => !char.IsAsciiDigit(c) && c is not (' ' or '-' or '(' or ')' or '.' or '+')) || s.LastIndexOf('+') > 0)
        {
            valid = false;
            return raw.Trim();
        }
        var digits = new string(s.Where(char.IsAsciiDigit).ToArray());
        var international = s.StartsWith('+');
        if (!international && digits.StartsWith("00")) { international = true; digits = digits[2..]; }
        if (digits.StartsWith("966") && (international || digits.Length == 12)) digits = "0" + digits[3..];
        else if (international)
        {
            valid = digits.Length is >= 8 and <= 15;
            return "+" + digits;
        }
        if (digits.Length == 9 && digits[0] is '5' or '1') digits = "0" + digits;
        valid = Mobile.IsMatch(digits) || Landline.IsMatch(digits) || Unified.IsMatch(digits);
        return valid ? digits : raw.Trim();
    }

    /// <summary>الآيبان بلا مسافات وبحروف كبيرة؛ صيغته والطول السعودي (24) وخانتا التحقق (mod 97).</summary>
    public static string NormalizeIban(string iban) => new string(iban.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

    public static bool IsValidIban(string iban)
    {
        if (!IbanPattern.IsMatch(iban) || (iban.StartsWith("SA") && iban.Length != 24)) return false;
        var rearranged = iban[4..] + iban[..4];
        var remainder = 0;
        foreach (var c in rearranged)
        {
            var value = char.IsAsciiDigit(c) ? c - '0' : c - 'A' + 10;
            remainder = value >= 10 ? (remainder * 100 + value) % 97 : (remainder * 10 + value) % 97;
        }
        return remainder == 1;
    }

    /// <summary>مفتاح مقارنة الأسماء: مسافات موحّدة، وحروف كبيرة/صغيرة، والهمزات والتاء المربوطة والألف المقصورة.</summary>
    private static string NameKey(string name)
        => Regex.Replace(name.Trim(), @"\s+", " ").ToLowerInvariant()
            .Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ؤ', 'و').Replace('ئ', 'ي').Replace('ى', 'ي').Replace('ة', 'ه');

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Dictionary<string, string> Index(IEnumerable<(string? Key, string Name)> pairs)
        => pairs.Where(p => !string.IsNullOrWhiteSpace(p.Key)).GroupBy(p => p.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

    /// <param name="read">حقول التكرار من صف بعد تنظيفه.</param>
    /// <param name="writePhone">يكتب الهاتف الموحَّد في الصف فيُحفظ بصيغته.</param>
    /// <param name="formatErrors">أخطاء صيغة حقول الصف (الحقول الإلزامية، الضريبي، العنوان، الهوية...).</param>
    public static List<PartyImportRowDto> Check<T>(IReadOnlyList<PartyImportRowInput<T>> rows, IReadOnlyList<PartyRecord> existing,
        Func<T, PartyRecord> read, Action<T, string?> writePhone, Func<T, List<string>> formatErrors, bool phoneRequired)
    {
        var codes = Index(existing.Select(e => ((string?)e.Code, e.NameAr)));
        var vats = Index(existing.Select(e => (Clean(e.Vat), e.NameAr)));
        var crs = Index(existing.Select(e => (Clean(e.Cr), e.NameAr)));
        var phones = Index(existing.Select(e => (NormalizePhone(e.Phone, out var ok) is { } p && ok ? p : null, e.NameAr)));
        var names = Index(existing.Select(e => ((string?)NameKey(e.NameAr), e.NameAr)));
        // ما سبق في الملف: المفتاح ← رقم الصف (من الصفوف المقبولة فقط، فصف مرفوض لا يحجب تاليه)
        var fileCodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var fileVats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var fileCrs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var filePhones = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var fileNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var results = new List<PartyImportRowDto>();
        foreach (var row in rows)
        {
            var f = read(row.Data);
            var result = new PartyImportRowDto { RowNumber = row.RowNumber, Code = Clean(f.Code), NameAr = f.NameAr };
            var errors = formatErrors(row.Data);

            var phone = NormalizePhone(f.Phone, out var phoneOk);
            if (phone == null) { if (phoneRequired) errors.Add(Messages.ImportPhoneRequired); }
            else if (!phoneOk) errors.Add(string.Format(Messages.ImportPhoneInvalid, phone));
            else writePhone(row.Data, phone);
            result.Phone = phone;

            void Unique(string? key, Dictionary<string, string> registered, Dictionary<string, int> inFile, string existingMessage, string fileMessage)
            {
                if (key == null) return;
                if (registered.TryGetValue(key, out var owner)) errors.Add(string.Format(existingMessage, key, owner));
                else if (inFile.TryGetValue(key, out var first)) errors.Add(string.Format(fileMessage, key, first));
            }
            Unique(result.Code, codes, fileCodes, Messages.ImportCodeExists, Messages.ImportCodeRepeatedInFile);
            Unique(Clean(f.Vat), vats, fileVats, Messages.ImportVatExists, Messages.ImportVatRepeatedInFile);
            Unique(Clean(f.Cr), crs, fileCrs, Messages.ImportCrExists, Messages.ImportCrRepeatedInFile);

            var nameKey = string.IsNullOrWhiteSpace(f.NameAr) ? null : NameKey(f.NameAr);
            if (phone != null && phoneOk)
            {
                if (phones.TryGetValue(phone, out var owner)) result.Warnings.Add(string.Format(Messages.ImportPhoneExists, phone, owner));
                else if (filePhones.TryGetValue(phone, out var first)) result.Warnings.Add(string.Format(Messages.ImportPhoneRepeatedInFile, phone, first));
            }
            if (nameKey != null)
            {
                if (names.TryGetValue(nameKey, out var owner)) result.Warnings.Add(string.Format(Messages.ImportNameExists, owner));
                else if (fileNames.TryGetValue(nameKey, out var first)) result.Warnings.Add(string.Format(Messages.ImportNameRepeatedInFile, f.NameAr, first));
            }

            result.Errors = errors.Distinct().ToList();
            result.Status = result.Errors.Count > 0 ? "error" : result.Warnings.Count > 0 ? "warning" : "valid";
            if (result.Errors.Count == 0)
            {
                if (result.Code != null) fileCodes.TryAdd(result.Code, row.RowNumber);
                if (Clean(f.Vat) is { } vat) fileVats.TryAdd(vat, row.RowNumber);
                if (Clean(f.Cr) is { } cr) fileCrs.TryAdd(cr, row.RowNumber);
                if (phone != null) filePhones.TryAdd(phone, row.RowNumber);
                if (nameKey != null) fileNames.TryAdd(nameKey, row.RowNumber);
            }
            results.Add(result);
        }
        return results;
    }

    public static PartyImportPreviewDto Summarize(List<PartyImportRowDto> rows) => new()
    {
        TotalRows = rows.Count, Rows = rows,
        ValidCount = rows.Count(r => r.Status == "valid"), WarningCount = rows.Count(r => r.Status == "warning"), ErrorCount = rows.Count(r => r.Status == "error"),
    };

    /// <summary>أخطاء صيغة مشتركة بين العميل والمورد (الاسم، الضريبي، البريد، العنوان الوطني، الهوية).</summary>
    public static List<string> CommonErrors(string nameAr, string? vat, string? email, decimal opening, string? country, string? buildingNo, string? postalCode,
        string? additionalNo, string? idType, string? idNumber)
    {
        var errors = new List<string>();
        PartyValidation.AddAddressErrors(errors, country, buildingNo, postalCode, additionalNo);
        PartyValidation.AddIdentificationErrors(errors, idType, idNumber);
        try { PartyValidation.Validate(nameAr, vat, email, opening, new List<string>(), country); }
        catch (ValidationFailedException ex) { errors.AddRange(ex.Errors); }
        return errors;
    }

    /// <summary>يحفظ الصفوف المقبولة صفاً صفاً (كل صف في معاملته) ويرفض صفوف الأخطاء بأسبابها.</summary>
    public static async Task<ImportResultDto> SaveAsync<T, TDto>(IReadOnlyList<PartyImportRowInput<T>> rows, IReadOnlyList<PartyImportRowDto> checks,
        Func<T, CancellationToken, Task<TDto>> create, Func<TDto, Guid> idOf, string label, CancellationToken ct)
    {
        var result = new ImportResultDto { TotalRows = rows.Count };
        for (var i = 0; i < rows.Count; i++)
        {
            var rowNumber = rows[i].RowNumber;
            if (checks[i].Status == "error")
            {
                result.Results.Add(new ImportRowResult { RowNumber = rowNumber, Success = false, Error = string.Join(" | ", checks[i].Errors) });
                result.FailedCount++;
                continue;
            }
            try
            {
                var created = await create(rows[i].Data, ct);
                result.Results.Add(new ImportRowResult { RowNumber = rowNumber, Success = true, Id = idOf(created) });
                result.SuccessCount++;
            }
            catch (Exception ex) when (ex is ValidationFailedException or ConflictException or NotFoundException)
            {
                result.Results.Add(new ImportRowResult { RowNumber = rowNumber, Success = false, Error = ex.Message });
                result.FailedCount++;
            }
        }
        return result;
    }
}
