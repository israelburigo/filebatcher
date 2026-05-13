using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FileBatcher.Models;

/// <summary>Payload JSON armazenado em <see cref="Domain.FileBatchItem.Data"/>.</summary>
public sealed class PartnerRowPayload
{
    [JsonPropertyName("NOME")]
    public string? Nome { get; set; }

    [JsonPropertyName("EMAIL")]
    public string? Email { get; set; }

    [JsonPropertyName("CPF")]
    public string? Cpf { get; set; }

    [JsonPropertyName("TELEFONE")]
    public string? Telefone { get; set; }
}

public static class PartnerRowValidation
{
    private static readonly Regex NameRegex = new(
        @"^(?i)[\p{L}]+(?:\s+[\p{L}]+)+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Padrão (##)#####-#### com parênteses e hífen.</summary>
    private static readonly Regex PhoneRegex = new(
        @"^\(\d{2}\)\d{5}-\d{4}$",
        RegexOptions.Compiled);

    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string? NormalizeCpfDigits(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length == 11 ? digits : null;
    }

    public static bool IsValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && EmailRegex.IsMatch(email.Trim());

    public static bool IsValidPhone(string? phone) =>
        !string.IsNullOrWhiteSpace(phone) && PhoneRegex.IsMatch(phone.Trim());

    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && NameRegex.IsMatch(name.Trim());

    /// <summary>Valida dígitos verificadores do CPF brasileiro.</summary>
    public static bool IsValidCpfChecksum(string cpf11)
    {
        if (cpf11.Length != 11 || cpf11.Distinct().Count() == 1) return false;

        static int Digit(string s, int[] weights)
        {
            var sum = 0;
            for (var i = 0; i < weights.Length; i++)
                sum += (s[i] - '0') * weights[i];
            var mod = sum % 11;
            return mod < 2 ? 0 : 11 - mod;
        }

        var w1 = new[] { 10, 9, 8, 7, 6, 5, 4, 3, 2 };
        var w2 = new[] { 11, 10, 9, 8, 7, 6, 5, 4, 3, 2 };
        var d1 = Digit(cpf11, w1);
        var d2 = Digit(cpf11, w2);
        return d1 == cpf11[9] - '0' && d2 == cpf11[10] - '0';
    }

    public static bool RowPassesFieldRules(PartnerRowPayload row, out string? normalizedCpf)
    {
        normalizedCpf = NormalizeCpfDigits(row.Cpf);
        if (string.IsNullOrWhiteSpace(row.Nome)
            || string.IsNullOrWhiteSpace(row.Email)
            || string.IsNullOrWhiteSpace(row.Cpf)
            || string.IsNullOrWhiteSpace(row.Telefone))
            return false;

        if (!IsValidName(row.Nome)) return false;
        if (!IsValidEmail(row.Email)) return false;
        if (normalizedCpf is null || !IsValidCpfChecksum(normalizedCpf)) return false;
        if (!IsValidPhone(row.Telefone)) return false;
        return true;
    }
}
