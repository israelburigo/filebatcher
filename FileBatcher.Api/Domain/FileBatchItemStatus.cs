namespace FileBatcher.Domain;

/// <summary>Status de uma linha do arquivo.</summary>
public enum FileBatchItemStatus
{
    PENDING,
    ERROR,
    IGNORED,
    PROCESSED
}
