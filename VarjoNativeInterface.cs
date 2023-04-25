using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using VRCFaceTracking;

namespace VRCFTVarjoModule
{
    class VarjoNativeInterface
    {
        private IntPtr _session;
        protected GazeData gazeData;
        protected EyeMeasurements eyeMeasurements;
        protected ILogger Logger;

        #region Lifetime methods (Init, Update, Teardown)
        public bool Initialize(ILogger loggerInstance)
        {
            Logger = loggerInstance;

            if (!VarjoAvailable())
            {
                Logger.LogError("Varjo headset isn't detected");
                return false;
            }
            if (LoadLibrary())
            {
                _session = varjo_SessionInit();
                if (_session == IntPtr.Zero)
                {
                    return false;
                }
                if (!varjo_IsGazeAllowed(_session))
                {
                    Logger.LogError("Gaze tracking is not allowed! Please enable it in the Varjo Base!");
                    return false;
                }
                varjo_GazeInit(_session);
                varjo_SyncProperties(_session);
                return true;
            }
            return false;
        }

        public void Teardown()
        {
            varjo_SessionShutDown(_session);
        }

        // Get's the newest Data from the SDK and stores it internally
        public bool Update()
        {
            if (_session == IntPtr.Zero)
                return false;

            // Get's GazeData and EyeMeasurements from the Varjo SDK
            // Return value states whether or not the request was successful (true = has Data; false = Error occured)
            bool hasData = varjo_GetGazeData(_session, out gazeData, out eyeMeasurements);

            if (!hasData)
                Logger.LogWarning("Error while getting Gaze Data");

            return hasData;
        }
        #endregion

        #region Public Getters
        public GazeData GetGazeData()
        {
            return gazeData;
        }

        public EyeMeasurements GetEyeMeasurements()
        {
            return eyeMeasurements;
        }

        public string GetHMDName()
        {
            int bufferSize = varjo_GetPropertyStringSize(_session, VarjoPropertyKey.HMDProductName);
            StringBuilder buffer = new StringBuilder(bufferSize);
            varjo_GetPropertyString(_session, VarjoPropertyKey.HMDProductName, buffer, bufferSize);

            return buffer.ToString();
        }
        #endregion

        #region Internal helper methods
        private bool LoadLibrary()
        {
            IEnumerable<string> dllPaths = ExtractAssemblies(new string[] { "Varjo.VarjoLib.dll" });
            if (dllPaths.Count() > 0)
            {
                var path = dllPaths.First();
                if (path == null)
                {
                    Logger.LogError(string.Concat("Couldn't extract the library ", path));
                    return false;
                }
                if (LoadLibrary(path) == IntPtr.Zero)
                {
                    Logger.LogError(string.Concat("Unable to load library ", path));
                    return false;
                }
                Logger.LogInformation(string.Concat("Loaded library ", path));
                return true;
            }
            else
            {
                return false;
            }
        }


        // I can't ask nicely to add my DLL into the dependency list so I had to steal code from the main repo :(
        private IEnumerable<string> ExtractAssemblies(IEnumerable<string> resourceNames)
        {
            var extractedPaths = new List<string>();

            var dirName = Path.Combine(Utils.PersistentDataDirectory, "StockLibs");
            if (!Directory.Exists(dirName))
                Directory.CreateDirectory(dirName);

            foreach (var dll in resourceNames)
            {
                var dllPath = Path.Combine(dirName, GetAssemblyNameFromPath(dll));

                using (var stm = Assembly.GetAssembly(typeof(VarjoNativeInterface)).GetManifestResourceStream("VRCFTVarjoModule.TrackingLibs." + dll))
                {
                    try
                    {
                        using (Stream outFile = File.Create(dllPath))
                        {
                            const int sz = 4096;
                            var buf = new byte[sz];
                            while (true)
                            {
                                if (stm == null) continue;
                                var nRead = stm.Read(buf, 0, sz);
                                if (nRead < 1)
                                    break;
                                outFile.Write(buf, 0, nRead);
                            }
                        }

                        extractedPaths.Add(dllPath);
                    }
                    catch (Exception e)
                    {
                        Logger.LogError($"Failed to get DLL: " + e.Message);
                    }
                }
            }
            return extractedPaths;
        }

        private static string GetAssemblyNameFromPath(string path)
        {
            var splitPath = path.Split('.').ToList();
            splitPath.Reverse();
            return splitPath[1] + ".dll";
        }

        private static bool VarjoAvailable()
        {
            // totally not how the official Varjo library works under the hood
            return File.Exists("\\\\.\\pipe\\Varjo\\InfoService");
        }
        #endregion

        #region DllImports
        [DllImport("kernel32", CharSet = CharSet.Unicode, ExactSpelling = false, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern bool varjo_IsAvailable();

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern IntPtr varjo_SessionInit();

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern void varjo_SessionShutDown(IntPtr session);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern void varjo_GazeInit(IntPtr session);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern int varjo_GetError(IntPtr session);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern string varjo_GetErrorDesc(int errorCode);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern bool varjo_IsGazeAllowed(IntPtr session);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern bool varjo_IsGazeCalibrated(IntPtr session);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern GazeData varjo_GetGaze(IntPtr session);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern bool varjo_GetGazeData(IntPtr session, out GazeData gaze, out EyeMeasurements eyeMeasurements);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern void varjo_RequestGazeCalibration(IntPtr session);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern bool varjo_GetPropertyBool(IntPtr session, VarjoPropertyKey propertyKey);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern int varjo_GetPropertyInt(IntPtr session, VarjoPropertyKey propertyKey);

        [DllImport("VarjoLib", CharSet = CharSet.Ansi)]
        private static extern void varjo_GetPropertyString(IntPtr session, VarjoPropertyKey propertyKey, StringBuilder buffer, int bufferSize);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern int varjo_GetPropertyStringSize(IntPtr session, VarjoPropertyKey propertyKey);

        [DllImport("VarjoLib", CharSet = CharSet.Auto)]
        private static extern void varjo_SyncProperties(IntPtr session);
        #endregion

    }
}
