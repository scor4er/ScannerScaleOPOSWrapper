using System;
using System.Diagnostics.Eventing.Reader;
using System.Threading;
using OPOSCONSTANTSLib;
using OposScale_CCO;
using OposScanner_CCO;
using System.Runtime.InteropServices;
using static Scanner_Scale_OPOS_Wrapper.Constants;

namespace Scanner_Scale_OPOS_Wrapper
{
    class Program
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_HIDE = 0;

        private static OPOSScanner scanner;
        private static OPOSScale scale;

        static void Main(string[] args)
        {
            // Read INI configuration
            INI ini = new INI();
            Logger.debug = ini.Debug;
            ConfigureConsoleVisibility(ini);

            var exitEvent = new ManualResetEvent(false);
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                exitEvent.Set();
            };

            try
            {
                switch (ini.Mode)
                {
                    case RuntimeMode.OPOS:
                        RunOpos(ini, exitEvent);
                        break;
                    case RuntimeMode.EMULATOR:
                        RunEmulator(ini, exitEvent);
                        break;
                    default:
                        Logger.Log(
                            $"Unsupported runtime mode '{ini.Mode}'. Falling back to OPOS.",
                            MessageType.misc
                        );
                        RunOpos(ini, exitEvent);
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error: {ex.Message}", MessageType.misc);
            }
            finally
            {
                CleanupDevices();
            }
        }

        private static void ConfigureConsoleVisibility(INI ini)
        {
            IntPtr handle = GetConsoleWindow();

            if (ini.Debug == 1)
            {
                ShowWindow(handle, 5); // 5 = SW_SHOW
            }
            else
            {
                ShowWindow(handle, SW_HIDE);
            }
        }

        private static void RunOpos(INI ini, ManualResetEvent exitEvent)
        {
            Logger.Log($"Runtime mode: {ini.Mode}", MessageType.normal);

            // Initialize Scanner
            if (InitializeScanner(ini))
            {
                Logger.Log("Scanner initialized successfully", MessageType.normal);
            }
            else
            {
                Logger.Log("Scanner initialization failed", MessageType.scanner_error);
            }

            // Initialize Scale
            if (ini.ScaleEnabled == 1 && InitializeScale(ini))
            {
                Logger.Log("Scale initialized successfully", MessageType.normal);
            }
            else
            {
                Logger.Log("Scale initialization failed", MessageType.scale_error);
            }

            if (scanner?.Claimed == true || scale?.Claimed == true)
            {
                NamedPipesServer.StartNamedPipeServer(ini.PipeName);
                if (ini.ScaleEnabled == 1)
                {
                    Logger.Log(
                        "\nDevice(s) ready. Scanner: scan barcodes | Scale: live weight monitoring",
                        MessageType.normal
                    );
                    Logger.Log($"Named pipe server: {ini.PipeName}", MessageType.normal);
                    Logger.Log("Press Ctrl+C to exit", MessageType.normal);
                }
                else
                {
                    Logger.Log("\nDevice(s) ready. Scanner: scan barcodes", MessageType.normal);
                    Logger.Log($"Named pipe server: {ini.PipeName}", MessageType.normal);
                    Logger.Log("Press Ctrl+C to exit", MessageType.normal);
                }

                exitEvent.WaitOne();
            }
        }

        private static void RunEmulator(INI ini, ManualResetEvent exitEvent)
        {
            Logger.Log($"Runtime mode: {ini.Mode}", MessageType.normal);
            Logger.Log("Emulator mode is not implemented yet.", MessageType.misc);
            Logger.Log("Press Ctrl+C to exit", MessageType.normal);

            exitEvent.WaitOne();
        }

        static bool InitializeScanner(INI ini)
        {
            try
            {
                Type scannerType = Type.GetTypeFromProgID("OPOS.Scanner");
                scanner = (OPOSScanner)Activator.CreateInstance(scannerType);

                int result = scanner.Open(ini.ScannerLogicalName);
                if (result != 0)
                {
                    Logger.Log($"Failed to open scanner: {result}", MessageType.scanner_error);
                    return false;
                }

                result = scanner.ClaimDevice(1000);
                if (result != 0)
                {
                    Logger.Log($"Failed to claim scanner: {result}", MessageType.scanner_error);
                    return false;
                }

                if (scanner.Claimed)
                {
                    scanner.DataEvent += ScannerDataEvent;
                    scanner.DeviceEnabled = true;
                    scanner.DataEventEnabled = true;
                    scanner.DecodeData = true;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Log(
                    $"Scanner initialization error: {ex.Message}",
                    MessageType.scanner_error
                );
            }
            return false;
        }

        static bool InitializeScale(INI ini)
        {
            try
            {
                Type scaleType = Type.GetTypeFromProgID("OPOS.Scale");
                scale = (OPOSScale)Activator.CreateInstance(scaleType);

                int result = scale.Open(ini.ScaleLogicalName);
                if (result != 0)
                {
                    Logger.Log($"Failed to open scale: {result}", MessageType.scale_error);
                    return false;
                }

                result = scale.ClaimDevice(1000);
                if (result != 0)
                {
                    Logger.Log($"Failed to claim scale: {result}", MessageType.scale_error);
                    return false;
                }

                if (scale.Claimed)
                {
                    // Enable live weighing
                    scale.StatusNotify = (int)OPOSScaleConstants.SCAL_SN_ENABLED;

                    if (scale.ResultCode == (int)OPOS_Constants.OPOS_SUCCESS) // OPOS_SUCCESS
                    {
                        scale.StatusUpdateEvent += ScaleStatusUpdateEvent;
                        scale.DeviceEnabled = true;
                        scale.DataEventEnabled = true;

                        Logger.Log(
                            $"Scale max weight: {scale.MaximumWeight / 1000.0:F3} lbs",
                            MessageType.normal
                        );
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Scale initialization error: {ex.Message}", MessageType.scale_error);
            }
            return false;
        }

        static void ScannerDataEvent(int value)
        {
            try
            {
                Logger.Log($"\n[SCAN] {scanner.ScanDataLabel}", MessageType.scanner_error);
                NamedPipesServer.SendDataToClient($"SCAN:{scanner.ScanDataLabel}");

                scanner.DataEventEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Scanner event error: {ex.Message}", MessageType.scanner_error);
            }
        }

        private static void ScaleStatusUpdateEvent(int value)
        {
            int status = (int)scale.ResultCode;

            if (value == (int)OPOSScaleConstants.SCAL_SUE_STABLE_WEIGHT)
            {
                Logger.Log(WeightFormat(scale.ScaleLiveWeight), MessageType.consoleOnly);
                NamedPipesServer.pipeWriter?.WriteLine(
                    $"WEIGHT:{WeightFormat(scale.ScaleLiveWeight)}"
                );
            }
            else if (value == (int)OPOSScaleConstants.SCAL_SUE_WEIGHT_UNSTABLE)
            {
                Logger.Log("Scale weight unstable", MessageType.scale_error);
            }
            else if (value == (int)OPOSScaleConstants.SCAL_SUE_WEIGHT_ZERO)
            {
                Logger.Log(WeightFormat(scale.ScaleLiveWeight), MessageType.consoleOnly);
                NamedPipesServer.SendDataToClient($"WEIGHT:{WeightFormat(scale.ScaleLiveWeight)}");
            }
            else if (value == (int)OPOSScaleConstants.SCAL_SUE_WEIGHT_OVERWEIGHT)
            {
                Logger.Log("Weight limit exceeded.", MessageType.scale_error);
            }
            else if (value == (int)OPOSScaleConstants.SCAL_SUE_NOT_READY)
            {
                Logger.Log("Scale not ready.", MessageType.scale_error);
            }
            else if (value == (int)OPOSScaleConstants.SCAL_SUE_WEIGHT_UNDER_ZERO)
            {
                Logger.Log("Scale under zero weight.", MessageType.scale_error);
            }
            else
            {
                Logger.Log($"Unknown status [0]: {value}", MessageType.scale_error);
            }
        }

        //Helper function to format weight
        private static string WeightFormat(int weight)
        {
            string weightStr = string.Empty;

            string units = UnitAbbreviation(scale.WeightUnits);
            if (units == string.Empty)
            {
                weightStr = string.Format("Unknown weight unit");
            }
            else
            {
                double dWeight = 0.001 * (double)weight;
                weightStr = string.Format("{0:0.000} {1}", dWeight, units);
            }

            return weightStr;
        }

        //Helper function to get proper UOM from scale
        private static string UnitAbbreviation(int units)
        {
            string unitStr = string.Empty;

            switch ((OPOSScaleConstants)units)
            {
                case OPOSScaleConstants.SCAL_WU_GRAM:
                    unitStr = "gr.";
                    break;
                case OPOSScaleConstants.SCAL_WU_KILOGRAM:
                    unitStr = "kg.";
                    break;
                case OPOSScaleConstants.SCAL_WU_OUNCE:
                    unitStr = "oz.";
                    break;
                case OPOSScaleConstants.SCAL_WU_POUND:
                    unitStr = "lb.";
                    break;
            }

            return unitStr;
        }

        static void CleanupDevices()
        {
            try
            {
                if (scanner?.Claimed == true)
                {
                    scanner.DataEvent -= ScannerDataEvent;
                    scanner.DataEventEnabled = false;
                    scanner.DeviceEnabled = false;
                    scanner.ReleaseDevice();
                    scanner.Close();
                    Logger.Log("Scanner closed", MessageType.normal);
                }

                if (scale?.Claimed == true)
                {
                    scale.StatusUpdateEvent -= ScaleStatusUpdateEvent;
                    scale.DataEventEnabled = false;
                    scale.DeviceEnabled = false;
                    scale.ReleaseDevice();
                    scale.Close();
                    Logger.Log("Scale closed", MessageType.normal);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Cleanup error: {ex.Message}", MessageType.misc);
            }
        }
    }
}
