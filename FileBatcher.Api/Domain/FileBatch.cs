namespace FileBatcher.Domain;

public class FileBatch
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public FileBatchAction Action { get; set; }
    public FileBatchStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<FileBatchItem> Items { get; set; } = new List<FileBatchItem>();
}
