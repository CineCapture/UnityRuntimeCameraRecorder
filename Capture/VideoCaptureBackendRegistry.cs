using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityRuntimeCameraRecorder
{
    // Stores ordered video backend factories and selects the first supported implementation.
    public static class VideoCaptureBackendRegistry
    {
        // Associates one backend factory with its selection priority.
        private sealed class Registration
        {
            internal int Priority;
            internal Func<GameObject, VideoCaptureBackend> Factory;
        }

        private static readonly List<Registration> Registrations = new List<Registration>();

        // Registers the built-in native encoding backend.
        static VideoCaptureBackendRegistry()
        {
            Register(CreateNativeBackend, 100);
        }

        // Registers a backend factory, where returning null means unsupported on this system.
        public static void Register(Func<GameObject, VideoCaptureBackend> factory, int priority = 0)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            Registrations.Add(new Registration { Priority = priority, Factory = factory });
            Registrations.Sort((left, right) => right.Priority.CompareTo(left.Priority));
        }

        // Creates the highest-priority backend supported by the current runtime.
        internal static VideoCaptureBackend Create(GameObject host)
        {
            foreach (Registration registration in Registrations)
            {
                VideoCaptureBackend backend = registration.Factory(host);
                if (backend != null)
                {
                    return backend;
                }
            }

            throw new NotSupportedException(
                "No compatible video encoder is available. The built-in encoder requires Windows, Direct3D 11, " +
                "an NVIDIA GPU and Direct3DVideoEncoder.dll. Image-sequence capture remains available.");
        }

        // Creates the native NVENC backend only on its supported graphics stack.
        private static VideoCaptureBackend CreateNativeBackend(GameObject host)
        {
            bool supported = (Application.platform == RuntimePlatform.WindowsPlayer ||
                              Application.platform == RuntimePlatform.WindowsEditor) &&
                             SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11 &&
                             SystemInfo.graphicsDeviceVendor.IndexOf(
                                 "NVIDIA",
                                 StringComparison.OrdinalIgnoreCase) >= 0 &&
                             NativeVideoCapture.IsAvailable();
            return supported ? host.AddComponent<NativeVideoCapture>() : null;
        }

    }
}
