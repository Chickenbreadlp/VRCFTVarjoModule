using System.Runtime.InteropServices;

namespace VRCFTVarjoModule
{
    //Varjo's structs used with both native library and companion
    [StructLayout(LayoutKind.Sequential)]
    public struct Vector
    {

        public double x;
        public double y;
        public double z;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GazeRay
    {
        public Vector origin;   //!< Origin of the ray.
        public Vector forward;  //!< Direction of the ray.
    }

    public enum GazeStatus : long
    {
        Invalid = 0,
        Adjust = 1,
        Valid = 2
    }

    public enum GazeEyeStatus : long
    {
        Invalid = 0,
        Visible = 1,
        Compensated = 2,
        Tracked = 3
    }


    [StructLayout(LayoutKind.Sequential)]
    public struct GazeData
    {
        public GazeRay leftEye;                 //!< Left eye gaze ray.
        public GazeRay rightEye;                //!< Right eye gaze ray.
        public GazeRay gaze;                    //!< Normalized gaze direction ray.
        public double focusDistance;            //!< Estimated gaze direction focus point distance.
        public double stability;                //!< Focus point stability.
        public long captureTime;                //!< Varjo time when this data was captured, see varjo_GetCurrentTime()
        public GazeEyeStatus leftStatus;        //!< Status of left eye data.
        public GazeEyeStatus rightStatus;       //!< Status of right eye data.
        public GazeStatus status;               //!< Tracking main status.
        public long frameNumber;                //!< Frame number, increases monotonically.
        public double leftPupilSize;            //!< Normalized [0..1] left eye pupil size.
        public double rightPupilSize;           //!< Normalized [0..1] right eye pupil size.
    }


    [StructLayout(LayoutKind.Sequential)]
    public struct EyeMeasurements
    {
        public long frameNumber;                    //!< Frame number, increases monotonically.
        public long captureTime;                    //!< Varjo time when this data was captured, see varjo_GetCurrentTime()
        public float interPupillaryDistanceInMM;    //!< Estimated IPD in millimeters
        public float leftPupilIrisDiameterRatio;    //!< Ratio between left pupil and left iris.
        public float rightPupilIrisDiameterRatio;   //!< Ratio between right pupil and right iris.
        public float leftPupilDiameterInMM;         //!< Left pupil diameter in mm
        public float rightPupilDiameterInMM;        //!< Right pupil diameter in mm
        public float leftIrisDiameterInMM;          //!< Left iris diameter in mm
        public float rightIrisDiameterInMM;         //!< Right iris diameter in mm
        public float leftEyeOpenness;               //!< Left Eye Openness
        public float rightEyeOpenness;              //!< Right Eye Openness
    }


    [StructLayout(LayoutKind.Sequential)]
    public struct GazeCalibrationParameter
    {
        [MarshalAs(UnmanagedType.LPStr)] public string key;
        [MarshalAs(UnmanagedType.LPStr)] public string value;
    }

    public enum GazeCalibrationMode
    {
        Legacy,
        Fast
    };

    public enum GazeOutputFilterType
    {
        None,
        Standard
    }

    public enum GazeOutputFrequency
    {
        MaximumSupported,
        Frequency100Hz,
        Frequency200Hz
    }

    public enum GazeEyeCalibrationQuality
    {
        Invalid = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GazeCalibrationQuality
    {
        public GazeEyeCalibrationQuality left;
        public GazeEyeCalibrationQuality right;
    }

    public enum VarjoPropertyKey
    {
        Invalid = 0x0,
        UserPresence = 0x2000,
        GazeCalibrating = 0xA000,
        GazeCalibrated = 0xA001,
        GazeCalibrationQuality = 0xA002,
        GazeAllowed = 0xA003,
        GazeEyeCalibrationQuality_Left = 0xA004,
        GazeEyeCalibrationQuality_Right = 0xA005,
        GazeIPDEstimate = 0xA010,
        HMDConnected = 0xE001,
        HMDProductName = 0xE002,
        HMDSerialNumber = 0xE003,
        MRAvailable = 0xD000
    }


}
