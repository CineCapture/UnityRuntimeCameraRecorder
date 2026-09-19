using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using FFmpegMediaWriter;

namespace UnityMediaRecorder
{
    // Generates one readable statistics file for one completed video.
    internal sealed class VideoStatisticsSnapshot
    {
        private const float WindowStart = 1f;
        private const float WindowDuration = 5f;
        private readonly RecordingSettings _settings;
        private readonly RecordingQualityProfile _profile;
        private readonly string _diagnostics;
        private readonly float _captureDuration;
        private readonly int _vSyncCount;
        private readonly string _ffprobePath;
        private readonly string _statisticsPath;

        // Stores immutable values before the worker leaves the Unity thread.
        internal VideoStatisticsSnapshot(RecordingSettings settings, RecordingQualityProfile profile, string diagnostics,
            float captureDuration, int vSyncCount, string ffprobePath, string statisticsPath)
        {
            _settings = settings;
            _profile = profile;
            _diagnostics = diagnostics ?? "{}";
            _captureDuration = captureDuration;
            _vSyncCount = vSyncCount;
            _ffprobePath = ffprobePath;
            _statisticsPath = statisticsPath;
        }

        // Measures the completed MP4 and writes one metric per line.
        internal void Write()
        {
            ProbeResult probe = Probe();
            long requested = ReadInteger("requested");
            long dropped = ReadInteger("dropped");
            var lines = new List<string>
            {
                $"Resolution: {_settings.Width} x {_settings.Height}",
                $"Target frame rate: {_settings.MaximumFrameRate} FPS",
                $"VSync: {(_vSyncCount > 0 ? "Enabled" : "Disabled")}",
                $"Anti-aliasing: {(_settings.SourceAntiAliasingSamples > 1 ? $"MSAA {_settings.SourceAntiAliasingSamples}x" : "Disabled")}",
                $"Video codec: {(_settings.VideoStreamFormat == VideoStreamFormat.Hevc ? "HEVC" : "H.264")}",
                $"NVENC preset: P{ReadInteger("preset", _profile.NativeEncodingPreset)}",
                $"CQP: {_profile.QuantizationParameter}",
                $"AAC audio bitrate: {_profile.AudioBitRate / 1000} kbps",
                $"Spatial AQ: {ReadBoolean("spatialAQ", true)}",
                $"Temporal AQ: {ReadBoolean("temporalAQ", false)}",
                $"Lookahead: {(ReadInteger("lookaheadDepth") > 0 ? "Enabled" : "Disabled")}",
                $"B-frames: {ReadInteger("bFrames")}",
                $"Capture duration: {FormatDuration(probe.Duration > 0 ? probe.Duration : _captureDuration)}",
                "Unity render FPS: " + (requested > 0 && _captureDuration > 0 ? requested / _captureDuration : 0).ToString("0.0", CultureInfo.InvariantCulture),
                "Video FPS: " + probe.FramesPerSecond.ToString("0.0", CultureInfo.InvariantCulture),
                $"Dropped frames: {dropped} / {requested}",
                "File size: " + (new FileInfo(_settings.OutputPath).Length / 1_000_000_000.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB"
            };
            File.WriteAllLines(_statisticsPath, lines);
        }

        // Runs FFprobe over at most five seconds after skipping the first second.
        private ProbeResult Probe()
        {
            if (!File.Exists(_ffprobePath))
            {
                throw new FileNotFoundException("FFprobe was not found.", _ffprobePath);
            }
            string arguments = "-v error -read_intervals 0%6 -select_streams v:0 " +
                "-show_entries stream=duration:frame=best_effort_timestamp_time -of default=noprint_wrappers=1 " + Quote(_settings.OutputPath);
            var startInfo = new ProcessStartInfo(_ffprobePath, arguments)
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("FFprobe failed while measuring the completed video.");
                }
                return ParseProbeOutput(output);
            }
        }

        // Calculates duration and FPS from FFprobe output.
        private static ProbeResult ParseProbeOutput(string output)
        {
            float duration = 0;
            int measuredFrames = 0;
            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("duration=", StringComparison.Ordinal))
                {
                    float.TryParse(line.Substring(9), NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
                }
                else if (line.StartsWith("best_effort_timestamp_time=", StringComparison.Ordinal) &&
                    float.TryParse(line.Substring(27), NumberStyles.Float, CultureInfo.InvariantCulture, out float timestamp) &&
                    timestamp >= WindowStart && timestamp < Math.Min(duration > 0 ? duration : WindowStart + WindowDuration, WindowStart + WindowDuration))
                {
                    measuredFrames++;
                }
            }
            float measuredDuration = Math.Max(0, Math.Min(duration, WindowStart + WindowDuration) - WindowStart);
            return new ProbeResult(duration, measuredDuration > 0 ? measuredFrames / measuredDuration : 0);
        }

        // Reads one integer from native diagnostics without requiring a JSON dependency.
        private long ReadInteger(string name, long fallback = 0)
        {
            Match match = Regex.Match(_diagnostics, "\\\"" + name + "\\\":(\\d+)");
            return match.Success && long.TryParse(match.Groups[1].Value, out long value) ? value : fallback;
        }

        // Reads one boolean from native diagnostics as a readable value.
        private string ReadBoolean(string name, bool fallback)
        {
            Match match = Regex.Match(_diagnostics, "\\\"" + name + "\\\":(true|false)");
            bool value = match.Success ? match.Groups[1].Value == "true" : fallback;
            return value ? "Enabled" : "Disabled";
        }

        // Formats a duration with whole minutes and seconds.
        private static string FormatDuration(float seconds)
        {
            int rounded = (int)Math.Round(seconds);
            return $"{rounded / 60} min {rounded % 60} s";
        }

        // Quotes a process argument that may contain spaces.
        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        // Stores the two FFprobe measurements used by the report.
        private readonly struct ProbeResult
        {
            internal ProbeResult(float duration, float framesPerSecond)
            {
                Duration = duration;
                FramesPerSecond = framesPerSecond;
            }

            internal float Duration { get; }
            internal float FramesPerSecond { get; }
        }
    }
}
