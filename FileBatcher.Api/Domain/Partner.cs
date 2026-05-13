namespace FileBatcher.Domain;

public class Partner
{
    public Guid Id { get; set; }
    public PartnerStatus Status { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>CPF com 11 dígitos (somente números).</summary>
    public string Document { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
