using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using VRCFaceTracking;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Types;

namespace VRCFTVarjoModule
{
   
    // This class contains the overrides for any VRCFT Tracking Data struct functions
    public static class TrackingData
    {
        enum VarjoOpennessMode : byte
        {
            Squeeze = 0,
            Openness = 1,
            Widen = 2
        }

        // Magic numbers to disect the 0-1 Varjo Openness float into SRanipal Openness, Widen & Squeeze values
        // Based on Testing from @Chickenbread; may need adjusting
        private static readonly float EYE_SQUEEZE_THRESHOLD = 0.15f, EYE_WIDEN_THRESHOLD = 0.90f;
        // Threshold of the maximum opening in Eye Openness that will be tracked as long as the eye status is "invalid"
        private static readonly float MAX_OPENNESS_DEVIATION = 0.1f;

        // Function to map a Varjo GazeRay to a Vector2 Ray for use in VRCFT
        private static Vector2 GetGazeVector(GazeRay varjoGaze)
        {
            return new Vector2((float)varjoGaze.forward.x, (float)varjoGaze.forward.y);
        }

        // This function is used to disect the single Varjo Openness Float into a more managable Openness, Squeeze and Widen Value
        // As the three Parameters are exclusive to one another (if one is between 0 or 1, the others have to be either 0 or 1), we only need to do maths for one parameter
        // `openness` is returned as `-1` when VRCFT values should remain unchanged
        private static (float openness, float squeeze, float widen) ParseOpenness(float currentOpenness, float externalOpenness, GazeEyeStatus eyeStatus)
        {
            float parsedOpenness;
            float widen = 0;
            float squeeze = 0;
            VarjoOpennessMode mode;

            // Check what range the Varjo Openness falls into and calculate the new "Openness"
            if (externalOpenness <= EYE_SQUEEZE_THRESHOLD)
            {
                parsedOpenness = 0;
                mode = VarjoOpennessMode.Squeeze;
            }
            else if (externalOpenness >= EYE_WIDEN_THRESHOLD)
            {
                parsedOpenness = 1;
                mode = VarjoOpennessMode.Widen;
            }
            else
            {
                parsedOpenness = (externalOpenness - EYE_SQUEEZE_THRESHOLD) / (EYE_WIDEN_THRESHOLD - EYE_SQUEEZE_THRESHOLD);
                mode = VarjoOpennessMode.Openness;
            }

            // Check if new Openness is within allowable margin
            if (eyeStatus >= GazeEyeStatus.Compensated || parsedOpenness < currentOpenness + MAX_OPENNESS_DEVIATION)
            {
                // ...and calculate squeeze/widen if called for
                switch (mode)
                {
                    case VarjoOpennessMode.Squeeze:
                        squeeze = (externalOpenness / -EYE_SQUEEZE_THRESHOLD) + 1;
                        break;
                    case VarjoOpennessMode.Widen:
                        widen = (externalOpenness - EYE_WIDEN_THRESHOLD) / (1 - EYE_WIDEN_THRESHOLD);
                        break;
                }
            }
            else
            {
                // Otherwise set the openness to -1 to indicate no changes
                parsedOpenness = -1;
            }

            return (parsedOpenness, squeeze, widen);
        }

        // Main Update function
        // Mapps Varjo Eye Data to VRCFT Parameters
        public static void Update(ref UnifiedEyeData data, ref UnifiedExpressionShape[] expressionData, GazeData external, EyeMeasurements externalMeasurements)
        {
            // Set the Gaze and Pupil Size for each eye when their status is somewhat reliable according to the SDK
            if (external.rightStatus >= GazeEyeStatus.Compensated)
            {
                data.Right.Gaze = GetGazeVector(external.rightEye);
                data.Right.PupilDiameter_MM = externalMeasurements.rightPupilDiameterInMM;
            }
            if (external.leftStatus >= GazeEyeStatus.Compensated)
            {
                data.Left.Gaze = GetGazeVector(external.leftEye);
                data.Left.PupilDiameter_MM = externalMeasurements.leftPupilDiameterInMM;
            }

            // Parse Openness as before, but instead of writing them immideatly, we store them in variables temporarely
            (float rightOpenness, float rightSqueeze, float rightWiden) = ParseOpenness(data.Right.Openness, externalMeasurements.rightEyeOpenness, external.rightStatus);
            (float leftOpenness, float leftSqueeze, float leftWiden) = ParseOpenness(data.Left.Openness, externalMeasurements.leftEyeOpenness, external.leftStatus);

            // Set Openness Values for each eye; if they should change
            if (rightOpenness >= 0.0f)
            {
                data.Right.Openness = rightOpenness;
                expressionData[(int)UnifiedExpressions.EyeWideRight].Weight = rightWiden;
                expressionData[(int)UnifiedExpressions.EyeSquintRight].Weight = rightSqueeze;

                // Duplicated like on the SRanipal Module
                expressionData[(int)UnifiedExpressions.BrowInnerUpRight].Weight = rightWiden;
                expressionData[(int)UnifiedExpressions.BrowOuterUpRight].Weight = rightWiden;
                expressionData[(int)UnifiedExpressions.BrowPinchRight].Weight = rightSqueeze;
                expressionData[(int)UnifiedExpressions.BrowLowererRight].Weight = rightSqueeze;
            }
            if (leftOpenness >= 0.0f)
            {
                data.Left.Openness = leftOpenness;
                expressionData[(int)UnifiedExpressions.EyeWideLeft].Weight = leftWiden;
                expressionData[(int)UnifiedExpressions.EyeSquintLeft].Weight = leftSqueeze;

                // Duplicated like on the SRanipal Module
                expressionData[(int)UnifiedExpressions.BrowInnerUpLeft].Weight = leftWiden;
                expressionData[(int)UnifiedExpressions.BrowOuterUpLeft].Weight = leftWiden;
                expressionData[(int)UnifiedExpressions.BrowPinchLeft].Weight = leftSqueeze;
                expressionData[(int)UnifiedExpressions.BrowLowererLeft].Weight = leftSqueeze;
            }
        }
    }
    
    public class VarjoTrackingModule : ExtTrackingModule 
    {
        private static VarjoNativeInterface tracker;

        // Mark this module as only supporting Eye Tracking
        public override (bool SupportsEye, bool SupportsExpression) Supported => (true, false);

        // Prepares the Varjo Interface for communication with the SDK and sets module display name and icon
        public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eye, bool lip)
        {
            // Init our tracker first
            tracker = new VarjoNativeInterface();
            bool pipeConnected = tracker.Initialize(Logger);

            if (pipeConnected)
            {
                // if the tracker has init'ed, get the first 4 chars of the HMD name for Icon and Module name (for VR-#, XR-# or AERO)
                string hmdName = tracker.GetHMDName().Substring(0, 4);

                // in case we're dealing with the Aero, capitilize the name properly
                if (hmdName == "AERO") hmdName = "Aero";
                var hmdIcon = GetType().Assembly.GetManifestResourceStream("VRCFTVarjoModule.Assets." + hmdName + ".png");

                // if no icon can be found the the reported HMD, use defaults and log the full name
                if (hmdIcon == null)
                {
                    Logger.LogInformation("Unknown HMD Name: " + tracker.GetHMDName());
                    ModuleInformation.Name = "Varjo Eye Tracking";
                    hmdIcon = GetType().Assembly.GetManifestResourceStream("VRCFTVarjoModule.Assets.unknown.png");
                }
                else
                {
                    ModuleInformation.Name = "Varjo " + hmdName + " Eye Tracking";
                }

                ModuleInformation.StaticImages = hmdIcon != null ? new List<Stream> { hmdIcon } : ModuleInformation.StaticImages;
            }

            // Tell the lib manager our result with the init and that we (again) do not support Lip Tracking
            return (pipeConnected, false);
        }

        // Update function to be called in a while(true) loop. Keeping a delay is necessary to not fully load an entire CPU thread! (despite what the docs state)
        public override void Update()
        {
            if (Status == ModuleState.Active)
            {
                // try and update data; log an error on failure and wait 250ms
                if (tracker.Update())
                {
                    TrackingData.Update(ref UnifiedTracking.Data.Eye, ref UnifiedTracking.Data.Shapes, tracker.GetGazeData(), tracker.GetEyeMeasurements());
                }
                else
                {
                    Logger.LogWarning("There seems to be an issue with getting Tracking data. Will try again in 250ms.");
                    Thread.Sleep(240);
                }
            }

            // Sleep the thread for 10ms (aka let the update run at 100Hz)
            Thread.Sleep(10);
        }

        // Function to be called when the module is torn down; this call should be passed through to the Varjo Interface
        public override void Teardown()
        {
            tracker.Teardown();
        }
    }
}