using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace VRCFTVarjoModule
{
    internal class ConfigManager
    {
        protected readonly IFormatProvider _formatProvider = new CultureInfo("en-001");
        protected ILogger Logger;
        protected string path;

        protected bool shouldCreateLink;
        public int readDelay {  get; protected set; }
        public uint stabalizingCycles { get; protected set; }
        public OpennessStrategy opennessStrategy { get; protected set; }

        // Magic numbers for float lid parsing
        public float squeezeThreshold { get; protected set; }
        public float widenThreshold { get; protected set; }
        public float opennessRange { get; protected set; }

        public float maxOpenSpeed { get; protected set; }

        public ConfigManager(ILogger loggerInstance) {
            Logger = loggerInstance;
            InitConfig();

            path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\VRCFT Varjo Config.ini";
            if (!File.Exists(path))
            {
                WriteConfig();
            }

            Reload();
            SaveModuleShortcut();
        }

        #region Config Handlers
        /// <summary>
        /// Inits internal config state to defaults
        /// </summary>
        public void InitConfig()
        {
            shouldCreateLink = true;
            readDelay = 10;
            stabalizingCycles = 0;
            opennessStrategy = OpennessStrategy.RestrictedSpeed;
            squeezeThreshold = 0.15f;
            widenThreshold = 0.9f;
            opennessRange = widenThreshold - squeezeThreshold;

            maxOpenSpeed = 0.1f;
        }

        /// <summary>
        /// Writes the current configuration to disk
        /// </summary>
        public void WriteConfig()
        {
            StreamWriter fs = null;
            try
            {
                fs = new StreamWriter(path, false, Encoding.UTF8);
                fs.AutoFlush = false;
                fs.WriteLine("[VarjoModule]");

                // Implement writing all the configs here! (please add comments using # or ; to the ini file)
                fs.WriteLine("");
                fs.WriteLine("; Whether or not the module should create a link to the module folder on the desktop");
                fs.WriteLine($"CreateLink={shouldCreateLink}");

                fs.WriteLine("");
                fs.WriteLine("; Double Time is an experimental option which lets the module read from the SDK 2x normal (200Hz)");
                fs.WriteLine("; This setting will do nothing but increase CPU time on VB versions older then 4.1");
                fs.WriteLine($"DoubleTime={readDelay == 5}");

                fs.WriteLine("");
                fs.WriteLine("; Stabalizing Cycles defines for how many consecutive tracking intervals the Eye Tracking status has to be \"Compensated\" or \"Good\" before tracking of gaze resumes.");
                fs.WriteLine("; 1 cycle is 10 milliseconds for DoubleTime=false and 5 milliseconds for DoubleTime=true");
                fs.WriteLine("; This can visually freeze your gaze when set too high or with unstable tracking, so use with caution!");
                fs.WriteLine($"StabalizingCycles={stabalizingCycles}");

                fs.WriteLine("");
                fs.WriteLine("; EyeLidStrat defines how the module calculates the Openness Value");
                fs.WriteLine("; There are 5 possible options: Bool, Stepped, RestrictedSpeed, RawFloat & Hybrid");
                fs.WriteLine("; Bool => The Eye Lids are either fully open or fully closed (no in-between) based on the individual Eye Status");
                fs.WriteLine("; Stepped => The Eye lids openness is stepped based on the individual eye status (Fully Open, 1/3 closed, 2/3 closed, fully closed)");
                fs.WriteLine("; RawFloat => The Openness Value given from the Varjo SDK is forwarded with no extra filtering (split up using the thresholds still happens)");
                fs.WriteLine("; RestrictedSpeed => (default behaviour) like RawFloat, however the speed at which the eyes can open is limited to MaxOpenSpeed when the eye status is registering as unreliable or invalid");
                fs.WriteLine("; Hybrid => Limits openness based on eye status, but allows for further refinement downwards using the openness value (the limits are 1/4 closed, 1/2 closed & 3/4 closed)");
                fs.WriteLine($"EyeLidStrat={OpennessStratToString(opennessStrategy)}");

                fs.WriteLine("");
                fs.WriteLine("; Squeeze and Widen Thershold are only relevant for float-based Eye Lid Strats");
                fs.WriteLine("; the Widen Thershold cannot be below the Squeeze Threshold and vice versa");
                fs.WriteLine("; Setting Squeeze to 0 or Widen to 1 will disable the parsing for that additional eye lid range");
                fs.WriteLine($"SqueezeThreshold={squeezeThreshold.ToString(_formatProvider)}");
                fs.WriteLine($"WidenThreshold={widenThreshold.ToString(_formatProvider)}");

                fs.WriteLine("");
                fs.WriteLine("; MaxOpenSpeed is only relevant for the \"RestrictedSpeed\" Eye Lid Strat");
                fs.WriteLine("; setting MaxOpenSpeed to 0 prevents the eye lids from opening up at all for as long as the Eye status reads invalid or unreliable");
                fs.WriteLine($"MaxOpenSpeed={maxOpenSpeed.ToString(_formatProvider)}");

                fs.Flush();
            }
            catch
            {
                Logger.LogWarning("Could not write INI config file!");
            }

            fs?.Close();
        }

        /// <summary>
        /// Function which generates a Shortcut for the module config on the users desktop
        /// </summary>
        public void SaveModuleShortcut()
        {
            // skip all logic if shortcut creation is disabled
            if (!shouldCreateLink)
            {
                Logger.LogInformation($"Link creation disabled, skipping.");
                return;
            }

            try
            {
                var lnkFilename = $"{Path.GetFileNameWithoutExtension(path)}.lnk";
                var shortcutLocation = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    lnkFilename
                );
                if (Path.Exists(shortcutLocation))
                {
                    Logger.LogInformation($"Shortcut to config already exists, skipping shortcut creation.");
                    return;
                }
                var shell = new IWshRuntimeLibrary.WshShell();
                var shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(shortcutLocation);
                shortcut.Description = $"Shortcut to {Path.GetFileName(path)}";   // The description of the shortcut
                shortcut.TargetPath = path;                 // The path of the file that will launch when the shortcut is run
                shortcut.Save();
                Logger.LogInformation($"Successfully created shortcut for {Path.GetFileName(path)}");
                return;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to create shortcut for file: {path}, reason: {ex.Message}");
                return;
            }
        }

        /// <summary>
        /// Loads the current configuration from disk
        /// </summary>
        public void Reload() {
            if (File.Exists(path))
            {
                Logger.LogInformation("Loading config file");
                StreamReader fs = null;
                try
                {
                    fs = new StreamReader(path, Encoding.UTF8);
                    bool correctSection = false, finished = false;
                    float _squeezeT = 0.15f, _widenT = 0.9f;

                    // continue reading the file until we either reach the end, or finished the correct parsing group
                    while (!fs.EndOfStream && !finished)
                    {
                        var line = fs.ReadLine();
                        // Skip comment lines
                        if (line.StartsWith(";") || line.StartsWith("#")) continue;
                        // Start parsing key-value pairs only in the VarjoModule section
                        if (line == "[VarjoModule]")
                        {
                            correctSection = true;
                            continue;
                        }
                        // Once the VarjoModule section is over, mark parsing as finished
                        if (line.StartsWith("[") && correctSection)
                        {
                            finished = true;
                            continue;
                        }

                        if (correctSection && line.Contains("=") && !line.Trim().StartsWith("="))
                        {
                            // parse the keyname and value
                            var name = line[..line.IndexOf('=')].Trim();
                            var value = line.Substring(line.IndexOf("=") + 1).Trim();

                            switch (name.ToLower())
                            {
                                // Implement all valid fields here
                                case "createlink":
                                    shouldCreateLink = ParseStringToBool(value);
                                    break;
                                case "doubletime":
                                    readDelay = ParseStringToBool(value) ? 5 : 10;
                                    break;
                                case "stabalizingcycles":{
                                        if (uint.TryParse(value, _formatProvider, out var pval))
                                        {
                                            stabalizingCycles = pval;
                                        }
                                        else Logger.LogWarning($"{value} not a valid value for StabalizingCycles");
                                        break;
                                    }
                                case "eyelidstrat":
                                    {
                                        var strat = StringToOpennessStrat(value);
                                        if (strat != OpennessStrategy.INVALID)
                                        {
                                            opennessStrategy = strat;
                                        }
                                        else Logger.LogWarning($"{value} is not a valid EyeLidStrat!");
                                        break;
                                    }
                                case "squeezethreshold":
                                    {
                                        if (float.TryParse(value, _formatProvider, out var pval))
                                        {
                                            if (pval < 0 || pval > 1)
                                            {
                                                Logger.LogWarning("SqueezeThreshold may not be <0 or >1");
                                            }
                                            else
                                            {
                                                _squeezeT = pval;
                                            }
                                        }
                                        else Logger.LogWarning($"{value} is not a valid Float! (for SqueezeThreshold)");
                                        break;
                                    }
                                case "widenthreshold":
                                    {
                                        if (float.TryParse(value, _formatProvider, out var pval)) 
                                        {
                                            if (pval < 0 || pval >1)
                                            {
                                                Logger.LogWarning("WidenThreshold may not be <0 or >1");
                                            }
                                            else
                                            {
                                                _widenT = pval;
                                            }
                                        }
                                        else Logger.LogWarning($"{value} is not a valid Float! (for WidenThreshold)");
                                        break;
                                    }
                                case "maxopenspeed":
                                    {
                                        if (float.TryParse(value, _formatProvider, out var pval))
                                        {
                                            if (pval < 0 || pval > 1)
                                            {
                                                Logger.LogWarning("MaxOpenSpeed may not be <0 or >1");
                                            }
                                            maxOpenSpeed = pval;
                                        }
                                        else Logger.LogWarning($"{value} is not a valid Float! (for MaxOpenSpeed)");
                                        break;
                                    }
                                default:
                                    Logger.LogWarning($"Unknown key {name} found with value {value}!");
                                    break;
                            }
                        }
                    }

                    if (_squeezeT > _widenT)
                    {
                        squeezeThreshold = _squeezeT;
                        widenThreshold = _widenT;
                        opennessRange = _widenT - _squeezeT;
                    }
                    else
                    {
                        Logger.LogWarning("SqueezeThreshold may not be larger then WidenThreshold!");
                    }
                }
                catch
                {
                    Logger.LogWarning("Error while parsing INI config file. Continuing with default config.");
                    InitConfig();
                }

                fs?.Close();
            }
        }
        #endregion

        #region Parsers
        /// <summary>
        /// Parses an OpennessStrategy Enum value to it's corresponding string
        /// </summary>
        /// <param name="strat"></param>
        /// <returns></returns>
        private static string OpennessStratToString(OpennessStrategy strat)
        {
            switch (strat)
            {
                case OpennessStrategy.Bool: return "Bool";
                case OpennessStrategy.Stepped: return "Stepped";
                case OpennessStrategy.Raw: return "RawFloat";
                case OpennessStrategy.RestrictedSpeed: return "RestrictedSpeed";
                case OpennessStrategy.Hybrid: return "Hybrid";
            }

            return "INVALID";
        }

        /// <summary>
        /// Parses a string to it's corresponding OpennessStrategy Enum value
        /// </summary>
        /// <param name="strat"></param>
        /// <returns></returns>
        private static OpennessStrategy StringToOpennessStrat(string strat)
        {
            switch (strat)
            {
                case "Bool": return OpennessStrategy.Bool;
                case "Stepped": return OpennessStrategy.Stepped;
                case "RawFloat": return OpennessStrategy.Raw;
                case "RestrictedSpeed": return OpennessStrategy.RestrictedSpeed;
                case "Hybrid": return OpennessStrategy.Hybrid;
            }

            return OpennessStrategy.INVALID;
        }

        /// <summary>
        /// Parses a string *correctly* to a boolean (y'know a string of "false" actually parses to false!)
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        private bool ParseStringToBool(string str)
        {
            if (str.ToLower() == "false" || str == "0") return false;
            else if (str.ToLower() == "true" || str == "1") return true;

            Logger.LogWarning($"{str} not a valid Boolean! Using length as fallback (string length >0 = true)");
            return str.Length > 0;
        }
#endregion
    }

    public enum OpennessStrategy
    {
        INVALID=-1,
        Bool,
        Stepped,
        RestrictedSpeed,
        Raw,
        Hybrid
    }
}
