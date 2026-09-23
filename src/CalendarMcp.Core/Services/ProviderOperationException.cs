namespace CalendarMcp.Core.Services;

/// <summary>
/// Thrown by a provider for a failure whose message is written for the MCP caller, such as
/// a folder that doesn't exist or an account missing required configuration. Tools pass the
/// message to the client, unlike other exceptions, whose messages stay server-side.
/// </summary>
/// <remarks>
/// The message must not contain secrets, tokens or server internals such as file paths.
/// Derives from <see cref="InvalidOperationException"/> so existing handlers keep working.
/// </remarks>
public class ProviderOperationException : InvalidOperationException
{
    public ProviderOperationException(string message)
        : base(message)
    {
    }

    public ProviderOperationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
