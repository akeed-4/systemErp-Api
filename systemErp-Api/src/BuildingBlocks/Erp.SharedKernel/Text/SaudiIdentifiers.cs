namespace Erp.SharedKernel.Text;

/// <summary>Format rules for Saudi identifiers, shared by every module that captures parties or bank details.</summary>
public static class SaudiIdentifiers
{
    /// <summary>ZATCA VAT registration number: 15 digits, first and last digit 3.</summary>
    public static bool IsVatNumber(string? value)
    {
        var v = value?.Trim();
        return v is { Length: 15 } && v.All(char.IsAsciiDigit) && v[0] == '3' && v[^1] == '3';
    }

    /// <summary>National id (1…), iqama (2…) or commercial registration: 10 digits.</summary>
    public static bool IsTenDigitId(string? value)
    {
        var v = value?.Trim();
        return v is { Length: 10 } && v.All(char.IsAsciiDigit);
    }

    /// <summary>IBAN length 15–34; Saudi IBANs are SA + 22 digits.</summary>
    public static bool IsIban(string? value)
    {
        var v = value?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        return v is { Length: >= 15 and <= 34 } && (!v.StartsWith("SA", StringComparison.Ordinal) || (v.Length == 24 && v[2..].All(char.IsAsciiDigit)));
    }
}
