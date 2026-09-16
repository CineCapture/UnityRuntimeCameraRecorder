using UnityEngine;
using FFmpegMediaWriter;

namespace UnityMediaRecorder
{
    // Defines the replaceable video capture and encoding boundary used by the recorder.
    public abstract class VideoCaptureBackend : MonoBehaviour
    {
        public abstract string Name { get; }
        public abstract VideoStreamFormat StreamFormat { get; }

        // Starts producing video data for one recording session.
        public abstract void StartCapture(VideoCaptureContext context);

        // Stops production and releases all backend-owned resources.
        public abstract void StopCapture();
    }
}
