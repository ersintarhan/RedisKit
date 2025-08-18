namespace RedisKit.Models;

/// <summary>
/// Represents the result of a batch execution.
/// </summary>
public class BatchResult
{
    /// <summary>
    /// Gets a value indicating whether all operations in the batch completed successfully.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BatchResult"/> class.
    /// </summary>
    /// <param name="isSuccess">A value indicating whether the batch was successful.</param>
    public BatchResult(bool isSuccess)
    {
        IsSuccess = isSuccess;
    }
}
