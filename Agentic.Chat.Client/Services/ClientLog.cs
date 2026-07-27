namespace Agentic.Chat.Services;

internal static class ClientLog
{
    private static readonly Action<ILogger, string, Exception?> WarningMessage =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(Warning)),
            "{Message}");

    private static readonly Action<ILogger, int, Exception?> MalformedPayloadMessage =
        LoggerMessage.Define<int>(
            LogLevel.Warning,
            new EventId(2, nameof(MalformedPayload)),
            "Ignoring malformed OpenRouter SSE payload ({PayloadLength} characters).");

    public static void Warning(ILogger logger, Exception exception, string message)
        => WarningMessage(logger, message, exception);

    public static void MalformedPayload(ILogger logger, Exception exception, int payloadLength)
        => MalformedPayloadMessage(logger, payloadLength, exception);
}
