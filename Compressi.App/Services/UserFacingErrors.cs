namespace Compressi_App.Services;

public readonly record struct UserFacingError(string Message, string? ActionLabel);

public static class UserFacingErrors
{
    public const string ActionBrowse = "Browse";
    public const string ActionRetry = "Retry";

    public static UserFacingError FromException(Exception ex)
    {
        switch (ex)
        {
            case FileNotFoundException fnf when IsMissingEngine(fnf):
                return new UserFacingError(
                    "Compressi couldn't find its video engine. Reinstall the app to restore it.",
                    null);

            case FileNotFoundException:
                return new UserFacingError(
                    "The selected video file could not be found. Choose another file and try again.",
                    ActionBrowse);

            case InvalidOperationException ioe when ioe.Message.Contains("supported video", StringComparison.OrdinalIgnoreCase):
                return new UserFacingError(ioe.Message, ActionBrowse);

            case InvalidOperationException ioe when IsPlainPreflightMessage(ioe.Message):
                return new UserFacingError(ioe.Message, ActionRetry);

            case OperationCanceledException:
                return new UserFacingError("Compression was cancelled.", null);

            default:
                return new UserFacingError(Sanitize(ex.Message), ActionRetry);
        }
    }

    private static bool IsMissingEngine(FileNotFoundException ex)
    {
        var haystack = $"{ex.Message} {ex.FileName}";
        return haystack.Contains("ffprobe", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("Assets/ffmpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlainPreflightMessage(string message) =>
        !string.IsNullOrWhiteSpace(message)
        && message.Length <= 160
        && !message.Contains('\n')
        && !message.Contains('@')
        && !message.Contains("[", StringComparison.Ordinal);

    private static string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Something went wrong. Try again or choose a different file.";
        }

        var trimmed = message.Trim();
        if (!IsPlainPreflightMessage(trimmed))
        {
            return "Couldn't process this video. Try another file or a different format.";
        }

        return trimmed;
    }
}
