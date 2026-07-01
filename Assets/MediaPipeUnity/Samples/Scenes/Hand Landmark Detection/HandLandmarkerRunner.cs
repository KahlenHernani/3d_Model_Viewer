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
// Disambiguate: a bare "NormalizedLandmark" resolves to Mediapipe.NormalizedLandmark
// inside this namespace, but hand.landmarks[i] is the Tasks container type.
using NLandmark = Mediapipe.Tasks.Components.Containers.NormalizedLandmark;

namespace Mediapipe.Unity.Sample.HandLandmarkDetection
{
  public class HandLandmarkerRunner : VisionTaskApiRunner<HandLandmarker>
  {

    private float previousHandSpread = -1f;

    // A deliberate two-hand zoom changes the wrist spread gradually. Pulling a
    // hand toward/out of frame produces a large single-frame jump; anything above
    // this (in normalized image units) is treated as a hand leaving, not a zoom.
    private const float MaxZoomSpreadDeltaPerFrame = 0.06f;
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
                IsPinching(result.handLandmarks[0], TwoHandPinchTriggerConfidence) &&
                IsPinching(result.handLandmarks[1], TwoHandPinchTriggerConfidence);
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
                    // Ignore large jumps: this is a hand being pulled away (e.g. to
                    // switch to one-hand rotation), not an intentional zoom.
                    if (Mathf.Abs(deltaSpread) > MaxZoomSpreadDeltaPerFrame)
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

      private static Vector2 P2(NLandmark lm)
      {
          return new Vector2(lm.x, lm.y);
      }

      private static Vector3 P3(NLandmark lm)
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
      private static float FingerCurl(NLandmark mcp, NLandmark pip, NLandmark tip)
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
      private static float FingerExtension(NLandmark mcp, NLandmark tip, float palmSize)
      {
          if (palmSize < 1e-6f)
          {
              return 0f;
          }

          float ratio = Vector2.Distance(P2(tip), P2(mcp)) / palmSize;
          return Mathf.Clamp01(Mathf.InverseLerp(0.5f, 1.0f, ratio));
      }

      // 1 if the finger is curled by EITHER measure: bent at the joints (angle)
      // or foreshortened/short in the image. Orientation-robust fist cue.
      private static float FingerCurledEitherWay(
          NormalizedLandmarks hand, int mcp, int pip, int tip, float palmSize)
      {
          float byAngle = FingerCurl(hand.landmarks[mcp], hand.landmarks[pip], hand.landmarks[tip]);
          float byLength = 1f - FingerExtension(hand.landmarks[mcp], hand.landmarks[tip], palmSize);
          return Mathf.Max(byAngle, byLength);
      }

      // Combined "this finger is sticking out" score: long AND not bent.
      private static float FingerOpenScore(
          NLandmark mcp,
          NLandmark pip,
          NLandmark tip,
          float palmSize)
      {
          return FingerExtension(mcp, tip, palmSize) * (1f - FingerCurl(mcp, pip, tip));
      }

      // 0 = fingertip far from palm center, 1 = tucked into the palm.
      private static float TipCompactness(NLandmark tip, Vector2 palmCenter, float palmSize)
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

      // 0 = thumb tip rests on the curled fingers (fist), 1 = thumb tip juts far
      // from the balled hand (thumbs-up). Measured against the centroid of the
      // four fingertips, so it is independent of hand orientation and size.
      private static float ThumbAwayFromFist(NormalizedLandmarks hand, float palmSize)
      {
          if (palmSize < 1e-6f)
          {
              return 0f;
          }

          Vector2 fingertipCentroid =
              (
                  P2(hand.landmarks[8]) +
                  P2(hand.landmarks[12]) +
                  P2(hand.landmarks[16]) +
                  P2(hand.landmarks[20])
              ) / 4f;

          float d = Vector2.Distance(P2(hand.landmarks[4]), fingertipCentroid) / palmSize;
          return Mathf.Clamp01(Mathf.InverseLerp(0.4f, 0.85f, d));
      }

      // One-hand pinch (rotation) is kept strict so it only fires on a deliberate
      // pinch. Two-hand pinch (zoom/explode) uses a lower bar because it requires
      // BOTH hands to pass at once, which is otherwise easy to drop out of.
      private const float PinchTriggerConfidence = 0.6f;
      private const float TwoHandPinchTriggerConfidence = 0.4f;

      private static bool IsPinching(NormalizedLandmarks hand)
      {
          return IsPinching(hand, PinchTriggerConfidence);
      }

      private static bool IsPinching(NormalizedLandmarks hand, float threshold)
      {
          return GetPinchConfidence(hand) >= threshold;
      }

      private static float GetPinchConfidence(NormalizedLandmarks hand)
      {
          var thumbTip = hand.landmarks[4];
          var indexMcp = hand.landmarks[5];
          var indexTip = hand.landmarks[8];

          float palmSize = PalmSize(hand);
          if (palmSize < 1e-6f)
          {
              return 0f;
          }

          // Measure thumb-tip <-> index-TIP distance only, in 2D (x/y) so it stays
          // on the same scale as palmSize. A pinch is specifically the two TIPS
          // meeting. Do NOT fall back to the index pip/dip joints: in a fist the
          // thumb wraps down near those joints, which would read as a false pinch.
          Vector2 thumbPoint = P2(thumbTip);
          float pinchDistance = Vector2.Distance(thumbPoint, P2(indexTip));

          // Distance band (as a fraction of palm width) over which the pinch
          // ramps from 0 -> 1. Tighter values require the fingers to be closer
          // together before the pinch registers.
          float openDistance = palmSize * 0.48f;
          float closedDistance = palmSize * 0.20f;
          float distanceScore = Mathf.Clamp01(Mathf.InverseLerp(openDistance, closedDistance, pinchDistance));

          // Gentle fist rejection via the index-extension gate (floor ~0.65 so a
          // naturally-bent pinch still reads strongly). The main thing keeping a
          // fist from registering as a pinch is the distance term above: in a fist
          // the thumb tip and index TIP are not actually together, so distanceScore
          // stays low and the product lands well under the trigger.
          float indexExtended = FingerExtension(indexMcp, indexTip, palmSize);
          return Mathf.Clamp01(distanceScore * Mathf.Lerp(0.65f, 1f, indexExtended));
      }

      private static float GetFistConfidence(NormalizedLandmarks hand)
      {
          float palmSize = PalmSize(hand);
          if (palmSize < 1e-6f)
          {
              return 0f;
          }

          // Each finger counts as curled if EITHER cue fires: its bend angle is
          // large (strong for a side-on fist) OR it is foreshortened/short in the
          // image (strong for a camera-facing fist). Taking the per-finger max
          // makes the fist read high at any comfortable wrist angle, instead of
          // forcing the user to twist the hand into one specific orientation.
          float fistShape =
              (
                  FingerCurledEitherWay(hand, 5, 6, 8, palmSize) +
                  FingerCurledEitherWay(hand, 9, 10, 12, palmSize) +
                  FingerCurledEitherWay(hand, 13, 14, 16, palmSize) +
                  FingerCurledEitherWay(hand, 17, 18, 20, palmSize)
              ) / 4f;

          // A jutting thumb means this is a thumbs-up, not a fist. Suppress fist
          // strongly so the two gestures are mutually exclusive at the source.
          float thumbSticksOut = ThumbAwayFromFist(hand, palmSize);
          return Mathf.Clamp01(fistShape * (1f - 0.8f * thumbSticksOut));
      }

      private static float GetThumbsUpConfidence(NormalizedLandmarks hand)
      {
          float palmSize = PalmSize(hand);
          if (palmSize < 1e-6f)
          {
              return 0f;
          }

          // A thumbs-up and a fist BOTH curl the four fingers; the only difference
          // is the thumb. The strongest discriminator is how far the thumb TIP is
          // from the balled-up hand: in a thumbs-up it juts out; in a fist it rests
          // on the curled fingers. This is orientation- and size-independent.
          float fingersCurled =
              (
                  FingerCurl(hand.landmarks[5], hand.landmarks[6], hand.landmarks[8]) +
                  FingerCurl(hand.landmarks[9], hand.landmarks[10], hand.landmarks[12]) +
                  FingerCurl(hand.landmarks[13], hand.landmarks[14], hand.landmarks[16]) +
                  FingerCurl(hand.landmarks[17], hand.landmarks[18], hand.landmarks[20])
              ) / 4f;

          float thumbSticksOut = ThumbAwayFromFist(hand, palmSize);
          float thumbStraight = 1f - FingerCurl(hand.landmarks[2], hand.landmarks[3], hand.landmarks[4]);
          float thumbSignal = 0.7f * thumbSticksOut + 0.3f * thumbStraight;

          // Require the four fingers to be closed, then let the thumb decide.
          return Mathf.Clamp01(Mathf.Lerp(0.4f, 1f, fingersCurled) * thumbSignal);
      }

      private static float GetOpenHandConfidence(NormalizedLandmarks hand)
      {
          float palmSize = PalmSize(hand);
          if (palmSize < 1e-6f)
          {
              return 0f;
          }

          // Open palm = all four fingers straight (uncurled). We key off curl
          // rather than finger length, because the pinky/ring are naturally short
          // and a length-based score under-rates a fully spread hand. Pinch
          // suppression is applied in ClassifyHand so a pinch never reads as open.
          float straightness =
              (
                  (1f - FingerCurl(hand.landmarks[5], hand.landmarks[6], hand.landmarks[8])) +
                  (1f - FingerCurl(hand.landmarks[9], hand.landmarks[10], hand.landmarks[12])) +
                  (1f - FingerCurl(hand.landmarks[13], hand.landmarks[14], hand.landmarks[16])) +
                  (1f - FingerCurl(hand.landmarks[17], hand.landmarks[18], hand.landmarks[20]))
              ) / 4f;

          return Mathf.Clamp01(straightness);
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
          float fingersOut =
              (FingerOpenScore(indexMcp, indexPip, indexTip, palmSize) +
               FingerOpenScore(middleMcp, middlePip, middleTip, palmSize)) * 0.5f;
          float othersTucked =
              (FingerCurl(hand.landmarks[13], hand.landmarks[14], hand.landmarks[16]) +
               FingerCurl(hand.landmarks[17], hand.landmarks[18], hand.landmarks[20])) * 0.5f;

          // The two fingers must point sideways and stay parallel. This is what
          // makes the gesture a *side* peace rather than a "V" pointing up.
          float horizontal = (Horizontalness(indexDirection) + Horizontalness(middleDirection)) * 0.5f;
          float aligned = Mathf.Clamp01(
              Mathf.InverseLerp(0.4f, 0.85f,
                  Vector2.Dot(indexDirection.normalized, middleDirection.normalized)));
          float sameDirection =
              Mathf.Sign(indexDirection.x) == Mathf.Sign(middleDirection.x) ? 1f : 0f;

          // Weighted sum (forgiving) rather than a min()/product (which let a
          // single loose finger kill the whole score). othersTucked is weighted
          // heavily so an open hand held sideways does not read as a nav gesture.
          float confidence = Mathf.Clamp01(
              (0.30f * fingersOut +
               0.35f * othersTucked +
               0.20f * horizontal +
               0.15f * aligned) * sameDirection);

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
