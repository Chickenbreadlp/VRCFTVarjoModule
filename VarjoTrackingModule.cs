using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
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

        private static Vector2 GetGazeVector(GazeRay varjoGaze)
        {
            return new Vector2((float)varjoGaze.forward.x, (float)varjoGaze.forward.y);
        }

        // This function is used to disect the single Varjo Openness Float into a more managable Openness, Squeeze and Widen Value
        // As the three Parameters are exclusive to one another (if one is between 0 or 1, the others have to be either 0 or 1), we only need to do maths for one parameter
        private static (float openness, float squeeze, float widen) ParseOpenness(float currentOpenness, float externalOpenness, GazeEyeStatus eyeStatus)
        {
            float parsedOpenness;
            float widen = 0;
            float squeeze = 0;
            VarjoOpennessMode mode;


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

            if (eyeStatus >= GazeEyeStatus.Compensated || parsedOpenness < currentOpenness + MAX_OPENNESS_DEVIATION)
            {
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
                parsedOpenness = -1;
            }

            return (parsedOpenness, squeeze, widen);
        }

        // This function parses the external module's full-data data into multiple VRCFT-Parseable single-eye structs
        public static void Update(ref UnifiedEyeData data, ref UnifiedExpressionShape[] expressionData, GazeData external, EyeMeasurements externalMeasurements)
        {
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

            (float rightOpenness, float rightSqueeze, float rightWiden) = ParseOpenness(data.Right.Openness, externalMeasurements.rightEyeOpenness, external.rightStatus);
            (float leftOpenness, float leftSqueeze, float leftWiden) = ParseOpenness(data.Left.Openness, externalMeasurements.leftEyeOpenness, external.leftStatus);

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
        private static VarjoInterface tracker;

        public override (bool SupportsEye, bool SupportsExpression) Supported => (true, false);

        // Synchronous module initialization. Take as much time as you need to initialize any external modules. This runs in the init-thread
        public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eye, bool lip)
        {
            ModuleInformation.Name = "Varjo Eye Tracking";

            tracker = new VarjoNativeInterface();
            bool pipeConnected = tracker.Initialize(Logger);

            if (pipeConnected)
            {
                string hmdName = tracker.GetHMDName();
                if (hmdName == "AERO")
                {
                    hmdName = "Aero";
                }

                var hmdIcon = GetType().Assembly.GetManifestResourceStream("VRCFTVarjoModule.Assets." + hmdName + ".png");

                if (hmdIcon == null)
                {
                    hmdIcon = GetType().Assembly.GetManifestResourceStream("VRCFTVarjoModule.Assets.unknown.png");
                }
                else
                {
                    ModuleInformation.Name = "Varjo " + hmdName + " Eye Tracking";
                }

                ModuleInformation.StaticImages = hmdIcon != null ? new List<Stream> { hmdIcon } : ModuleInformation.StaticImages;
            }

            return (pipeConnected, false);
        }

        // The update function needs to be defined separately in case the user is running with the --vrcft-nothread launch parameter
        public override void Update()
        {
            if (Status == ModuleState.Active)
            {
                tracker.Update();
                TrackingData.Update(ref UnifiedTracking.Data.Eye, ref UnifiedTracking.Data.Shapes, tracker.GetGazeData(), tracker.GetEyeMeasurements());
            }

            Thread.Sleep(10);
        }

        // A chance to de-initialize everything. This runs synchronously inside main game thread. Do not touch any Unity objects here.
        public override void Teardown()
        {
            tracker.Teardown();
        }
    }
}