using UnityEngine;
using FFmpegMediaWriter;

namespace UnityMediaRecorder
{
    // Defines the replaceable video capture and encoding boundary used by the recorder.
    public abstract class VideoCaptureBackend : MonoBehaviour
    {
        public abstract string Name { get; }
        public abstract VideoStreamFormat StreamFormat { get; }
        public virtual string DiagnosticsJson => null;

        // Validates the requested codec before the writer is configured.
        public virtual void ConfigureStreamFormat(VideoStreamFormat format)
        {
            if (format != StreamFormat)
            {
                throw new System.NotSupportedException("This video backend does not support the requested codec.");
            }
        }

        // Starts producing video data for one recording session.
        public abstract void StartCapture(VideoCaptureContext context);

        // Stops production and releases all backend-owned resources.
        public abstract void StopCapture();
    }
}
