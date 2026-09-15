namespace Landoria.UnityMediaRecorder
{
    // Describes one recording session without depending on a game or configuration framework.
    public sealed class RecordingSettings
    {
        public string FfmpegPath { get; set; }
        public string TemporaryContainerPath { get; set; }
        public string ArchivePath { get; set; }
        public string OutputPath { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int MaximumFrameRate { get; set; } = 60;
        public bool FlipVertically { get; set; }
    }
}
