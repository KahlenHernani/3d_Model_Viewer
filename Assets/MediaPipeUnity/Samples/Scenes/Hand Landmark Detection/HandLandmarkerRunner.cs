// Copyright (c) 2023 homuler
//
// Use of this source code is governed by an MIT-style
// license that can be found in the LICENSE file or at
// https://opensource.org/licenses/MIT.

using System.Collections;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Tasks.Components.Containers;
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
            {
                _tankController.SetGestureConfidences(0f, 0f, 0f, 0, 0f);
                previousHandSpread = -1f;
                return;
            }

            var hand = result.handLandmarks[0];

            // Wrist
            var wrist = hand.landmarks[0];

            Vector2 wristPos = new Vector2(
                wrist.x,
                wrist.y
            );

            bool isPinching = IsPinching(hand);

            // ONLY ROTATE WHEN EXACTLY ONE HAND IS DETECTED
            if (result.handLandmarks.Count == 1)
            {
                _tankController.UpdateRotation(
                    wristPos,
                    isPinching
                );
            }

            bool singleHandDetected = result.handLandmarks.Count == 1;
            bool twoHandPinching =
                result.handLandmarks.Count >= 2 &&
                IsPinching(result.handLandmarks[0]) &&
                IsPinching(result.handLandmarks[1]);
            bool modeGestureAllowed =
                result.handLandmarks.Count >= 1 &&
                !twoHandPinching;
            float thumbsUpConfidence = 0f;
            float openHandConfidence = 0f;
            float fistConfidence = 0f;

            if (modeGestureAllowed)
            {
                for (int i = 0; i < result.handLandmarks.Count; i++)
                {
                    NormalizedLandmarks candidateHand = result.handLandmarks[i];
                    thumbsUpConfidence = Mathf.Max(
                        thumbsUpConfidence,
                        GetThumbsUpConfidence(candidateHand)
                    );
                    openHandConfidence = Mathf.Max(
                        openHandConfidence,
                        GetOpenHandConfidence(candidateHand)
                    );
                    fistConfidence = Mathf.Max(
                        fistConfidence,
                        GetFistConfidence(candidateHand)
                    );
                }
            }

            bool groupNavigationAllowed =
                singleHandDetected &&
                _tankController.currentMode == TankHandController.InteractionMode.Group;
            int groupNavigationDirection = 0;
            float groupNavigationConfidence = groupNavigationAllowed
                ? GetSidePeaceNavigationConfidence(hand, out groupNavigationDirection)
                : 0f;

            _tankController.SetGestureConfidences(
                fistConfidence,
                thumbsUpConfidence,
                openHandConfidence,
                groupNavigationDirection,
                groupNavigationConfidence
            );

            // TWO HAND CONTROL
            if (result.handLandmarks.Count >= 2)
            {
                var hand1 = result.handLandmarks[0];
                var hand2 = result.handLandmarks[1];

                // ONLY ZOOM/EXPLODE WHEN BOTH HANDS ARE PINCHING
                if (twoHandPinching)
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
                    if (Mathf.Abs(deltaSpread) < 0.0005f)
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
            else
            {
                previousHandSpread = -1f;
            }
        }

      private static bool IsPinching(NormalizedLandmarks hand)
      {
          return GetPinchConfidence(hand) >= 0.7f;
      }

      private static float GetPinchConfidence(NormalizedLandmarks hand)
      {
          var wrist = hand.landmarks[0];
          var thumbTip = hand.landmarks[4];
          var indexMcp = hand.landmarks[5];
          var indexPip = hand.landmarks[6];
          var indexDip = hand.landmarks[7];
          var indexTip = hand.landmarks[8];
          var middleMcp = hand.landmarks[9];

          float palmSize = Vector2.Distance(
              new Vector2(wrist.x, wrist.y),
              new Vector2(middleMcp.x, middleMcp.y)
          );
          float indexLength = Vector2.Distance(
              new Vector2(indexTip.x, indexTip.y),
              new Vector2(indexMcp.x, indexMcp.y)
          );
          Vector2 thumbPoint = new Vector2(thumbTip.x, thumbTip.y);
          float tipDistance = Vector2.Distance(
              thumbPoint,
              new Vector2(indexTip.x, indexTip.y)
          );
          float dipDistance = Vector2.Distance(
              thumbPoint,
              new Vector2(indexDip.x, indexDip.y)
          );
          float pipDistance = Vector2.Distance(
              thumbPoint,
              new Vector2(indexPip.x, indexPip.y)
          );
          bool indexBentEnoughForJointFallback = indexLength < palmSize * 0.6f;
          float jointFallbackDistance = Mathf.Min(dipDistance, pipDistance);
          float pinchDistance = indexBentEnoughForJointFallback
              ? Mathf.Min(tipDistance, jointFallbackDistance)
              : tipDistance;
          float openDistance = Mathf.Max(0.09f, palmSize * 0.85f);
          float closedDistance = Mathf.Max(0.04f, palmSize * 0.42f);
          float distanceScore = Mathf.InverseLerp(openDistance, closedDistance, pinchDistance);
          float indexExtendedScore = Mathf.InverseLerp(palmSize * 0.15f, palmSize * 0.45f, indexLength);

          return Mathf.Clamp01(distanceScore * indexExtendedScore);
      }

      private static float GetFistConfidence(NormalizedLandmarks hand)
      {
          int sidePeaceDirection;
          if (GetSidePeaceNavigationConfidence(hand, out sidePeaceDirection) >= 0.72f)
          {
              return 0f;
          }

          var wrist = hand.landmarks[0];
          var thumbMcp = hand.landmarks[2];
          var thumbTip = hand.landmarks[4];
          var indexMcp = hand.landmarks[5];
          var indexPip = hand.landmarks[6];
          var indexTip = hand.landmarks[8];
          var middleMcp = hand.landmarks[9];
          var middlePip = hand.landmarks[10];
          var middleTip = hand.landmarks[12];
          var ringMcp = hand.landmarks[13];
          var ringPip = hand.landmarks[14];
          var ringTip = hand.landmarks[16];
          var pinkyMcp = hand.landmarks[17];
          var pinkyPip = hand.landmarks[18];
          var pinkyTip = hand.landmarks[20];

          float palmSize = Vector2.Distance(
              new Vector2(wrist.x, wrist.y),
              new Vector2(middleMcp.x, middleMcp.y)
          );

          Vector2 wristPoint = new Vector2(wrist.x, wrist.y);
          Vector2 palmCenter =
              (
                  wristPoint +
                  new Vector2(indexMcp.x, indexMcp.y) +
                  new Vector2(middleMcp.x, middleMcp.y) +
                  new Vector2(ringMcp.x, ringMcp.y) +
                  new Vector2(pinkyMcp.x, pinkyMcp.y)
              ) / 5f;
          float curlTolerance = palmSize * 0.28f;
          float compactFingerLength = palmSize * 0.82f;
          float compactPalmDistance = palmSize * 0.95f;
          bool indexCurled =
              indexTip.y > indexPip.y - curlTolerance ||
              Vector2.Distance(new Vector2(indexTip.x, indexTip.y), wristPoint) <
              Vector2.Distance(new Vector2(indexPip.x, indexPip.y), wristPoint) + curlTolerance ||
              Vector2.Distance(new Vector2(indexTip.x, indexTip.y), new Vector2(indexMcp.x, indexMcp.y)) < compactFingerLength ||
              Vector2.Distance(new Vector2(indexTip.x, indexTip.y), palmCenter) < compactPalmDistance;
          bool middleCurled =
              middleTip.y > middlePip.y - curlTolerance ||
              Vector2.Distance(new Vector2(middleTip.x, middleTip.y), wristPoint) <
              Vector2.Distance(new Vector2(middlePip.x, middlePip.y), wristPoint) + curlTolerance ||
              Vector2.Distance(new Vector2(middleTip.x, middleTip.y), new Vector2(middleMcp.x, middleMcp.y)) < compactFingerLength ||
              Vector2.Distance(new Vector2(middleTip.x, middleTip.y), palmCenter) < compactPalmDistance;
          bool ringCurled =
              ringTip.y > ringPip.y - curlTolerance ||
              Vector2.Distance(new Vector2(ringTip.x, ringTip.y), wristPoint) <
              Vector2.Distance(new Vector2(ringPip.x, ringPip.y), wristPoint) + curlTolerance ||
              Vector2.Distance(new Vector2(ringTip.x, ringTip.y), new Vector2(ringMcp.x, ringMcp.y)) < compactFingerLength ||
              Vector2.Distance(new Vector2(ringTip.x, ringTip.y), palmCenter) < compactPalmDistance;
          bool pinkyCurled =
              pinkyTip.y > pinkyPip.y - curlTolerance ||
              Vector2.Distance(new Vector2(pinkyTip.x, pinkyTip.y), wristPoint) <
              Vector2.Distance(new Vector2(pinkyPip.x, pinkyPip.y), wristPoint) + curlTolerance ||
              Vector2.Distance(new Vector2(pinkyTip.x, pinkyTip.y), new Vector2(pinkyMcp.x, pinkyMcp.y)) < compactFingerLength ||
              Vector2.Distance(new Vector2(pinkyTip.x, pinkyTip.y), palmCenter) < compactPalmDistance;
          int curledFingerCount =
              (indexCurled ? 1 : 0) +
              (middleCurled ? 1 : 0) +
              (ringCurled ? 1 : 0) +
              (pinkyCurled ? 1 : 0);

          Vector2 thumbDirection = new Vector2(
              thumbTip.x - thumbMcp.x,
              thumbTip.y - thumbMcp.y
          );
          bool thumbClearlyRaised =
              thumbTip.y < wrist.y - palmSize * 0.28f &&
              thumbDirection.y < -palmSize * 0.4f &&
              Mathf.Abs(thumbDirection.y) > Mathf.Abs(thumbDirection.x) * 1.25f;

          float confidence = Mathf.Clamp01(curledFingerCount / 4f);
          if (thumbClearlyRaised)
          {
              confidence = Mathf.Min(confidence, 0.25f);
          }

          return confidence;
      }

      private static float GetThumbsUpConfidence(NormalizedLandmarks hand)
      {
          var wrist = hand.landmarks[0];
          var thumbMcp = hand.landmarks[2];
          var thumbTip = hand.landmarks[4];
          var indexMcp = hand.landmarks[5];
          var indexPip = hand.landmarks[6];
          var indexTip = hand.landmarks[8];
          var middleMcp = hand.landmarks[9];
          var middlePip = hand.landmarks[10];
          var middleTip = hand.landmarks[12];
          var ringPip = hand.landmarks[14];
          var ringTip = hand.landmarks[16];
          var pinkyPip = hand.landmarks[18];
          var pinkyTip = hand.landmarks[20];

          float palmSize = Vector2.Distance(
              new Vector2(wrist.x, wrist.y),
              new Vector2(middleMcp.x, middleMcp.y)
          );
          Vector2 thumbDirection = new Vector2(
              thumbTip.x - thumbMcp.x,
              thumbTip.y - thumbMcp.y
          );

          bool thumbRaised =
              thumbTip.y < thumbMcp.y - palmSize * 0.12f &&
              thumbTip.y < wrist.y - palmSize * 0.12f;
          bool thumbMostlyVertical =
              thumbDirection.y < -palmSize * 0.22f &&
              Mathf.Abs(thumbDirection.y) > Mathf.Abs(thumbDirection.x) * 0.8f;

          float curlTolerance = palmSize * 0.14f;
          int curledFingerCount =
              (indexTip.y > indexPip.y - curlTolerance ? 1 : 0) +
              (middleTip.y > middlePip.y - curlTolerance ? 1 : 0) +
              (ringTip.y > ringPip.y - curlTolerance ? 1 : 0) +
              (pinkyTip.y > pinkyPip.y - curlTolerance ? 1 : 0);

          bool thumbSeparatedFromIndex =
              Vector2.Distance(
                  new Vector2(thumbTip.x, thumbTip.y),
                  new Vector2(indexMcp.x, indexMcp.y)
              ) > palmSize * 0.45f;

          float confidence = 0f;
          confidence += thumbRaised ? 0.35f : 0f;
          confidence += thumbMostlyVertical ? 0.25f : 0f;
          confidence += Mathf.Clamp01(curledFingerCount / 4f) * 0.25f;
          confidence += thumbSeparatedFromIndex ? 0.15f : 0f;

          return Mathf.Clamp01(confidence);
      }

      private static float GetOpenHandConfidence(NormalizedLandmarks hand)
      {
          if (IsPinching(hand))
          {
              return 0f;
          }

          var indexPip = hand.landmarks[6];
          var indexTip = hand.landmarks[8];
          var middlePip = hand.landmarks[10];
          var middleTip = hand.landmarks[12];
          var ringPip = hand.landmarks[14];
          var ringTip = hand.landmarks[16];
          var pinkyPip = hand.landmarks[18];
          var pinkyTip = hand.landmarks[20];

          int extendedFingerCount =
              (indexTip.y < indexPip.y ? 1 : 0) +
              (middleTip.y < middlePip.y ? 1 : 0) +
              (ringTip.y < ringPip.y ? 1 : 0) +
              (pinkyTip.y < pinkyPip.y ? 1 : 0);

          return Mathf.Clamp01(extendedFingerCount / 4f);
      }

      private static float GetSidePeaceNavigationConfidence(NormalizedLandmarks hand, out int direction)
      {
          direction = 0;
          var wrist = hand.landmarks[0];
          var thumbTip = hand.landmarks[4];

          var indexMcp = hand.landmarks[5];
          var indexTip = hand.landmarks[8];

          var middleMcp = hand.landmarks[9];
          var middleTip = hand.landmarks[12];
          var ringMcp = hand.landmarks[13];
          var ringPip = hand.landmarks[14];
          var ringTip = hand.landmarks[16];
          var pinkyMcp = hand.landmarks[17];
          var pinkyPip = hand.landmarks[18];
          var pinkyTip = hand.landmarks[20];

          Vector2 indexDirection = new Vector2(
              indexTip.x - indexMcp.x,
              indexTip.y - indexMcp.y
          );
          Vector2 middleDirection = new Vector2(
              middleTip.x - middleMcp.x,
              middleTip.y - middleMcp.y
          );
          Vector2 averageDirection = (indexDirection + middleDirection) * 0.5f;

          float indexLength = Vector2.Distance(
              new Vector2(indexTip.x, indexTip.y),
              new Vector2(indexMcp.x, indexMcp.y)
          );

          float middleLength = Vector2.Distance(
              new Vector2(middleTip.x, middleTip.y),
              new Vector2(middleMcp.x, middleMcp.y)
          );

          float ringLength = Vector2.Distance(
              new Vector2(ringTip.x, ringTip.y),
              new Vector2(ringMcp.x, ringMcp.y)
          );

          float pinkyLength = Vector2.Distance(
              new Vector2(pinkyTip.x, pinkyTip.y),
              new Vector2(pinkyMcp.x, pinkyMcp.y)
          );

          float palmSize = Vector2.Distance(
              new Vector2(wrist.x, wrist.y),
              new Vector2(middleMcp.x, middleMcp.y)
          );
          float minimumExtendedLength = Mathf.Max(0.08f, palmSize * 0.55f);
          float maximumRelaxedLength = Mathf.Max(indexLength, middleLength) * 0.85f;
          float relaxedCurlTolerance = Mathf.Max(0.02f, palmSize * 0.12f);
          float minimumPinchDistance = Mathf.Max(0.08f, palmSize * 0.45f);

          bool indexExtended = indexLength > minimumExtendedLength;
          bool middleExtended = middleLength > minimumExtendedLength;
          bool indexMostlyHorizontal =
              Mathf.Abs(indexDirection.x) > Mathf.Abs(indexDirection.y) * 1.45f;
          bool middleMostlyHorizontal =
              Mathf.Abs(middleDirection.x) > Mathf.Abs(middleDirection.y) * 1.45f;
          bool fingersPointSameDirection =
              Mathf.Sign(indexDirection.x) == Mathf.Sign(middleDirection.x);
          bool fingersAligned =
              Vector2.Dot(indexDirection.normalized, middleDirection.normalized) > 0.72f;
          bool ringRelaxed =
              ringLength < maximumRelaxedLength ||
              ringTip.y > ringPip.y - relaxedCurlTolerance;
          bool pinkyRelaxed =
              pinkyLength < maximumRelaxedLength ||
              pinkyTip.y > pinkyPip.y - relaxedCurlTolerance;
          bool notPinching = Vector2.Distance(
              new Vector2(thumbTip.x, thumbTip.y),
              new Vector2(indexTip.x, indexTip.y)
          ) > minimumPinchDistance;

          float confidence = 0f;
          confidence += indexExtended ? 0.15f : 0f;
          confidence += middleExtended ? 0.15f : 0f;
          confidence += indexMostlyHorizontal ? 0.15f : 0f;
          confidence += middleMostlyHorizontal ? 0.15f : 0f;
          confidence += fingersPointSameDirection ? 0.1f : 0f;
          confidence += fingersAligned ? 0.1f : 0f;
          confidence += ringRelaxed ? 0.08f : 0f;
          confidence += pinkyRelaxed ? 0.08f : 0f;
          confidence += notPinching ? 0.04f : 0f;

          confidence = Mathf.Clamp01(confidence);
          if (Mathf.Abs(averageDirection.x) > 0.0001f)
          {
              direction = averageDirection.x > 0f ? 1 : -1;
          }

          return confidence;
      }

    }
}
