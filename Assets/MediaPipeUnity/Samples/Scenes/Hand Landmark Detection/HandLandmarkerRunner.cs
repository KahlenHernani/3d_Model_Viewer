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
                _tankController.SetPinchConfidence(0f);
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
            float pinchConfidence = 0f;

            if (modeGestureAllowed)
            {
                for (int i = 0; i < result.handLandmarks.Count; i++)
                {
                    // ClassifyHand computes all gesture scores together and applies
                    // mutual-exclusivity suppression so overlapping gestures compete.
                    HandGestureScores scores = ClassifyHand(result.handLandmarks[i]);
                    thumbsUpConfidence = Mathf.Max(thumbsUpConfidence, scores.thumbsUp);
                    openHandConfidence = Mathf.Max(openHandConfidence, scores.openHand);
                    fistConfidence = Mathf.Max(fistConfidence, scores.fist);
                    pinchConfidence = Mathf.Max(pinchConfidence, scores.pinch);
                }
            }

            _tankController.SetPinchConfidence(pinchConfidence);

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

      // ---------------------------------------------------------------------
      // Orientation-invariant landmark helpers.
      //
      // All gesture math below is built from these so that curl / extension are
      // decided by bone geometry (angles + palm-relative lengths) rather than
      // image-space "up" (tip.y < pip.y). That keeps the classifiers stable when
      // the hand is tilted or rotated.
      // ---------------------------------------------------------------------

      private static Vector2 P2(NormalizedLandmark lm)
      {
          return new Vector2(lm.x, lm.y);
      }

      private static Vector3 P3(NormalizedLandmark lm)
      {
          return new Vector3(lm.x, lm.y, lm.z);
      }

      private static float PalmSize(NormalizedLandmarks hand)
      {
          return Vector2.Distance(P2(hand.landmarks[0]), P2(hand.landmarks[9]));
      }

      // 0 = finger straight, 1 = finger fully curled. Based on the angle between
      // the proximal (mcp->pip) and distal (pip->tip) bone vectors, so it does
      // not depend on hand orientation.
      private static float FingerCurl(NormalizedLandmark mcp, NormalizedLandmark pip, NormalizedLandmark tip)
      {
          Vector2 proximal = P2(pip) - P2(mcp);
          Vector2 distal = P2(tip) - P2(pip);
          if (proximal.sqrMagnitude < 1e-8f || distal.sqrMagnitude < 1e-8f)
          {
              return 0f;
          }

          float angle = Vector2.Angle(proximal, distal); // 0 (straight) .. 180 (folded)
          return Mathf.Clamp01(Mathf.InverseLerp(20f, 100f, angle));
      }

      // 0 = tip close to its knuckle (curled/short), 1 = tip far from knuckle
      // (extended), normalized by palm size so it is scale invariant.
      private static float FingerExtension(NormalizedLandmark mcp, NormalizedLandmark tip, float palmSize)
      {
          if (palmSize < 1e-6f)
          {
              return 0f;
          }

          float ratio = Vector2.Distance(P2(tip), P2(mcp)) / palmSize;
          return Mathf.Clamp01(Mathf.InverseLerp(0.5f, 1.0f, ratio));
      }

      // Combined "this finger is sticking out" score: long AND not bent.
      private static float FingerOpenScore(
          NormalizedLandmark mcp,
          NormalizedLandmark pip,
          NormalizedLandmark tip,
          float palmSize)
      {
          return FingerExtension(mcp, tip, palmSize) * (1f - FingerCurl(mcp, pip, tip));
      }

      // 0 = fingertip far from palm center, 1 = tucked into the palm.
      private static float TipCompactness(NormalizedLandmark tip, Vector2 palmCenter, float palmSize)
      {
          if (palmSize < 1e-6f)
          {
              return 0f;
          }

          float d = Vector2.Distance(P2(tip), palmCenter) / palmSize;
          return Mathf.Clamp01(Mathf.InverseLerp(1.1f, 0.5f, d));
      }

      private static Vector2 PalmCenter(NormalizedLandmarks hand)
      {
          return (
              P2(hand.landmarks[0]) +
              P2(hand.landmarks[5]) +
              P2(hand.landmarks[9]) +
              P2(hand.landmarks[13]) +
              P2(hand.landmarks[17])
          ) / 5f;
      }

      // 0 = thumb tucked/curled, 1 = thumb clearly extended (long + straight).
      private static float ThumbExtension(NormalizedLandmarks hand, float palmSize)
      {
          if (palmSize < 1e-6f)
          {
              return 0f;
          }

          var cmc = hand.landmarks[1];
          var mcp = hand.landmarks[2];
          var tip = hand.landmarks[4];

          float length = Vector2.Distance(P2(tip), P2(mcp)) / palmSize;
          float lengthScore = Mathf.Clamp01(Mathf.InverseLerp(0.35f, 0.75f, length));
          float straightness = 1f - FingerCurl(cmc, mcp, tip);
          return Mathf.Clamp01(lengthScore * straightness);
      }

      private static bool IsPinching(NormalizedLandmarks hand)
      {
          return GetPinchConfidence(hand) >= 0.7f;
      }

      private static float GetPinchConfidence(NormalizedLandmarks hand)
      {
          var thumbTip = hand.landmarks[4];
          var indexMcp = hand.landmarks[5];
          var indexPip = hand.landmarks[6];
          var indexDip = hand.landmarks[7];
          var indexTip = hand.landmarks[8];

          float palmSize = PalmSize(hand);
          if (palmSize < 1e-6f)
          {
              return 0f;
          }

          // Use 3D distance (includes z / depth) so the pinch is robust to the
          // thumb and index crossing in front of one another.
          Vector3 thumbPoint = P3(thumbTip);
          float tipDistance = Vector3.Distance(thumbPoint, P3(indexTip));
          float dipDistance = Vector3.Distance(thumbPoint, P3(indexDip));
          float pipDistance = Vector3.Distance(thumbPoint, P3(indexPip));

          // Occlusion fallback: when the index is bent (or the tip is hidden),
          // allow the nearer index joints to stand in for the tip.
          float indexLength = Vector2.Distance(P2(indexTip), P2(indexMcp));
          bool indexBent = indexLength < palmSize * 0.6f;
          float pinchDistance = indexBent
              ? Mathf.Min(tipDistance, Mathf.Min(dipDistance, pipDistance))
              : tipDistance;

          float openDistance = palmSize * 0.85f;
          float closedDistance = palmSize * 0.35f;
          float distanceScore = Mathf.Clamp01(Mathf.InverseLerp(openDistance, closedDistance, pinchDistance));

          // Gate on index extension so a closed fist (thumb resting near the
          // fingers) does not masquerade as a pinch.
          float indexExtended = FingerExtension(indexMcp, indexTip, palmSize);
          return Mathf.Clamp01(distanceScore * Mathf.Lerp(0.35f, 1f, indexExtended));
      }

      private static float GetFistConfidence(NormalizedLandmarks hand)
      {
          float palmSize = PalmSize(hand);
          if (palmSize < 1e-6f)
          {
              return 0f;
          }

          // All four fingers should be bent...
          float curl =
              (
                  FingerCurl(hand.landmarks[5], hand.landmarks[6], hand.landmarks[8]) +
                  FingerCurl(hand.landmarks[9], hand.landmarks[10], hand.landmarks[12]) +
                  FingerCurl(hand.landmarks[13], hand.landmarks[14], hand.landmarks[16]) +
                  FingerCurl(hand.landmarks[17], hand.landmarks[18], hand.landmarks[20])
              ) / 4f;

          // ...and the fingertips should be tucked into the palm. Combining curl
          // with compactness rejects a hand that is merely relaxed/half-open.
          Vector2 palmCenter = PalmCenter(hand);
          float compact =
              (
                  TipCompactness(hand.landmarks[8], palmCenter, palmSize) +
                  TipCompactness(hand.landmarks[12], palmCenter, palmSize) +
                  TipCompactness(hand.landmarks[16], palmCenter, palmSize) +
                  TipCompactness(hand.landmarks[20], palmCenter, palmSize)
              ) / 4f;

          return Mathf.Clamp01(0.6f * curl + 0.4f * compact);
      }

      private static float GetThumbsUpConfidence(NormalizedLandmarks hand)
      {
          float palmSize = PalmSize(hand);
          if (palmSize < 1e-6f)
          {
              return 0f;
          }

          // Thumb extended, the other four fingers curled, and the thumb held
          // clear of the fist. Orientation is irrelevant here, so a sideways or
          // tilted thumbs-up still reads correctly.
          float thumb = ThumbExtension(hand, palmSize);
          float fingersCurled =
              (
                  FingerCurl(hand.landmarks[5], hand.landmarks[6], hand.landmarks[8]) +
                  FingerCurl(hand.landmarks[9], hand.landmarks[10], hand.landmarks[12]) +
                  FingerCurl(hand.landmarks[13], hand.landmarks[14], hand.landmarks[16]) +
                  FingerCurl(hand.landmarks[17], hand.landmarks[18], hand.landmarks[20])
              ) / 4f;

          float separationRatio =
              Vector2.Distance(P2(hand.landmarks[4]), P2(hand.landmarks[5])) / palmSize;
          float separation = Mathf.Clamp01(Mathf.InverseLerp(0.3f, 0.6f, separationRatio));

          return Mathf.Clamp01(
              thumb *
              Mathf.Lerp(0.5f, 1f, fingersCurled) *
              Mathf.Lerp(0.7f, 1f, separation));
      }

      private static float GetOpenHandConfidence(NormalizedLandmarks hand)
      {
          float palmSize = PalmSize(hand);
          if (palmSize < 1e-6f)
          {
              return 0f;
          }

          // All four fingers long and straight. Pinch suppression is applied in
          // ClassifyHand so an open palm and a pinch cannot both read high.
          float openness =
              (
                  FingerOpenScore(hand.landmarks[5], hand.landmarks[6], hand.landmarks[8], palmSize) +
                  FingerOpenScore(hand.landmarks[9], hand.landmarks[10], hand.landmarks[12], palmSize) +
                  FingerOpenScore(hand.landmarks[13], hand.landmarks[14], hand.landmarks[16], palmSize) +
                  FingerOpenScore(hand.landmarks[17], hand.landmarks[18], hand.landmarks[20], palmSize)
              ) / 4f;

          return Mathf.Clamp01(openness);
      }

      private static float GetSidePeaceNavigationConfidence(NormalizedLandmarks hand, out int direction)
      {
          direction = 0;
          float palmSize = PalmSize(hand);
          if (palmSize < 1e-6f)
          {
              return 0f;
          }

          var indexMcp = hand.landmarks[5];
          var indexPip = hand.landmarks[6];
          var indexTip = hand.landmarks[8];
          var middleMcp = hand.landmarks[9];
          var middlePip = hand.landmarks[10];
          var middleTip = hand.landmarks[12];

          Vector2 indexDirection = P2(indexTip) - P2(indexMcp);
          Vector2 middleDirection = P2(middleTip) - P2(middleMcp);
          Vector2 averageDirection = (indexDirection + middleDirection) * 0.5f;

          // Shape gate: index + middle out, ring + pinky tucked. Uses min() so
          // the weakest part of the shape governs the score (forgiving but still
          // requires the whole shape), instead of a boolean count.
          float indexOut = FingerOpenScore(indexMcp, indexPip, indexTip, palmSize);
          float middleOut = FingerOpenScore(middleMcp, middlePip, middleTip, palmSize);
          float ringCurl = FingerCurl(hand.landmarks[13], hand.landmarks[14], hand.landmarks[16]);
          float pinkyCurl = FingerCurl(hand.landmarks[17], hand.landmarks[18], hand.landmarks[20]);
          float shape = Mathf.Min(Mathf.Min(indexOut, middleOut), Mathf.Min(ringCurl, pinkyCurl));

          // Quality: the two fingers must point sideways and stay parallel. This
          // is what makes the gesture a *side* peace rather than a "V" up.
          float indexHoriz = Horizontalness(indexDirection);
          float middleHoriz = Horizontalness(middleDirection);
          float aligned = Mathf.Clamp01(
              Mathf.InverseLerp(0.4f, 0.85f,
                  Vector2.Dot(indexDirection.normalized, middleDirection.normalized)));
          float sameDirection =
              Mathf.Sign(indexDirection.x) == Mathf.Sign(middleDirection.x) ? 1f : 0f;
          float quality = (indexHoriz + middleHoriz + aligned) / 3f;

          float confidence = Mathf.Clamp01(shape * Mathf.Lerp(0.5f, 1f, quality) * sameDirection);

          if (Mathf.Abs(averageDirection.x) > 1e-4f)
          {
              direction = averageDirection.x > 0f ? 1 : -1;
          }

          return confidence;
      }

      // 0 = the vector points mostly up/down, 1 = mostly left/right.
      private static float Horizontalness(Vector2 v)
      {
          float ratio = Mathf.Abs(v.x) / (Mathf.Abs(v.y) + 1e-5f);
          return Mathf.Clamp01(Mathf.InverseLerp(0.7f, 1.6f, ratio));
      }

      // Bundle of the suppressed per-hand gesture scores. Suppression makes the
      // gestures compete so that only one reads high at a time.
      private struct HandGestureScores
      {
          public float pinch;
          public float fist;
          public float openHand;
          public float thumbsUp;
          public float nav;
          public int navDirection;
      }

      private static HandGestureScores ClassifyHand(NormalizedLandmarks hand)
      {
          HandGestureScores s = new HandGestureScores();

          float pinchRaw = GetPinchConfidence(hand);
          float fistRaw = GetFistConfidence(hand);
          float openRaw = GetOpenHandConfidence(hand);
          float thumbsUpRaw = GetThumbsUpConfidence(hand);
          float navRaw = GetSidePeaceNavigationConfidence(hand, out s.navDirection);

          // Mutual exclusivity: overlapping gestures suppress one another so a
          // single frame yields one clear winner instead of several near-ties.
          //  - a pinch and an open palm share the "fingers out" look
          //  - a thumbs-up and a side-peace are both "fist + something extended",
          //    which is exactly what a fist looks like too
          s.pinch = pinchRaw * (1f - 0.5f * openRaw);
          s.openHand = openRaw * (1f - pinchRaw) * (1f - 0.5f * thumbsUpRaw);
          s.thumbsUp = thumbsUpRaw * (1f - navRaw);
          s.nav = navRaw;
          s.fist = fistRaw * (1f - thumbsUpRaw) * (1f - navRaw);

          return s;
      }

    }
}
