namespace FileBatcher.Domain;

/// <summary>Status do arquivo de importação.</summary>
public enum FileBatchStatus
{
    IMPORTED,
    PROCESSING,
    ERROR,
    CANCELLED,
    PROCESSED
}
