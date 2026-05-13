namespace FileBatcher.Domain;

public class FileBatchItem
{
    public Guid Id { get; set; }
    public Guid FileBatchId { get; set; }
    public string Data { get; set; } = string.Empty;
    public FileBatchItemStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public FileBatch? FileBatch { get; set; }
}
