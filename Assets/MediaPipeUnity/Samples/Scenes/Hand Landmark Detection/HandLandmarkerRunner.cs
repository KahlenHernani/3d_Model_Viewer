// Copyright (c) 2023 homuler
//
// Use of this source code is governed by an MIT-style
// license that can be found in the LICENSE file or at
// https://opensource.org/licenses/MIT.

using System.Collections;
using Mediapipe.Tasks.Vision.HandLandmarker;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mediapipe.Unity.Sample.HandLandmarkDetection
{
  public class HandLandmarkerRunner : VisionTaskApiRunner<HandLandmarker>
  {

       private float previousHandSpread = -1f;
        [SerializeField] private HandLandmarkerResultAnnotationController _handLandmarkerResultAnnotationController;

        [SerializeField]
        private TankHandController _tankController;

        private Experimental.TextureFramePool _textureFramePool;

    public readonly HandLandmarkDetectionConfig config = new HandLandmarkDetectionConfig();

    public override void Stop()
    {
      base.Stop();
      _textureFramePool?.Dispose();
      _textureFramePool = null;
    }

    protected override IEnumerator Run()
    {
      Debug.Log($"Delegate = {config.Delegate}");
      Debug.Log($"Image Read Mode = {config.ImageReadMode}");
      Debug.Log($"Running Mode = {config.RunningMode}");
      Debug.Log($"NumHands = {config.NumHands}");
      Debug.Log($"MinHandDetectionConfidence = {config.MinHandDetectionConfidence}");
      Debug.Log($"MinHandPresenceConfidence = {config.MinHandPresenceConfidence}");
      Debug.Log($"MinTrackingConfidence = {config.MinTrackingConfidence}");

      yield return AssetLoader.PrepareAssetAsync(config.ModelPath);

      var options = config.GetHandLandmarkerOptions(config.RunningMode == Tasks.Vision.Core.RunningMode.LIVE_STREAM ? OnHandLandmarkDetectionOutput : null);
      taskApi = HandLandmarker.CreateFromOptions(options, GpuManager.GpuResources);
      var imageSource = ImageSourceProvider.ImageSource;

      yield return imageSource.Play();

      if (!imageSource.isPrepared)
      {
        Debug.LogError("Failed to start ImageSource, exiting...");
        yield break;
      }

      // Use RGBA32 as the input format.
      // TODO: When using GpuBuffer, MediaPipe assumes that the input format is BGRA, so maybe the following code needs to be fixed.
      _textureFramePool = new Experimental.TextureFramePool(imageSource.textureWidth, imageSource.textureHeight, TextureFormat.RGBA32, 10);

      // NOTE: The screen will be resized later, keeping the aspect ratio.
      screen.Initialize(imageSource);

      SetupAnnotationController(_handLandmarkerResultAnnotationController, imageSource);

      var transformationOptions = imageSource.GetTransformationOptions();
      var flipHorizontally = transformationOptions.flipHorizontally;
      var flipVertically = transformationOptions.flipVertically;
      var imageProcessingOptions = new Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: (int)transformationOptions.rotationAngle);

      AsyncGPUReadbackRequest req = default;
      var waitUntilReqDone = new WaitUntil(() => req.done);
      var waitForEndOfFrame = new WaitForEndOfFrame();
      var result = HandLandmarkerResult.Alloc(options.numHands);

      // NOTE: we can share the GL context of the render thread with MediaPipe (for now, only on Android)
      var canUseGpuImage = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 && GpuManager.GpuResources != null;
      using var glContext = canUseGpuImage ? GpuManager.GetGlContext() : null;

      while (true)
      {
        if (isPaused)
        {
          yield return new WaitWhile(() => isPaused);
        }

        if (!_textureFramePool.TryGetTextureFrame(out var textureFrame))
        {
          yield return new WaitForEndOfFrame();
          continue;
        }

        // Build the input Image
        Image image;
        switch (config.ImageReadMode)
        {
          case ImageReadMode.GPU:
            if (!canUseGpuImage)
            {
              throw new System.Exception("ImageReadMode.GPU is not supported");
            }
            textureFrame.ReadTextureOnGPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            image = textureFrame.BuildGPUImage(glContext);
            // TODO: Currently we wait here for one frame to make sure the texture is fully copied to the TextureFrame before sending it to MediaPipe.
            // This usually works but is not guaranteed. Find a proper way to do this. See: https://github.com/homuler/MediaPipeUnityPlugin/pull/1311
            yield return waitForEndOfFrame;
            break;
          case ImageReadMode.CPU:
            yield return waitForEndOfFrame;
            textureFrame.ReadTextureOnCPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            image = textureFrame.BuildCPUImage();
            textureFrame.Release();
            break;
          case ImageReadMode.CPUAsync:
          default:
            req = textureFrame.ReadTextureAsync(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            yield return waitUntilReqDone;

            if (req.hasError)
            {
              Debug.LogWarning($"Failed to read texture from the image source");
              continue;
            }
            image = textureFrame.BuildCPUImage();
            textureFrame.Release();
            break;
        }

        switch (taskApi.runningMode)
        {
          case Tasks.Vision.Core.RunningMode.IMAGE:
            if (taskApi.TryDetect(image, imageProcessingOptions, ref result))
            {
              _handLandmarkerResultAnnotationController.DrawNow(result);
            }
            else
            {
              _handLandmarkerResultAnnotationController.DrawNow(default);
            }
            break;
          case Tasks.Vision.Core.RunningMode.VIDEO:
            if (taskApi.TryDetectForVideo(image, GetCurrentTimestampMillisec(), imageProcessingOptions, ref result))
            {
              _handLandmarkerResultAnnotationController.DrawNow(result);
            }
            else
            {
              _handLandmarkerResultAnnotationController.DrawNow(default);
            }
            break;
          case Tasks.Vision.Core.RunningMode.LIVE_STREAM:
            taskApi.DetectAsync(image, GetCurrentTimestampMillisec(), imageProcessingOptions);
            break;
        }
      }
    }

        private void OnHandLandmarkDetectionOutput(
     HandLandmarkerResult result,
     Image image,
     long timestamp)
        {
            _handLandmarkerResultAnnotationController.DrawLater(result);

            if (_tankController == null)
                return;

            if (result.handLandmarks == null || result.handLandmarks.Count == 0)
                return;

            var hand = result.handLandmarks[0];

            // Wrist
            var wrist = hand.landmarks[0];

            // Thumb tip
            var thumb = hand.landmarks[4];

            // Index tip
            var index = hand.landmarks[8];

            Vector2 wristPos = new Vector2(
                wrist.x,
                wrist.y
            );

            float pinchDistance = Vector2.Distance(
                new Vector2(thumb.x, thumb.y),
                new Vector2(index.x, index.y)
            );

            bool isPinching = pinchDistance < 0.05f;

            // ONLY ROTATE WHEN EXACTLY ONE HAND IS DETECTED
            if (result.handLandmarks.Count == 1)
            {
                _tankController.UpdateRotation(
                    wristPos,
                    isPinching
                );
            }

            // Fist detection
            var indexKnuckle = hand.landmarks[6];
            var middleTip = hand.landmarks[12];
            var middleKnuckle = hand.landmarks[10];
            var ringTip = hand.landmarks[16];
            var ringKnuckle = hand.landmarks[14];
            var pinkyTip = hand.landmarks[20];
            var pinkyKnuckle = hand.landmarks[18];

            bool fistDetected =
                index.y > indexKnuckle.y &&
                middleTip.y > middleKnuckle.y &&
                ringTip.y > ringKnuckle.y &&
                pinkyTip.y > pinkyKnuckle.y;

            _tankController.UpdateFist(fistDetected);

            // TWO HAND CONTROL
            if (result.handLandmarks.Count >= 2)
            {
                var hand1 = result.handLandmarks[0];
                var hand2 = result.handLandmarks[1];

                // Hand 1 pinch
                var thumb1 = hand1.landmarks[4];
                var index1 = hand1.landmarks[8];

                float pinch1 = Vector2.Distance(
                    new Vector2(thumb1.x, thumb1.y),
                    new Vector2(index1.x, index1.y)
                );

                // Hand 2 pinch
                var thumb2 = hand2.landmarks[4];
                var index2 = hand2.landmarks[8];

                float pinch2 = Vector2.Distance(
                    new Vector2(thumb2.x, thumb2.y),
                    new Vector2(index2.x, index2.y)
                );

                bool hand1Pinching = pinch1 < 0.05f;
                bool hand2Pinching = pinch2 < 0.05f;

                // ONLY ZOOM/EXPLODE WHEN BOTH HANDS ARE PINCHING
                if (hand1Pinching && hand2Pinching)
                {
                    var wrist1 = hand1.landmarks[0];
                    var wrist2 = hand2.landmarks[0];

                    float spread = Vector2.Distance(
                        new Vector2(wrist1.x, wrist1.y),
                        new Vector2(wrist2.x, wrist2.y)
                    );

                    if (previousHandSpread < 0f)
                    {
                        previousHandSpread = spread;
                        return;
                    }

                    float deltaSpread =
                        spread - previousHandSpread;

                    previousHandSpread = spread;
                    if (Mathf.Abs(deltaSpread) < 0.001f)
                    {
                        return;
                    }
                    _tankController.UpdateDistanceDelta(
                        deltaSpread
                    );
                }
                else
                {
                    previousHandSpread = -1f;
                }
            }
        }
    }
}
