namespace UnityRuntimeCameraRecorder
{
    // Describes a numbered PNG image-sequence capture session.
    public sealed class PngSequenceSettings
    {
        public string OutputDirectory { get; set; }
        public string FileNamePrefix { get; set; } = "frame_";
        public int Width { get; set; }
        public int Height { get; set; }
        public double CapturesPerSecond { get; set; } = 1.0;
        public double InitialDelaySeconds { get; set; }
        public int AntiAliasingSamples { get; set; } = 1;
        public bool FlipVertically { get; set; }
    }
}
