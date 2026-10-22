using System.Globalization;

namespace ERP.Service.Services.Accounting;

/// <summary>درجة الحالة: Block تمنع الإرسال أو تعني رفض الفاتورة، Warning مقبولة مع ملاحظة، Retry خطأ مؤقت يُعاد.</summary>
public enum ZatcaIssueKind { Block, Warning, Retry }

/// <summary>
/// ماذا يحدث للسلسلة بعد هذه الحالة:
/// Keep = لم تُستهلك (لم تصل للهيئة)، أعد الإرسال بنفس ICV/PIH/UUID.
/// Consumed = وصلت ورُفضت أو قُبلت؛ الفاتورة التالية تأخذ ICV جديداً وPIH = بصمة هذه.
/// </summary>
public enum ZatcaChainEffect { Keep, Consumed }

/// <summary>
/// حالة بالعربية والإنجليزية؛ <see cref="Title"/> و<see cref="Fix"/> بلغة الطلب الحالية (Accept-Language عبر
/// RequestLocalizationMiddleware): الإنجليزية صراحةً فقط، وإلا العربية — كبقية رسائل النظام.
/// </summary>
public sealed record ZatcaIssue(string TitleAr, string FixAr, string TitleEn, string FixEn, ZatcaIssueKind Kind, ZatcaChainEffect Chain)
{
    private static bool English => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase);
    public string Title => English ? TitleEn : TitleAr;
    public string Fix => English ? FixEn : FixAr;
}

/// <summary>
/// قاموس كل الحالات التي تمنع إرسال الفاتورة أو تؤدي لرفضها، بثلاث طبقات:
/// 1) فحوص محلية قبل الإرسال (لا تصل للهيئة أصلاً).
/// 2) رموز HTTP من بوابة فاتورة.
/// 3) رموز رسائل التحقق داخل validationResults (تطابق تام أولاً، ثم البادئة).
/// </summary>
public static class ZatcaErrorDictionary
{
    // ───────────── 1) فحوص محلية: تُنفّذ قبل استدعاء الهيئة ─────────────
    public static readonly IReadOnlyDictionary<string, ZatcaIssue> PreChecks = new Dictionary<string, ZatcaIssue>
    {
        ["NoActiveUnit"]          = Local("المستخدم غير مربوط بالهيئة", "اربط المستخدم من تبويب الربط برمز OTP جديد",
                                          "The user is not linked to ZATCA", "Link the user from the onboarding tab with a new OTP"),
        ["UnitDisabled"]          = Local("وحدة المستخدم معطّلة", "أعد ربط المستخدم أو استخدم مستخدماً مربوطاً",
                                          "The user's unit is disabled", "Re-link the user or use a linked user"),
        ["UnitEnvMismatch"]       = Local("الوحدة مربوطة على بيئة غير البيئة الحالية", "عطّل الوحدة وأعد الربط على البيئة المختارة",
                                          "The unit is linked to a different environment", "Disable the unit and re-link it on the selected environment"),
        ["CertificateExpired"]    = Local("شهادة الوحدة منتهية", "جدّد الشهادة برمز OTP من بوابة فاتورة",
                                          "The unit certificate has expired", "Renew the certificate with an OTP from the Fatoora portal"),
        ["SellerVatInvalid"]      = Local("الرقم الضريبي للمنشأة غير صحيح", "15 رقماً يبدأ وينتهي بـ 3 (إعدادات المنشأة)",
                                          "The company VAT number is invalid", "15 digits starting and ending with 3 (company settings)"),
        ["SellerAddressMissing"]  = Local("العنوان الوطني للمنشأة ناقص", "أكمل الشارع ورقم المبنى (4 أرقام) والحي والمدينة والرمز البريدي (5 أرقام)",
                                          "The company national address is incomplete", "Complete the street, building number (4 digits), district, city and postal code (5 digits)"),
        ["SellerCrMissing"]       = Local("السجل التجاري أو معرّف البائع مفقود", "أدخل السجل التجاري في إعدادات المنشأة",
                                          "The commercial registration or seller ID is missing", "Enter the commercial registration in company settings"),
        ["BuyerVatInvalid"]       = Local("الرقم الضريبي للعميل غير صحيح", "صحّح رقم العميل أو اجعل الفاتورة مبسّطة إن كان فرداً",
                                          "The customer VAT number is invalid", "Correct the customer number, or make the invoice simplified for an individual"),
        ["BuyerAddressMissing"]   = Local("عنوان العميل ناقص (فاتورة ضريبية)", "أكمل عنوان العميل الوطني",
                                          "The customer address is incomplete (tax invoice)", "Complete the customer's national address"),
        ["BuyerNameMissing"]      = Local("اسم العميل مفقود (فاتورة ضريبية)", "أدخل اسم العميل",
                                          "The customer name is missing (tax invoice)", "Enter the customer name"),
        ["NoLines"]               = Local("الفاتورة بلا أصناف", "أضف صنفاً واحداً على الأقل",
                                          "The invoice has no lines", "Add at least one item"),
        ["ZeroOrNegativeTotal"]   = Local("إجمالي الفاتورة صفر أو سالب", "استخدم إشعاراً دائناً بدل الفاتورة السالبة",
                                          "The invoice total is zero or negative", "Use a credit note instead of a negative invoice"),
        ["ExemptionReasonMissing"]= Local("صنف معفى أو صفري بلا سبب إعفاء", "اختر رمز وسبب الإعفاء للصنف",
                                          "An exempt or zero-rated line has no exemption reason", "Choose the exemption code and reason for the item"),
        ["NoteWithoutReference"]  = Local("إشعار دائن/مدين بلا فاتورة أصلية", "اربط الإشعار برقم الفاتورة الأصلية",
                                          "Credit/debit note without an original invoice", "Link the note to the original invoice number"),
        ["NoteWithoutReason"]     = Local("إشعار دائن/مدين بلا سبب", "أدخل سبب الإشعار",
                                          "Credit/debit note without a reason", "Enter the reason for the note"),
        ["NotPosted"]             = Local("الفاتورة غير مرحّلة", "رحّل الفاتورة أولاً",
                                          "The invoice is not posted", "Post the invoice first"),
        ["AlreadyAccepted"]       = Local("الفاتورة أُرسلت وقُبلت مسبقاً", "لا تُرسل مرتين؛ للتعديل استخدم إشعاراً",
                                          "The invoice was already sent and accepted", "Do not send it twice; use a note to amend it"),
        ["AlreadyRejected"]       = Local("الفاتورة رُفضت مسبقاً", "عدّلها وصحّح سبب الرفض ثم أعد إرسالها (تُرسل بمعرّف جديد)",
                                          "The invoice was already rejected", "Edit it to fix the cause of rejection, then resend it (it is sent with a new UUID)"),
        ["ChainBusy"]             = Retry("فاتورة أخرى لنفس الوحدة قيد الإرسال", "أعد المحاولة بعد ثوانٍ (قفل السلسلة)",
                                          "Another invoice for the same unit is being sent", "Retry in a few seconds (chain lock)"),
        ["ReportingOverdue"]      = LocalWarning("فاتورة مبسّطة تجاوزت 24 ساعة دون إبلاغ", "أرسلها فوراً وراجع سبب التأخير",
                                          "Simplified invoice not reported within 24 hours", "Send it now and review the cause of the delay"),
    };

    // ───────────── 2) رموز HTTP من بوابة فاتورة ─────────────
    public static readonly IReadOnlyDictionary<int, ZatcaIssue> Http = new Dictionary<int, ZatcaIssue>
    {
        [200] = new("مقبولة", "لا شيء", "Accepted", "Nothing to do", ZatcaIssueKind.Warning, ZatcaChainEffect.Consumed),
        [202] = new("مقبولة مع تحذيرات", "صحّح سبب التحذير في الفواتير القادمة؛ لا تُعيد هذه",
                    "Accepted with warnings", "Fix the cause in future invoices; do not resend this one", ZatcaIssueKind.Warning, ZatcaChainEffect.Consumed),
        [400] = new("مرفوضة: طلب أو فاتورة غير صالحة", "اقرأ errorMessages، صحّح، وأصدر مستنداً جديداً",
                    "Rejected: invalid request or invoice", "Read errorMessages, correct, and issue a new document", ZatcaIssueKind.Block, ZatcaChainEffect.Consumed),
        [401] = new("الشهادة أو السر غير صحيح", "تحقق من CSID والسر للوحدة، أو أعد الربط",
                    "Invalid certificate or secret", "Check the unit's CSID and secret, or re-link it", ZatcaIssueKind.Block, ZatcaChainEffect.Keep),
        [403] = new("غير مصرّح", "تحقق من أن الرقم الضريبي يطابق شهادة الوحدة",
                    "Forbidden", "Check that the VAT number matches the unit certificate", ZatcaIssueKind.Block, ZatcaChainEffect.Keep),
        [404] = new("رابط الواجهة غير موجود", "تحقق من رابط البيئة (developer-portal / simulation / core)",
                    "API URL not found", "Check the environment URL (developer-portal / simulation / core)", ZatcaIssueKind.Retry, ZatcaChainEffect.Keep),
        [413] = new("حجم الطلب كبير جداً", "قلّل حجم الفاتورة أو المرفقات",
                    "Request too large", "Reduce the invoice or attachment size", ZatcaIssueKind.Block, ZatcaChainEffect.Keep),
        [422] = new("فشلت قواعد التحقق", "اقرأ errorMessages، صحّح، وأصدر مستنداً جديداً",
                    "Validation rules failed", "Read errorMessages, correct, and issue a new document", ZatcaIssueKind.Block, ZatcaChainEffect.Consumed),
        [429] = new("طلبات كثيرة", "انتظر وأعد المحاولة بتباعد تدريجي",
                    "Too many requests", "Wait and retry with progressive back-off", ZatcaIssueKind.Retry, ZatcaChainEffect.Keep),
        [500] = new("خطأ في خادم الهيئة", "أعد المحاولة لاحقاً بنفس البيانات",
                    "ZATCA server error", "Retry later with the same data", ZatcaIssueKind.Retry, ZatcaChainEffect.Keep),
        [503] = new("خدمة الهيئة غير متاحة", "أعد المحاولة لاحقاً بنفس البيانات",
                    "ZATCA service unavailable", "Retry later with the same data", ZatcaIssueKind.Retry, ZatcaChainEffect.Keep),
        [504] = new("انتهت مهلة الاتصال", "تحقق من حالة الفاتورة قبل إعادة الإرسال",
                    "Connection timed out", "Check the invoice status before resending", ZatcaIssueKind.Retry, ZatcaChainEffect.Keep),
    };

    // ───────────── 3) رموز رسائل التحقق (تطابق تام) ─────────────
    public static readonly IReadOnlyDictionary<string, ZatcaIssue> Codes = new Dictionary<string, ZatcaIssue>(StringComparer.OrdinalIgnoreCase)
    {
        // البصمة والتوقيع والشهادة
        ["invalid-invoice-hash"]               = Block("بصمة الطلب لا تطابق بصمة XML", "احسب البصمة من نفس النص المرسل تماماً (بلا تعديل مسافات أو ترميز)",
                                                       "The request hash does not match the XML hash", "Compute the hash from exactly the text sent (no whitespace or encoding changes)"),
        ["InvoiceHash-Errors"]                 = Block("البصمة مفقودة من الطلب", "أرسل invoiceHash في جسم الطلب",
                                                       "The hash is missing from the request", "Send invoiceHash in the request body"),
        ["signing-error"]                      = Block("فشل التحقق من التوقيع", "تأكد من مطابقة المفتاح الخاص لشهادة الوحدة",
                                                       "Signature verification failed", "Make sure the private key matches the unit certificate"),
        ["Missing-Signature"]                  = Block("الفاتورة بلا توقيع رقمي", "وقّع الفاتورة قبل الإرسال",
                                                       "The invoice has no digital signature", "Sign the invoice before sending"),
        ["Signature-Value"]                    = Block("قيمة التوقيع غير صحيحة", "تحقق من توقيع البصمة بـ ECDSA-SHA256 والمفتاح الصحيح",
                                                       "Invalid signature value", "Check that the hash is signed with ECDSA-SHA256 and the correct key"),
        ["Invalid-Authentication-Certificate"] = Block("شهادة المصادقة غير صالحة", "استخدم شهادة الإنتاج (لا الامتثال) للبيئة الصحيحة",
                                                       "Invalid authentication certificate", "Use the production (not compliance) certificate for the right environment"),
        ["Invalid-Request"]                    = Block("طلب غير صالح", "تحقق من بنية JSON والترويسات (Accept-Version: V2)",
                                                       "Invalid request", "Check the JSON structure and headers (Accept-Version: V2)"),
        ["Invalid-OTP"]                        = Block("رمز OTP غير صالح أو منتهٍ", "استخرج رمزاً جديداً من البوابة الصحيحة (محاكاة/إنتاج) واستخدمه خلال ساعة",
                                                       "Invalid or expired OTP", "Generate a new OTP from the right portal (simulation/production) and use it within an hour"),

        // الحقول الإلزامية (EN16931)
        ["BR-02"] = Block("رقم الفاتورة مفقود", "تأكد من cbc:ID", "Invoice number missing", "Check cbc:ID"),
        ["BR-03"] = Block("تاريخ الإصدار مفقود", "تأكد من cbc:IssueDate", "Issue date missing", "Check cbc:IssueDate"),
        ["BR-04"] = Block("رمز نوع الفاتورة مفقود", "388 فاتورة، 381 دائن، 383 مدين", "Invoice type code missing", "388 invoice, 381 credit, 383 debit"),
        ["BR-05"] = Block("رمز العملة مفقود", "تأكد من DocumentCurrencyCode", "Currency code missing", "Check DocumentCurrencyCode"),
        ["BR-13"] = Block("الإجمالي قبل الضريبة مفقود", "تأكد من TaxExclusiveAmount", "Total before VAT missing", "Check TaxExclusiveAmount"),

        // اتساق الحسابات
        ["BR-CO-10"] = Block("مجموع صافي الأسطر لا يطابق الإجمالي", "LineExtensionAmount = مجموع صافي الأسطر بعد التقريب",
                             "Sum of line net amounts does not match the total", "LineExtensionAmount = sum of rounded line net amounts"),
        ["BR-CO-15"] = Block("الإجمالي مع الضريبة ≠ قبل الضريبة + الضريبة", "راجع التقريب: قرّب كل سطر لخانتين ثم اجمع",
                             "Total with VAT ≠ total before VAT + VAT", "Review rounding: round each line to 2 decimals, then sum"),
        ["BR-KSA-EN16931-09"] = Block("مبلغ الضريبة لا يطابق النسبة", "الضريبة = الصافي × النسبة، مقرّبة لخانتين",
                                      "VAT amount does not match the rate", "VAT = net × rate, rounded to 2 decimals"),

        // العملة وقوائم الرموز
        ["BR-KSA-EN16931-04"] = Block("عملة الضريبة غير صحيحة", "TaxCurrencyCode يجب أن تكون SAR دائماً",
                                      "Invalid tax currency", "TaxCurrencyCode must always be SAR"),
        ["BR-CL-01"]          = Block("رمز من خارج القوائم المعتمدة", "استخدم رموز ISO وقوائم الهيئة فقط",
                                      "Code outside the approved lists", "Use only ISO and ZATCA code lists"),

        // السلسلة
        ["BR-KSA-31"] = Block("مشكلة في عدّاد الفواتير أو المعرّف الفريد", "ICV تصاعدي لكل وحدة، وUUID لا يتكرر أبداً",
                              "Invoice counter or unique ID problem", "ICV increases per unit, and a UUID is never reused"),
    };

    // ───────────── بادئات احتياطية لأي رمز غير مُدرج ─────────────
    private static readonly (string Prefix, ZatcaIssue Issue)[] Prefixes =
    {
        ("XSD",        Block("ملف XML لا يطابق مخطط UBL 2.1", "راجع ترتيب العناصر والـ namespaces؛ تحقق بأداة SDK",
                             "The XML does not match the UBL 2.1 schema", "Review element order and namespaces; validate with the SDK")),
        ("BR-KSA-EN",  Block("قاعدة سعودية مبنية على المعيار الأوروبي", "راجع الحقل المذكور في الرسالة",
                             "Saudi rule based on the European standard", "Review the field named in the message")),
        ("BR-KSA",     Block("قاعدة خاصة بالمملكة", "راجع الحقل المذكور في الرسالة في دليل قواعد الهيئة",
                             "Saudi-specific rule", "Review the field named in the message in the ZATCA rules guide")),
        ("BR-CO",      Block("خطأ في اتساق الحسابات", "راجع الإجماليات والتقريب",
                             "Calculation consistency error", "Review totals and rounding")),
        ("BR-S",       Block("خطأ في تصنيف الضريبة الأساسية (S)", "تأكد من النسبة 15% وتفصيل الضريبة",
                             "Standard rate (S) category error", "Check the 15% rate and the VAT breakdown")),
        ("BR-Z",       Block("خطأ في الصنف الصفري (Z)", "النسبة 0 مع رمز وسبب الإعفاء",
                             "Zero-rated (Z) line error", "Rate 0 with an exemption code and reason")),
        ("BR-E",       Block("خطأ في الصنف المعفى (E)", "النسبة 0 مع رمز وسبب الإعفاء",
                             "Exempt (E) line error", "Rate 0 with an exemption code and reason")),
        ("BR-O",       Block("خطأ في صنف خارج النطاق (O)", "لا ضريبة، مع سبب",
                             "Out-of-scope (O) line error", "No VAT, with a reason")),
        ("BR-CL",      Block("رمز من خارج القوائم المعتمدة", "راجع رموز العملة والنوع والتصنيف",
                             "Code outside the approved lists", "Review the currency, type and category codes")),
        ("BR",         Block("خطأ في قواعد العمل", "راجع الحقل المذكور في الرسالة",
                             "Business rule error", "Review the field named in the message")),
        ("QR",         Block("رمز QR غير صحيح", "تحقق من ترتيب وسوم TLV وأن بصمته تطابق بصمة XML",
                             "Invalid QR code", "Check the TLV tag order and that its hash matches the XML hash")),
        ("Signature",  Block("خطأ في التوقيع", "تحقق من المفتاح والشهادة وكتلة XAdES",
                             "Signature error", "Check the key, certificate and XAdES block")),
        ("Certificate",Warning("ملاحظة على الشهادة", "تحقق من بيانات المُصدر والرقم التسلسلي",
                             "Certificate note", "Check the issuer details and serial number")),
    };

    /// <summary>يبحث عن الرمز تطابقاً تاماً، ثم بأطول بادئة مطابقة، وإلا يعيد حالة عامة.</summary>
    public static ZatcaIssue Resolve(string? code, bool isWarning = false)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            if (Codes.TryGetValue(code, out var exact)) return isWarning ? exact with { Kind = ZatcaIssueKind.Warning, Chain = ZatcaChainEffect.Consumed } : exact;
            foreach (var (prefix, issue) in Prefixes)
                if (code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return isWarning ? issue with { Kind = ZatcaIssueKind.Warning, Chain = ZatcaChainEffect.Consumed } : issue;
        }
        return isWarning
            ? Warning("ملاحظة من الهيئة", "راجع نص الرسالة", "ZATCA note", "Review the message text")
            : Block("رفض من الهيئة", "راجع نص الرسالة الأصلي", "Rejected by ZATCA", "Review the original message text");
    }

    public static ZatcaIssue ResolveHttp(int status)
        => Http.TryGetValue(status, out var issue) ? issue
         : status >= 500 ? Http[500]
         : Block($"استجابة غير متوقعة ({status})", "سجّل الرد كاملاً وراجعه", $"Unexpected response ({status})", "Log the full response and review it");

    private static ZatcaIssue Block(string t, string f, string tEn, string fEn) => new(t, f, tEn, fEn, ZatcaIssueKind.Block, ZatcaChainEffect.Consumed);
    private static ZatcaIssue Warning(string t, string f, string tEn, string fEn) => new(t, f, tEn, fEn, ZatcaIssueKind.Warning, ZatcaChainEffect.Consumed);
    // الفحوص المحلية لا تصل للهيئة، فلا تستهلك ICV
    private static ZatcaIssue Local(string t, string f, string tEn, string fEn) => new(t, f, tEn, fEn, ZatcaIssueKind.Block, ZatcaChainEffect.Keep);
    private static ZatcaIssue LocalWarning(string t, string f, string tEn, string fEn) => new(t, f, tEn, fEn, ZatcaIssueKind.Warning, ZatcaChainEffect.Keep);
    private static ZatcaIssue Retry(string t, string f, string tEn, string fEn) => new(t, f, tEn, fEn, ZatcaIssueKind.Retry, ZatcaChainEffect.Keep);
}
