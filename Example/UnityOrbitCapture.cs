using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Landoria.UnityMediaRecorder.Example
{
    // Builds a lit cube scene and records a complete camera orbit without game-specific code.
    public sealed class UnityOrbitCapture : MonoBehaviour
    {
        private const float CaptureDurationSeconds = 10f;
        private const float OrbitRadius = 5f;
        private const int OverlayLayer = 30;
        private Camera _camera;
        private Camera _overlayCamera;
        private Camera _staticCamera;
        private TemporalMotionSmoothing _mainTemporalSmoothing;
        private TemporalMotionSmoothing _staticTemporalSmoothing;
        private Text _fpsText;
        private Text _staticFpsText;
        private RenderTexture _preparedTarget;
        private RenderTexture _staticPreparedTarget;
        private UnityMediaRecorder _recorder;
        private UnityMediaRecorder _staticRecorder;
        private bool _captureStarted;
        private int _startedRecorderCount;
        private int _completedRecorderCount;
        private float _fpsElapsed;
        private int _fpsFrameCount;
        private float _renderFramesPerSecond;
        private long _lastFpsTimestamp;
        private float _staticFpsElapsed;
        private int _staticFpsFrameCount;
        private float _staticRenderFramesPerSecond;
        private long _staticLastFpsTimestamp;
        private int _captureWidth;
        private int _captureHeight;
        private int _antiAliasingSamples;
        private float _captureStartTime;
        private int _renderedFramesSinceCapture;
        private int _staticRenderedFramesSinceCapture;
        private int _previousTargetFrameRate;
        private int _previousVSyncCount;
        private string _benchmarkMode;

        // Creates the example automatically when an otherwise empty scene enters Play mode.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            new GameObject("UnityMediaRecorderExample").AddComponent<UnityOrbitCapture>();
        }

        // Builds the scene and begins the self-contained recording workflow.
        private void Start()
        {
            _benchmarkMode = Environment.GetEnvironmentVariable("CAPTURE_BENCHMARK_MODE") ?? string.Empty;
            ConfigureFramePacing();
            BuildScene();
            StartCoroutine(RecordOrbit());
        }

        // Disconnects example callbacks when its runtime object is destroyed.
        private void OnDestroy()
        {
            if (_recorder != null)
            {
                _recorder.CaptureStarted -= HandleCaptureStarted;
                _recorder.RecordingCompleted -= HandleRecordingCompleted;
                _recorder.RecordingFailed -= HandleRecordingFailed;
            }
            if (_staticRecorder != null)
            {
                _staticRecorder.CaptureStarted -= HandleCaptureStarted;
                _staticRecorder.RecordingCompleted -= HandleRecordingCompleted;
                _staticRecorder.RecordingFailed -= HandleRecordingFailed;
            }

            ReleasePreparedTarget();
            Camera.onPostRender -= HandleCameraPostRender;
            QualitySettings.vSyncCount = _previousVSyncCount;
            Application.targetFrameRate = _previousTargetFrameRate;
        }

        // Enables display-synchronized rendering while remembering the host application's settings.
        private void ConfigureFramePacing()
        {
            _previousVSyncCount = QualitySettings.vSyncCount;
            _previousTargetFrameRate = Application.targetFrameRate;
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
        }

        // Holds at each principal angle and performs two one-second eased half-turns.
        private void LateUpdate()
        {
            if (_camera == null)
            {
                return;
            }

            float captureElapsed = _captureStarted
                ? Time.realtimeSinceStartup - _captureStartTime
                : 0f;
            float angleDegrees;
            if (captureElapsed < 3f)
            {
                angleDegrees = 0f;
            }
            else if (captureElapsed < 5f)
            {
                angleDegrees = 180f * EvaluateOrbitTransition(captureElapsed - 3f);
            }
            else if (captureElapsed < 8f)
            {
                angleDegrees = 180f;
            }
            else
            {
                angleDegrees = 180f + 180f * EvaluateOrbitTransition(captureElapsed - 8f);
            }

            float angle = angleDegrees * Mathf.Deg2Rad;
            _camera.transform.position = new Vector3(
                Mathf.Sin(angle) * OrbitRadius,
                2.5f,
                Mathf.Cos(angle) * OrbitRadius);
            _camera.transform.LookAt(Vector3.up * 0.5f);
        }

        // Integrates the FirstPerson quintic easing curve to produce a smooth velocity ramp.
        private static float IntegratedEaseInOut(float progress)
        {
            float squared = progress * progress;
            float fourth = squared * squared;
            return progress * progress * fourth - 3f * progress * fourth + 2.5f * fourth;
        }

        // Evaluates a two-second move with half-second acceleration and deceleration ramps.
        private static float EvaluateOrbitTransition(float elapsed)
        {
            const float rampDuration = 0.5f;
            const float transitionDuration = 2f;
            const float normalizedCruiseSpeed = 2f / 3f;
            float clamped = Mathf.Clamp(elapsed, 0f, transitionDuration);
            if (clamped < rampDuration)
            {
                float rampProgress = clamped / rampDuration;
                return normalizedCruiseSpeed * rampDuration * IntegratedEaseInOut(rampProgress);
            }

            if (clamped <= transitionDuration - rampDuration)
            {
                return 1f / 6f + normalizedCruiseSpeed * (clamped - rampDuration);
            }

            float remaining = transitionDuration - clamped;
            float remainingProgress = remaining / rampDuration;
            return 1f - normalizedCruiseSpeed * rampDuration * IntegratedEaseInOut(remainingProgress);
        }

        // Creates a cube, floor, light, camera and audio listener entirely from code.
        private void BuildScene()
        {
            MediaRecorderLog.Info = Debug.Log;
            MediaRecorderLog.Warning = Debug.LogWarning;
            MediaRecorderLog.Error = Debug.LogException;
            Camera existingCamera = Camera.main;
            if (existingCamera != null)
            {
                existingCamera.gameObject.SetActive(false);
            }

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "RecordedCube";
            cube.transform.position = Vector3.up * 0.5f;
            ApplyExampleMaterial(cube, new Color(0.15f, 0.55f, 1f));
            cube.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = Vector3.one * 2f;
            ApplyExampleMaterial(floor, new Color(0.18f, 0.2f, 0.24f));
            floor.GetComponent<Renderer>().receiveShadows = true;

            GameObject lightObject = new GameObject("KeyLight");
            Light keyLight = lightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.2f;
            keyLight.shadows = LightShadows.Soft;
            keyLight.shadowStrength = 0.75f;
            keyLight.shadowBias = 0.035f;
            keyLight.shadowNormalBias = 0.25f;
            lightObject.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.25f, 0.25f, 0.3f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.16f, 0.2f, 0.25f);
            RenderSettings.fogDensity = 0.022f;
            CreateFloatingParticles();
            CreateHumidityMist();
            CreateGrassPatches();

            GameObject cameraObject = new GameObject("OrbitCamera");
            cameraObject.tag = "MainCamera";
            _camera = cameraObject.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.04f, 0.06f, 0.1f);
            _camera.cullingMask &= ~(1 << OverlayLayer);
            _mainTemporalSmoothing = cameraObject.AddComponent<TemporalMotionSmoothing>();
            cameraObject.AddComponent<AudioListener>();
            GameObject overlayCameraObject = new GameObject("OverlayCamera");
            overlayCameraObject.transform.SetParent(cameraObject.transform, false);
            _overlayCamera = overlayCameraObject.AddComponent<Camera>();
            _overlayCamera.clearFlags = CameraClearFlags.Depth;
            _overlayCamera.cullingMask = 1 << OverlayLayer;
            _overlayCamera.depth = _camera.depth + 1f;
            _overlayCamera.fieldOfView = _camera.fieldOfView;
            _overlayCamera.nearClipPlane = _camera.nearClipPlane;
            _overlayCamera.farClipPlane = _camera.farClipPlane;
            _fpsText = CreateDiagnosticOverlay(_overlayCamera, "MainCameraDiagnostics");
            Camera.onPostRender += HandleCameraPostRender;

            GameObject staticCameraObject = new GameObject("StaticCamera");
            _staticCamera = staticCameraObject.AddComponent<Camera>();
            _staticCamera.clearFlags = CameraClearFlags.SolidColor;
            _staticCamera.backgroundColor = _camera.backgroundColor;
            _staticCamera.transform.position = new Vector3(-5.2f, 3.2f, -5.2f);
            _staticCamera.transform.LookAt(Vector3.up * 0.55f);
            _staticTemporalSmoothing = staticCameraObject.AddComponent<TemporalMotionSmoothing>();
            _staticFpsText = CreateDiagnosticOverlay(_staticCamera, "StaticCameraDiagnostics");

            _recorder = gameObject.AddComponent<UnityMediaRecorder>();
            _recorder.CaptureStarted += HandleCaptureStarted;
            _recorder.RecordingCompleted += HandleRecordingCompleted;
            _recorder.RecordingFailed += HandleRecordingFailed;
            _staticRecorder = gameObject.AddComponent<UnityMediaRecorder>();
            _staticRecorder.CaptureStarted += HandleCaptureStarted;
            _staticRecorder.RecordingCompleted += HandleRecordingCompleted;
            _staticRecorder.RecordingFailed += HandleRecordingFailed;
        }

        // Builds clustered crossed-quad grass blades animated entirely by the GPU.
        private static void CreateGrassPatches()
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var ultraviolet = new List<Vector2>();
            var triangles = new List<int>();
            var random = new System.Random(7319);
            Vector2[] patchCenters =
            {
                new Vector2(-2.7f, -1.8f),
                new Vector2(-2.2f, 1.7f),
                new Vector2(2.5f, -1.5f),
                new Vector2(2.8f, 1.5f),
                new Vector2(-0.2f, 3.1f),
                new Vector2(0.3f, -3.2f)
            };

            foreach (Vector2 center in patchCenters)
            {
                for (int blade = 0; blade < 125; blade++)
                {
                    float radius = Mathf.Sqrt((float)random.NextDouble()) * 1.25f;
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    var position = new Vector3(
                        center.x + Mathf.Cos(angle) * radius,
                        0f,
                        center.y + Mathf.Sin(angle) * radius);
                    float height = Mathf.Lerp(0.5f, 1.25f, (float)random.NextDouble());
                    float width = Mathf.Lerp(0.022f, 0.052f, (float)random.NextDouble());
                    float rotation = (float)random.NextDouble() * Mathf.PI;
                    Color variation = Color.Lerp(
                        new Color(0.65f, 0.82f, 0.48f),
                        new Color(0.9f, 1f, 0.62f),
                        (float)random.NextDouble());
                    AddGrassBlade(vertices, colors, ultraviolet, triangles, position, width, height, rotation, variation);
                }
            }

            var mesh = new Mesh { name = "ProceduralGrassPatches" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, ultraviolet);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Bounds bounds = mesh.bounds;
            bounds.Expand(new Vector3(0.5f, 0.2f, 0.5f));
            mesh.bounds = bounds;

            GameObject grass = new GameObject("WindGrassPatches");
            grass.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = grass.AddComponent<MeshRenderer>();
            Shader shader = Shader.Find("Landoria/ExampleWindGrass");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "The Landoria/ExampleWindGrass shader is missing; copy the complete Example folder into the Unity project.");
            }

            renderer.material = new Material(shader);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        // Adds one crossed pair of tapered grass cards to the shared procedural mesh.
        private static void AddGrassBlade(
            List<Vector3> vertices,
            List<Color> colors,
            List<Vector2> ultraviolet,
            List<int> triangles,
            Vector3 position,
            float width,
            float height,
            float rotation,
            Color color)
        {
            const int verticalSegments = 6;
            for (int card = 0; card < 2; card++)
            {
                float angle = rotation + card * Mathf.PI * 0.5f;
                Vector3 side = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * width;
                int first = vertices.Count;
                for (int segment = 0; segment <= verticalSegments; segment++)
                {
                    float vertical = segment / (float)verticalSegments;
                    Vector3 center = position + Vector3.up * height * vertical;
                    vertices.Add(center - side);
                    vertices.Add(center + side);
                    colors.Add(color);
                    colors.Add(color);
                    ultraviolet.Add(new Vector2(0f, vertical));
                    ultraviolet.Add(new Vector2(1f, vertical));
                }

                for (int segment = 0; segment < verticalSegments; segment++)
                {
                    int lowerLeft = first + segment * 2;
                    int upperLeft = lowerLeft + 2;
                    triangles.Add(lowerLeft);
                    triangles.Add(upperLeft);
                    triangles.Add(lowerLeft + 1);
                    triangles.Add(lowerLeft + 1);
                    triangles.Add(upperLeft);
                    triangles.Add(upperLeft + 1);
                }
            }
        }

        // Creates softly drifting airborne particles around the recorded subject.
        private static void CreateFloatingParticles()
        {
            GameObject particleObject = new GameObject("FloatingAirParticles");
            particleObject.transform.position = new Vector3(0f, 2f, 0f);
            ParticleSystem particles = particleObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.prewarm = true;
            main.maxParticles = 300;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.015f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.075f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.75f, 0.88f, 1f, 0.18f),
                new Color(1f, 0.88f, 0.58f, 0.42f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 36f;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(10f, 4f, 10f);

            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0.9f, 1.55f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.04f, 0.08f);
            velocity.z = new ParticleSystem.MinMaxCurve(0.24f, 0.58f);

            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
            noise.frequency = 0.18f;
            noise.scrollSpeed = 0.12f;
            noise.damping = true;

            ParticleSystemRenderer renderer = particleObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = -10;
            Shader shader = Shader.Find("Landoria/ExampleParticle");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "The Landoria/ExampleParticle shader is missing; copy the complete Example folder into the Unity project.");
            }

            renderer.material = new Material(shader);
            particles.Play();
        }

        // Creates broad translucent mist layers that drift slowly near the ground.
        private static void CreateHumidityMist()
        {
            GameObject mistObject = new GameObject("HumidityMist");
            mistObject.transform.position = new Vector3(0f, 0.8f, 0f);
            ParticleSystem mist = mistObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = mist.main;
            main.loop = true;
            main.prewarm = true;
            main.maxParticles = 90;
            main.startLifetime = new ParticleSystem.MinMaxCurve(12f, 22f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.045f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 2.6f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.62f, 0.72f, 0.8f, 0.012f),
                new Color(0.78f, 0.84f, 0.88f, 0.045f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = mist.emission;
            emission.rateOverTime = 5f;

            ParticleSystem.ShapeModule shape = mist.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(12f, 1.6f, 12f);

            ParticleSystem.VelocityOverLifetimeModule velocity = mist.velocityOverLifetime;
            velocity.enabled = true;
            velocity.x = new ParticleSystem.MinMaxCurve(0.015f, 0.045f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.005f, 0.012f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);

            ParticleSystem.NoiseModule noise = mist.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.025f, 0.07f);
            noise.frequency = 0.06f;
            noise.scrollSpeed = 0.018f;
            noise.damping = true;

            ParticleSystemRenderer renderer = mistObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = -20;
            Shader shader = Shader.Find("Landoria/ExampleParticle");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "The Landoria/ExampleParticle shader is missing; copy the complete Example folder into the Unity project.");
            }

            renderer.material = new Material(shader);
            mist.Play();
        }

        // Creates one screen-space diagnostic canvas for a specific output camera.
        private static Text CreateDiagnosticOverlay(Camera targetCamera, string objectName)
        {
            GameObject canvasObject = new GameObject($"{objectName}Canvas");
            canvasObject.layer = OverlayLayer;
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = targetCamera;
            canvas.planeDistance = 1f;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject overlay = new GameObject(objectName);
            overlay.layer = OverlayLayer;
            overlay.transform.SetParent(canvasObject.transform, false);
            Text diagnosticText = overlay.AddComponent<Text>();
            diagnosticText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            diagnosticText.alignment = TextAnchor.UpperLeft;
            diagnosticText.fontSize = 20;
            diagnosticText.color = Color.white;
            diagnosticText.horizontalOverflow = HorizontalWrapMode.Overflow;
            diagnosticText.verticalOverflow = VerticalWrapMode.Overflow;
            diagnosticText.text = "Preparing recorder...";
            RectTransform overlayTransform = diagnosticText.rectTransform;
            overlayTransform.anchorMin = new Vector2(0f, 1f);
            overlayTransform.anchorMax = new Vector2(0f, 1f);
            overlayTransform.pivot = new Vector2(0f, 1f);
            overlayTransform.anchoredPosition = new Vector2(20f, -18f);
            overlayTransform.sizeDelta = new Vector2(1100f, 300f);
            return diagnosticText;
        }

        // Counts completed renders from the example camera inside Unity's normal render loop.
        private void HandleCameraPostRender(Camera renderedCamera)
        {
            long timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            if (renderedCamera == _camera)
            {
                UpdateFrameRateMeasurement(timestamp);
                return;
            }

            if (renderedCamera == _staticCamera)
            {
                UpdateStaticFrameRateMeasurement(timestamp);
            }
        }

        // Updates the measured rate after one static-camera render completes.
        private void UpdateStaticFrameRateMeasurement(long timestamp)
        {
            if (_staticFpsText == null)
            {
                return;
            }

            if (_staticLastFpsTimestamp == 0)
            {
                _staticLastFpsTimestamp = timestamp;
            }

            _staticFpsElapsed += (float)(timestamp - _staticLastFpsTimestamp) / System.Diagnostics.Stopwatch.Frequency;
            _staticLastFpsTimestamp = timestamp;
            _staticFpsFrameCount++;
            if (_captureStarted)
            {
                _staticRenderedFramesSinceCapture++;
            }
            if (_staticFpsElapsed >= 0.25f)
            {
                _staticRenderFramesPerSecond = _staticFpsFrameCount / _staticFpsElapsed;
                _staticFpsElapsed = 0f;
                _staticFpsFrameCount = 0;
                UpdateDiagnosticText();
            }
        }

        // Updates the measured rate after one camera render completes.
        private void UpdateFrameRateMeasurement(long timestamp)
        {
            if (_fpsText == null)
            {
                return;
            }

            if (_lastFpsTimestamp == 0)
            {
                _lastFpsTimestamp = timestamp;
            }

            _fpsElapsed += (float)(timestamp - _lastFpsTimestamp) / System.Diagnostics.Stopwatch.Frequency;
            _lastFpsTimestamp = timestamp;
            _fpsFrameCount++;
            if (_captureStarted)
            {
                _renderedFramesSinceCapture++;
            }
            if (_fpsElapsed >= 0.25f)
            {
                _renderFramesPerSecond = _fpsFrameCount / _fpsElapsed;
                _fpsElapsed = 0f;
                _fpsFrameCount = 0;
                UpdateDiagnosticText();
            }

        }

        // Formats the current scene and recorder diagnostics into the camera overlay.
        private void UpdateDiagnosticText()
        {
            float frameTimeMilliseconds = _renderFramesPerSecond > 0f
                ? 1000f / _renderFramesPerSecond
                : 0f;
            float elapsed = _captureStarted ? Time.realtimeSinceStartup - _captureStartTime : 0f;
            string commonDiagnostics =
                $"Capture: {_captureWidth}x{_captureHeight} @ 1 PNG/s  |  MSAA: {_antiAliasingSamples}x\n" +
                $"VSync: {(QualitySettings.vSyncCount > 0 ? "On" : "Off")}  |  Elapsed: {elapsed:0.0} s\n" +
                $"GPU: {SystemInfo.graphicsDeviceName}";
            string mainDiagnostics =
                $"Render: {_renderFramesPerSecond:0.0} FPS  ({frameTimeMilliseconds:0.0} ms)\n" +
                $"Rendered: {_renderedFramesSinceCapture}\n" +
                commonDiagnostics;
            _fpsText.text = $"View: Main Camera\n{mainDiagnostics}";
            if (_staticFpsText != null)
            {
                float staticFrameTimeMilliseconds = _staticRenderFramesPerSecond > 0f
                    ? 1000f / _staticRenderFramesPerSecond
                    : 0f;
                string staticDiagnostics =
                    $"Render: {_staticRenderFramesPerSecond:0.0} FPS  ({staticFrameTimeMilliseconds:0.0} ms)\n" +
                    $"Rendered: {_staticRenderedFramesSinceCapture}\n" +
                    commonDiagnostics;
                _staticFpsText.text = $"View: Static Camera\n{staticDiagnostics}";
            }
        }

        // Assigns the example shader explicitly so standalone shader stripping cannot produce magenta objects.
        private static void ApplyExampleMaterial(GameObject target, Color color)
        {
            Shader shader = Shader.Find("Landoria/ExampleLit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "The Landoria/ExampleLit shader is missing; copy the complete Example folder into the Unity project.");
            }

            var material = new Material(shader);
            material.color = color;
            target.GetComponent<Renderer>().material = material;
        }

        // Starts recording, completes the ten-second staged revolution and requests finalization.
        private IEnumerator RecordOrbit()
        {
            yield return null;
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "UnityMediaRecorderExample");
            Directory.CreateDirectory(directory);
            int width = ReadPositiveEnvironmentInteger("CAPTURE_WIDTH", Math.Max(2, Screen.width & ~1)) & ~1;
            int height = ReadPositiveEnvironmentInteger("CAPTURE_HEIGHT", Math.Max(2, Screen.height & ~1)) & ~1;
            int frameRate = ReadPositiveEnvironmentInteger("CAPTURE_FRAME_RATE", 60);
            int antiAliasingSamples = ReadPositiveEnvironmentInteger("CAPTURE_MSAA", 4);
            _captureWidth = width;
            _captureHeight = height;
            _antiAliasingSamples = antiAliasingSamples;
            string profileName = Environment.GetEnvironmentVariable("CAPTURE_PROFILE") ?? $"{width}x{height}_{frameRate}fps";
            string baseName = $"CubeOrbit_{profileName}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
            _preparedTarget = new RenderTexture(
                width,
                height,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            _preparedTarget.antiAliasing = antiAliasingSamples;
            _preparedTarget.Create();
            _staticPreparedTarget = new RenderTexture(
                width,
                height,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            _staticPreparedTarget.antiAliasing = antiAliasingSamples;
            _staticPreparedTarget.Create();
            _camera.targetTexture = _preparedTarget;
            _overlayCamera.targetTexture = _preparedTarget;
            _staticCamera.targetTexture = _staticPreparedTarget;
            if (!string.IsNullOrEmpty(_benchmarkMode))
            {
                yield return RunBenchmarkMode(
                    directory,
                    baseName,
                    width,
                    height,
                    frameRate,
                    antiAliasingSamples);
                yield break;
            }

            _recorder.StartPngSequence(
                _overlayCamera,
                CreatePngSequenceSettings(
                    Path.Combine(directory, $"{baseName}_MainCamera_Frames"),
                    width,
                    height,
                    antiAliasingSamples,
                    0.0),
                _preparedTarget);
            _staticRecorder.StartPngSequence(
                _staticCamera,
                CreatePngSequenceSettings(
                    Path.Combine(directory, $"{baseName}_StaticCamera_Frames"),
                    width,
                    height,
                    antiAliasingSamples,
                    0.5),
                _staticPreparedTarget);
            while (!_captureStarted)
            {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(CaptureDurationSeconds);
            _recorder.StopPngSequence();
            _staticRecorder.StopPngSequence();
        }

        // Runs one controlled render or NVENC benchmark selected through the environment.
        private IEnumerator RunBenchmarkMode(
            string directory,
            string baseName,
            int width,
            int height,
            int frameRate,
            int antiAliasingSamples)
        {
            bool singleCamera = string.Equals(_benchmarkMode, "single-render", StringComparison.OrdinalIgnoreCase);
            bool useNvenc = _benchmarkMode.StartsWith("dual-nvenc", StringComparison.OrdinalIgnoreCase);
            bool disableTemporal = string.Equals(
                _benchmarkMode,
                "dual-nvenc-no-temporal",
                StringComparison.OrdinalIgnoreCase);
            _staticCamera.enabled = !singleCamera;
            if (disableTemporal)
            {
                _mainTemporalSmoothing.enabled = false;
                _staticTemporalSmoothing.enabled = false;
            }

            if (useNvenc)
            {
                _recorder.StartRecording(
                    _overlayCamera,
                    _camera.GetComponent<AudioListener>(),
                    CreateRecordingSettings(
                        directory,
                        $"{baseName}_{_benchmarkMode}_MainCamera",
                        width,
                        height,
                        frameRate,
                        antiAliasingSamples),
                    _preparedTarget);
                _staticRecorder.StartRecording(
                    _staticCamera,
                    _camera.GetComponent<AudioListener>(),
                    CreateRecordingSettings(
                        directory,
                        $"{baseName}_{_benchmarkMode}_StaticCamera",
                        width,
                        height,
                        frameRate,
                        antiAliasingSamples),
                    _staticPreparedTarget);
                while (!_captureStarted)
                {
                    yield return null;
                }
            }
            else
            {
                BeginMeasurement();
            }

            yield return new WaitForSecondsRealtime(CaptureDurationSeconds);
            ReportBenchmarkMeasurement();
            if (useNvenc)
            {
                _recorder.StopRecording();
                _staticRecorder.StopRecording();
            }
            else
            {
                ReleasePreparedTarget();
                if (!Application.isEditor)
                {
                    Application.Quit();
                }
            }
        }

        // Creates one independent output configuration for a synchronized camera recording.
        private static RecordingSettings CreateRecordingSettings(
            string directory,
            string baseName,
            int width,
            int height,
            int frameRate,
            int antiAliasingSamples)
        {
            return new RecordingSettings
            {
                FfmpegPath = Environment.GetEnvironmentVariable("FFMPEG_PATH"),
                TemporaryContainerPath = Path.Combine(directory, $"{baseName}.mkv.tmp"),
                ArchivePath = Path.Combine(directory, $"{baseName}.mkv"),
                KeepIntermediateFile = false,
                GeneratePreviewImage = false,
                OutputPath = Path.Combine(directory, $"{baseName}.mp4"),
                Width = width,
                Height = height,
                MaximumFrameRate = frameRate,
                AntiAliasingSamples = antiAliasingSamples,
                FlipVertically = SystemInfo.graphicsUVStartsAtTop
            };
        }

        // Creates a one-image-per-second PNG sequence matching its associated video output.
        private static PngSequenceSettings CreatePngSequenceSettings(
            string outputDirectory,
            int width,
            int height,
            int antiAliasingSamples,
            double initialDelaySeconds)
        {
            return new PngSequenceSettings
            {
                OutputDirectory = outputDirectory,
                FileNamePrefix = "frame_",
                Width = width,
                Height = height,
                CapturesPerSecond = 1.0,
                InitialDelaySeconds = initialDelaySeconds,
                AntiAliasingSamples = antiAliasingSamples,
                FlipVertically = false
            };
        }

        // Reads a positive integer override while retaining a safe default for missing or invalid values.
        private static int ReadPositiveEnvironmentInteger(string name, int defaultValue)
        {
            string value = Environment.GetEnvironmentVariable(name);
            return int.TryParse(value, out int parsed) && parsed > 0 ? parsed : defaultValue;
        }

        // Marks the instant at which media inputs are connected and capture is active.
        private void HandleCaptureStarted()
        {
            _startedRecorderCount++;
            if (_startedRecorderCount < 2)
            {
                return;
            }

            BeginMeasurement();
            Debug.Log("Cube orbit PNG capture started.");
        }

        // Resets both render counters and starts a common measurement interval.
        private void BeginMeasurement()
        {
            _captureStarted = true;
            _captureStartTime = Time.realtimeSinceStartup;
            _renderedFramesSinceCapture = 0;
            _staticRenderedFramesSinceCapture = 0;
            UpdateDiagnosticText();
        }

        // Writes the average render rate for the selected benchmark interval.
        private void ReportBenchmarkMeasurement()
        {
            float elapsed = Math.Max(0.001f, Time.realtimeSinceStartup - _captureStartTime);
            float mainAverageFps = _renderedFramesSinceCapture / elapsed;
            float staticAverageFps = _staticRenderedFramesSinceCapture / elapsed;
            Debug.Log(
                $"CAPTURE_BENCHMARK mode={_benchmarkMode}; elapsed={elapsed:0.000}; " +
                $"mainFps={mainAverageFps:0.00}; mainFrames={_renderedFramesSinceCapture}; " +
                $"staticFps={staticAverageFps:0.00}; staticFrames={_staticRenderedFramesSinceCapture}");
        }

        // Reports the completed files produced on the Desktop.
        private void HandleRecordingCompleted()
        {
            _completedRecorderCount++;
            if (_completedRecorderCount < 2)
            {
                return;
            }

            float elapsed = Math.Max(0.001f, Time.realtimeSinceStartup - _captureStartTime);
            float mainAverageFps = _renderedFramesSinceCapture / elapsed;
            float staticAverageFps = _staticRenderedFramesSinceCapture / elapsed;
            Debug.Log(
                $"Unity render averages over {elapsed:0.000} s: " +
                $"main camera={mainAverageFps:0.00} FPS ({_renderedFramesSinceCapture} frames), " +
                $"static camera={staticAverageFps:0.00} FPS ({_staticRenderedFramesSinceCapture} frames).");
            Debug.Log("Cube orbit PNG capture completed in Desktop/UnityMediaRecorderExample.");
            ReleasePreparedTarget();
            if (!Application.isEditor)
            {
                Application.Quit();
            }
        }

        // Reports a recording failure through the Unity console.
        private void HandleRecordingFailed(Exception exception)
        {
            Debug.LogException(exception);
            ReleasePreparedTarget();
            if (!Application.isEditor)
            {
                Application.Quit(1);
            }
        }

        // Releases the camera target owned by this example after recording ends.
        private void ReleasePreparedTarget()
        {
            if (_preparedTarget == null)
            {
                return;
            }

            if (_camera != null && _camera.targetTexture == _preparedTarget)
            {
                _camera.targetTexture = null;
            }
            if (_overlayCamera != null && _overlayCamera.targetTexture == _preparedTarget)
            {
                _overlayCamera.targetTexture = null;
            }
            if (_staticCamera != null && _staticCamera.targetTexture == _staticPreparedTarget)
            {
                _staticCamera.targetTexture = null;
            }
            _preparedTarget.Release();
            Destroy(_preparedTarget);
            _preparedTarget = null;
            if (_staticPreparedTarget != null)
            {
                _staticPreparedTarget.Release();
                Destroy(_staticPreparedTarget);
                _staticPreparedTarget = null;
            }
        }
    }

    // Applies temporally reprojected smoothing and motion-vector blur to the example camera.
    [RequireComponent(typeof(Camera))]
    internal sealed class TemporalMotionSmoothing : MonoBehaviour
    {
        private const float HistoryWeight = 0.08f;
        private const float MotionBlurStrength = 0.35f;
        private Material _material;
        private RenderTexture _history;
        private bool _historyValid;

        // Enables the depth and motion-vector textures required by temporal reprojection.
        private void OnEnable()
        {
            Camera cameraComponent = GetComponent<Camera>();
            cameraComponent.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
            Shader shader = Shader.Find("Landoria/ExampleTemporalMotionSmoothing");
            if (shader == null)
            {
                Debug.LogWarning("The temporal motion smoothing shader is unavailable; the example will render without it.");
                return;
            }

            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        // Releases the temporal history and its private material.
        private void OnDisable()
        {
            if (_history != null)
            {
                _history.Release();
                Destroy(_history);
                _history = null;
            }

            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }

            _historyValid = false;
        }

        // Reprojects the previous image and integrates motion blur into the camera output.
        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (_material == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            EnsureHistory(source);
            if (!_historyValid)
            {
                Graphics.Blit(source, destination);
                Graphics.Blit(source, _history);
                _historyValid = true;
                return;
            }

            _material.SetTexture("_HistoryTex", _history);
            _material.SetFloat("_HistoryWeight", HistoryWeight);
            _material.SetFloat("_MotionBlurStrength", MotionBlurStrength);
            Graphics.Blit(source, destination, _material);
            Graphics.Blit(destination, _history);
        }

        // Recreates the temporal history when the capture resolution or format changes.
        private void EnsureHistory(RenderTexture source)
        {
            if (_history != null &&
                _history.width == source.width &&
                _history.height == source.height &&
                _history.format == source.format)
            {
                return;
            }

            if (_history != null)
            {
                _history.Release();
                Destroy(_history);
            }

            _history = new RenderTexture(source.width, source.height, 0, source.format)
            {
                name = "TemporalMotionHistory",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            _history.Create();
            _historyValid = false;
        }
    }
}
