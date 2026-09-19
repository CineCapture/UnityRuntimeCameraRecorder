namespace UnityRuntimeCameraRecorder
{
    // Selects the file format written by an image sequence capture.
    public enum ImageSequenceFormat
    {
        Png,
        Jpeg
    }

    // Describes a numbered PNG or JPEG image-sequence capture session.
    public sealed class PngSequenceSettings
    {
        public string OutputDirectory { get; set; }
        public string FileNamePrefix { get; set; } = "frame_";
        public int Width { get; set; }
        public int Height { get; set; }
        public double CapturesPerSecond { get; set; } = 1.0;
        public double InitialDelaySeconds { get; set; }
        public int AntiAliasingSamples { get; set; } = 1;
        public int EncoderThreadCount { get; set; } = 1;
        public int MaximumQueuedFrames { get; set; } = 4;
        public int MaximumFrameCount { get; set; }
        public ImageSequenceFormat FileFormat { get; set; } = ImageSequenceFormat.Png;
        public int JpegQuality { get; set; } = 95;
        public bool FlipVertically { get; set; }
    }
}
