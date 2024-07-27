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
        internal static ConfigManager Config { get; set; }
        internal static uint leftTimeoutCycles = 0, rightTimeoutCycles = 0;
        internal static bool isTrackingLeft = false, isTrackingRight = false;
        internal static Vector2 refGazeLeft = new Vector2(0, 0), refGazeRight = new Vector2(0, 0);
        internal static Vector2 gazeLeft = new Vector2(0, 0), gazeRight = new Vector2(0, 0);
        internal static GazeEyeStatus validGazeStatus { get; set; }

        /// <summary>
        /// Function to map a Varjo GazeRay to a Vector2 Ray for use in VRCFT
        /// </summary>
        /// <param name="varjoGaze"></param>
        /// <returns></returns>
        private static Vector2 GetGazeVector(GazeRay varjoGaze)
        {
            return new Vector2((float)varjoGaze.forward.x, (float)varjoGaze.forward.y);
        }

        /// <summary>
        /// This function is used to disect the single Varjo Openness Float into a more managable Openness, Squeeze and Widen Value
        /// As the three Parameters are exclusive to one another (if one is between 0 or 1, the others have to be either 0 or 1), we only need to do maths for one parameter
        /// `openness` is returned as `-1` when VRCFT values should remain unchanged
        /// </summary>
        /// <param name="currentOpenness">Current openness value from VRCFT</param>
        /// <param name="externalOpenness">Recorded openness value from Varjo</param>
        /// <param name="eyeStatus">Tracking status of the eye</param>
        /// <returns></returns>
        private static (float openness, float squeeze, float widen) ParseOpenness(float currentOpenness, float externalOpenness, GazeEyeStatus eyeStatus, uint timeoutCycles)
        {
            float parsedOpenness;
            float widen = 0f;
            float squeeze = 0f;

            // Check what range the Varjo Openness falls into and calculate the new "Openness" (ignore widen/squeeze thresholds when they are the respective max values)
            if (Config.squeezeThreshold > 0f && externalOpenness <= Config.squeezeThreshold)
            {
                parsedOpenness = 0f;
                squeeze = (externalOpenness / -Config.squeezeThreshold) + 1;
            }
            else if (Config.widenThreshold < 1f && externalOpenness >= Config.widenThreshold)
            {
                parsedOpenness = 1f;
                widen = (externalOpenness - Config.widenThreshold) / (1 - Config.widenThreshold);
            }
            else
            {
                parsedOpenness = (externalOpenness - Config.squeezeThreshold) / Config.opennessRange;
            }

            // filter openness value if configured
            switch (Config.opennessStrategy)
            {
                case OpennessStrategy.RestrictedSpeed:
                    {
                        // Check if new Openness is within allowable margin
                        if ((eyeStatus <= GazeEyeStatus.Visible || timeoutCycles != 0) && parsedOpenness >= currentOpenness + Config.maxOpenSpeed)
                        {
                            // And if not set the openness to -1 to indicate no changes
                            parsedOpenness = -1f;
                        }
                        break;
                    }
                case OpennessStrategy.Hybrid:
                    {
                        if (eyeStatus == GazeEyeStatus.Compensated && parsedOpenness > 0.75f)
                        {
                            parsedOpenness = 0.75f;
                            widen = 0;
                        }
                        else if (eyeStatus == GazeEyeStatus.Visible && parsedOpenness > 0.5f)
                        {
                            parsedOpenness = 0.5f;
                            widen = 0;
                        }
                        else if (eyeStatus == GazeEyeStatus.Invalid && parsedOpenness > 0.25f)
                        {
                            parsedOpenness = 0.25f;
                            widen = 0;
                        }
                        break;
                    }
            }

            return (parsedOpenness, squeeze, widen);
        }

        /// <summary>
        /// Main Update function
        /// Mapps Varjo Eye Data to VRCFT Parameters
        /// </summary>
        /// <param name="data">Primary VRCFT gaza data object</param>
        /// <param name="expressionData">VRCFT expression data object</param>
        /// <param name="external">Varjo Gaze data object</param>
        /// <param name="externalMeasurements">Auxillary Varjo measurements object</param>
        public static void Update(ref UnifiedEyeData data, ref UnifiedExpressionShape[] expressionData, GazeData external, EyeMeasurements externalMeasurements)
        {
            // Detect which eye is tracked depending on if their status is somewhat reliable according to the SDK
            isTrackingRight = false;
            if (external.rightStatus >= validGazeStatus)
            {
                if (rightTimeoutCycles == 0) isTrackingRight = true;
                else rightTimeoutCycles--;
            }
            else rightTimeoutCycles = Config.stabalizingCycles;

            isTrackingLeft = false;
            if (external.leftStatus >= validGazeStatus)
            {
                if (leftTimeoutCycles == 0) isTrackingLeft = true;
                else leftTimeoutCycles--;
            }
            else leftTimeoutCycles = Config.stabalizingCycles;


            // Set the Gaze and Pupil Size for each eye
            if (isTrackingRight && isTrackingLeft)
            {
                gazeRight = GetGazeVector(external.rightEye);
                gazeLeft = GetGazeVector(external.leftEye);
                refGazeRight = gazeRight;
                refGazeLeft = gazeLeft;
                data.Right.Gaze = gazeRight;
                data.Right.PupilDiameter_MM = externalMeasurements.rightPupilDiameterInMM;
                data.Left.Gaze = gazeLeft;
                data.Left.PupilDiameter_MM = externalMeasurements.leftPupilDiameterInMM;
            }
            else if (isTrackingRight)
            {
                gazeRight = GetGazeVector(external.rightEye);
                data.Right.Gaze = gazeRight;
                data.Right.PupilDiameter_MM = externalMeasurements.rightPupilDiameterInMM;
                if (Config.untrackedEyeFollowTracked)
                {
                    data.Left.Gaze = (gazeRight - refGazeRight) + refGazeLeft;
                    data.Left.PupilDiameter_MM = externalMeasurements.rightPupilDiameterInMM;
                }
            }
            else if (isTrackingLeft)
            {
                gazeLeft = GetGazeVector(external.leftEye);
                data.Left.Gaze = gazeLeft;
                data.Left.PupilDiameter_MM = externalMeasurements.leftPupilDiameterInMM;
                if (Config.untrackedEyeFollowTracked)
                {
                    data.Right.Gaze = (gazeLeft - refGazeLeft) + refGazeRight;
                    data.Right.PupilDiameter_MM = externalMeasurements.leftPupilDiameterInMM;
                }
            }


            // Parse openness as boolean or float depending on config
            switch (Config.opennessStrategy)
            {
                case OpennessStrategy.Bool:
                    data.Right.Openness = external.rightStatus >= GazeEyeStatus.Compensated ? 1f : 0f;
                    data.Left.Openness = external.leftStatus >= GazeEyeStatus.Compensated ? 1f : 0f;
                    break;

                case OpennessStrategy.Stepped:
                    data.Right.Openness = (float)external.rightStatus / 3f;
                    data.Left.Openness = (float)external.leftStatus / 3f;
                    break;

                default:
                    // Parse Openness and store them in temporary variables
                    (float rightOpenness, float rightSqueeze, float rightWiden) = ParseOpenness(data.Right.Openness, externalMeasurements.rightEyeOpenness, external.rightStatus, rightTimeoutCycles);
                    (float leftOpenness, float leftSqueeze, float leftWiden) = ParseOpenness(data.Left.Openness, externalMeasurements.leftEyeOpenness, external.leftStatus, leftTimeoutCycles);

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
                    break;
            }
        }
    }
    
    public class VarjoTrackingModule : ExtTrackingModule 
    {
        private static VarjoNativeInterface tracker;
        private static ConfigManager config;

        // Mark this module as only supporting Eye Tracking
        public override (bool SupportsEye, bool SupportsExpression) Supported => (true, false);

        // Prepares the Varjo Interface for communication with the SDK and sets module display name and icon
        public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eye, bool lip)
        {
            // as the very first thing: Init the config manager
            config = new ConfigManager(Logger);
            Logger.LogInformation($"Testing Config: {config.readDelay}");
            TrackingData.Config = config;
            TrackingData.validGazeStatus = config.pickyTracking ? GazeEyeStatus.Tracked : GazeEyeStatus.Compensated;

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
                    Logger.LogWarning("There seems to be an issue with getting Tracking data. Will try again in 1 second.");
                    Thread.Sleep(990);
                }
            }

            // Sleep the thread for a predetermined time
            Thread.Sleep(config.readDelay);
        }

        // Function to be called when the module is torn down; this call should be passed through to the Varjo Interface
        public override void Teardown()
        {
            tracker.Teardown();
        }
    }
}
