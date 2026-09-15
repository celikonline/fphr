namespace Sense.Shared.Contracts.Configuration;

/// <summary>
/// Options for Redis configuration.
/// </summary>
public class RedisOptions
{
    /// <summary>
    /// Redis host address.
    /// </summary>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>
    /// Redis port.
    /// </summary>
    public int Port { get; init; } = 6379;

    /// <summary>
    /// Redis password (optional).
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Gets the Redis connection string.
    /// </summary>
    public string GetConnectionString()
    {
        if (string.IsNullOrWhiteSpace(Password))
        {
            return $"{Host}:{Port},abortConnect=false";
        }

        return $"{Host}:{Port},password={Password},abortConnect=false";
    }
}
