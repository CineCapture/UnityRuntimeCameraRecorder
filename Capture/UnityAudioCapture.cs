using System;
using UnityEngine;

namespace UnityRuntimeCameraRecorder
{
    // Captures Unity's active audio mix and forwards raw samples to FFmpeg.
    internal sealed class UnityAudioCapture : MonoBehaviour
    {
        private Func<byte[], bool> _writeAudio;
        private volatile int _channels;

        internal int Channels => _channels;
        internal int SampleRate => AudioSettings.outputSampleRate;
        internal bool IsReady => _channels > 0;

        // Assigns the format-neutral destination that receives captured audio buffers.
        internal void Initialize(Func<byte[], bool> writeAudio)
        {
            _writeAudio = writeAudio;
        }

        // Receives Unity audio samples on the audio thread and queues them for FFmpeg.
        private void OnAudioFilterRead(float[] data, int channels)
        {
            _channels = channels;
            if (_writeAudio == null)
            {
                return;
            }

            try
            {
                byte[] bytes = new byte[data.Length * sizeof(float)];
                Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
                _writeAudio(bytes);
            }
            catch (Exception exception)
            {
                MediaRecorderLog.WriteError(exception);
            }
        }
    }
}
