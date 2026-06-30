// GestureRecognizerRunner.cs
// Drives MediaPipe GestureRecognizer from webcam and feeds GestureController.
// Requires "gesture_recognizer.bytes" in Assets/StreamingAssets/.

using System.Collections;
using Mediapipe.Tasks.Vision.GestureRecognizer;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mediapipe.Unity.Sample.HandLandmarkDetection
{
    public class GestureRecognizerRunner : VisionTaskApiRunner<GestureRecognizer>
    {
        [Tooltip("The GestureController that will receive gesture events.")]
        [SerializeField] private GestureController _gestureController;

        [Header("Recognizer Settings")]
        [SerializeField] private int _numHands = 2;
        [SerializeField] private float _minHandDetectionConfidence = 0.5f;
        [SerializeField] private float _minHandPresenceConfidence = 0.5f;
        [SerializeField] private float _minTrackingConfidence = 0.5f;

        [Header("Pinch Detection")]
        [SerializeField] private float _pinchThreshold = 0.05f;

        private Experimental.TextureFramePool _textureFramePool;

        public override void Stop()
        {
            base.Stop();
            _textureFramePool?.Dispose();
            _textureFramePool = null;
        }

        protected override IEnumerator Run()
        {
            Debug.Log("[GestureRecognizerRunner] Starting");

            yield return AssetLoader.PrepareAssetAsync("gesture_recognizer.bytes");

            var options = new GestureRecognizerOptions(
                new Tasks.Core.BaseOptions(
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                    Tasks.Core.BaseOptions.Delegate.CPU,
#else
                    Tasks.Core.BaseOptions.Delegate.GPU,
#endif
                    modelAssetPath: "gesture_recognizer.bytes"
                ),
                runningMode: Tasks.Vision.Core.RunningMode.LIVE_STREAM,
                numHands: _numHands,
                minHandDetectionConfidence: _minHandDetectionConfidence,
                minHandPresenceConfidence: _minHandPresenceConfidence,
                minTrackingConfidence: _minTrackingConfidence,
                resultCallback: OnGestureRecognitionOutput
            );

            taskApi = GestureRecognizer.CreateFromOptions(options, GpuManager.GpuResources);

            var imageSource = ImageSourceProvider.ImageSource;
            yield return imageSource.Play();

            if (!imageSource.isPrepared)
            {
                Debug.LogError("[GestureRecognizerRunner] Failed to start ImageSource.");
                yield break;
            }

            _textureFramePool = new Experimental.TextureFramePool(
                imageSource.textureWidth, imageSource.textureHeight, TextureFormat.RGBA32, 10);

            screen.Initialize(imageSource);

            var transformationOptions = imageSource.GetTransformationOptions();
            var flipHorizontally = transformationOptions.flipHorizontally;
            var flipVertically = transformationOptions.flipVertically;
            var imageProcessingOptions = new Tasks.Vision.Core.ImageProcessingOptions(
                rotationDegrees: (int)transformationOptions.rotationAngle);

            AsyncGPUReadbackRequest req = default;
            var waitUntilReqDone = new WaitUntil(() => req.done);
            var waitForEndOfFrame = new WaitForEndOfFrame();

            var canUseGpuImage =
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 &&
                GpuManager.GpuResources != null;

            using var glContext = canUseGpuImage ? GpuManager.GetGlContext() : null;

            while (true)
            {
                if (isPaused)
                    yield return new WaitWhile(() => isPaused);

                if (!_textureFramePool.TryGetTextureFrame(out var textureFrame))
                {
                    yield return new WaitForEndOfFrame();
                    continue;
                }

                Image image;
                if (canUseGpuImage)
                {
                    textureFrame.ReadTextureOnGPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
                    image = textureFrame.BuildGPUImage(glContext);
                    yield return waitForEndOfFrame;
                }
                else
                {
                    req = textureFrame.ReadTextureAsync(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
                    yield return waitUntilReqDone;

                    if (req.hasError)
                    {
                        Debug.LogWarning("[GestureRecognizerRunner] Failed to read texture.");
                        continue;
                    }

                    image = textureFrame.BuildCPUImage();
                    textureFrame.Release();
                }

                taskApi.RecognizeAsync(image, GetCurrentTimestampMillisec(), imageProcessingOptions);
            }
        }

        private void OnGestureRecognitionOutput(
            GestureRecognizerResult result,
            Image image,
            long timestamp)
        {
            string gestureName = "None";
            Vector2 wristPos = Vector2.zero;
            bool isPinching = false;
            Vector2 secondWristPos = Vector2.zero;
            bool secondIsPinching = false;
            bool secondHandPresent = false;

            if (result.gestures != null && result.gestures.Count > 0 &&
                result.handLandmarks != null && result.handLandmarks.Count > 0)
            {
                var primaryGestures = result.gestures[0].categories;
                if (primaryGestures != null && primaryGestures.Count > 0)
                    gestureName = primaryGestures[0].categoryName;

                var hand0 = result.handLandmarks[0];
                wristPos = new Vector2(hand0.landmarks[0].x, hand0.landmarks[0].y);

                var thumb0 = hand0.landmarks[4];
                var index0 = hand0.landmarks[8];
                isPinching = Vector2.Distance(
                    new Vector2(thumb0.x, thumb0.y),
                    new Vector2(index0.x, index0.y)) < _pinchThreshold;

                if (result.handLandmarks.Count >= 2)
                {
                    secondHandPresent = true;
                    var hand1 = result.handLandmarks[1];
                    secondWristPos = new Vector2(hand1.landmarks[0].x, hand1.landmarks[0].y);

                    var thumb1 = hand1.landmarks[4];
                    var index1 = hand1.landmarks[8];
                    secondIsPinching = Vector2.Distance(
                        new Vector2(thumb1.x, thumb1.y),
                        new Vector2(index1.x, index1.y)) < _pinchThreshold;
                }
            }

            string g = gestureName;
            Vector2 w = wristPos;
            bool p = isPinching;
            Vector2 sw = secondWristPos;
            bool sp = secondIsPinching;
            bool shp = secondHandPresent;

            UnityEngine.WSA.Application.InvokeOnAppThread(() =>
            {
                if (_gestureController != null)
                    _gestureController.ProcessGesture(g, w, p, sw, sp, shp);
            }, false);
        }
    }
}