using System;

namespace UnityRuntimeCameraRecorder
{
    // Provides optional logging callbacks without coupling the library to a logging framework.
    public static class MediaRecorderLog
    {
        public static Action<string> Info { get; set; }
        public static Action<string> Warning { get; set; }
        public static Action<Exception> Error { get; set; }

        // Reports an informational message when a callback is configured.
        internal static void WriteInfo(string message)
        {
            Info?.Invoke(message);
        }

        // Reports a warning when a callback is configured.
        internal static void WriteWarning(string message)
        {
            Warning?.Invoke(message);
        }

        // Reports an exception when a callback is configured.
        internal static void WriteError(Exception exception)
        {
            Error?.Invoke(exception);
        }
    }
}
