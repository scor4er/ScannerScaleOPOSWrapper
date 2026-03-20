using System;
using System.Globalization;
using System.Text;
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
            Logger.forceConsoleOutput = ini.Mode == RuntimeMode.EMULATOR;
            ConfigureConsoleVisibility(ini);

            var exitEvent = new ManualResetEvent(false);
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                if (!exitEvent.WaitOne(0))
                {
                    Logger.Log("Shutdown requested. Stopping wrapper...", MessageType.normal);
                    exitEvent.Set();
                }
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
                NamedPipesServer.StopNamedPipeServer();
                CleanupDevices();
                Logger.Log("Wrapper stopped.", MessageType.normal);
            }
        }

        private static void ConfigureConsoleVisibility(INI ini)
        {
            IntPtr handle = GetConsoleWindow();

            if (ini.Mode == RuntimeMode.EMULATOR || ini.Debug == 1)
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
            Console.Title = "Scanner Scale OPOS Wrapper - Emulator";
            Logger.Log($"Runtime mode: {ini.Mode}", MessageType.normal);
            NamedPipesServer.StartNamedPipeServer(ini.PipeName);
            PrintEmulatorWelcome(ini.PipeName);

            while (!exitEvent.WaitOne(0))
            {
                Console.Write("emulator> ");
                string command = ReadInteractiveCommand(exitEvent);
                if (command == null)
                {
                    Console.WriteLine();
                    break;
                }

                HandleEmulatorCommand(command, exitEvent, ini.PipeName);
            }

            Logger.Log("Emulator mode stopped.", MessageType.normal);
        }

        private static void HandleEmulatorCommand(
            string commandLine,
            ManualResetEvent exitEvent,
            string pipeName
        )
        {
            string command = commandLine?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(command))
            {
                return;
            }

            if (command.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                PrintEmulatorHelp();
                return;
            }

            if (
                command.Equals("exit", StringComparison.OrdinalIgnoreCase)
                || command.Equals("quit", StringComparison.OrdinalIgnoreCase)
            )
            {
                Logger.Log("Exit requested from emulator console.", MessageType.normal);
                exitEvent.Set();
                return;
            }

            if (
                command.Equals("status", StringComparison.OrdinalIgnoreCase)
                || command.Equals("pipe", StringComparison.OrdinalIgnoreCase)
            )
            {
                Logger.Log(
                    $"Pipe client connected: {(NamedPipesServer.IsClientConnected ? "yes" : "no")}",
                    MessageType.normal
                );
                return;
            }

            if (
                command.Equals("clear", StringComparison.OrdinalIgnoreCase)
                || command.Equals("cls", StringComparison.OrdinalIgnoreCase)
            )
            {
                Console.Clear();
                PrintEmulatorWelcome(pipeName);
                return;
            }

            if (command.StartsWith("scan ", StringComparison.OrdinalIgnoreCase))
            {
                string barcode = command.Substring(5).Trim();
                if (string.IsNullOrEmpty(barcode))
                {
                    Logger.Log("Usage: scan <barcode>", MessageType.misc);
                    return;
                }

                Logger.Log($"\n[SCAN] {barcode}", MessageType.scanner_error);
                NamedPipesServer.SendDataToClient($"SCAN:{barcode}");
                return;
            }

            if (command.StartsWith("weight ", StringComparison.OrdinalIgnoreCase))
            {
                string rawWeight = command.Substring(7).Trim();
                if (!TryParseWeight(rawWeight, out double weightInPounds))
                {
                    Logger.Log("Usage: weight <lb>", MessageType.misc);
                    return;
                }

                string formattedWeight = WeightFormat(weightInPounds);
                Logger.Log(formattedWeight, MessageType.consoleOnly);
                NamedPipesServer.SendDataToClient($"WEIGHT:{formattedWeight}");
                return;
            }

            Logger.Log("Unknown emulator command. Type 'help' for usage.", MessageType.misc);
        }

        private static string ReadInteractiveCommand(WaitHandle exitEvent)
        {
            if (Console.IsInputRedirected)
            {
                return Console.ReadLine();
            }

            StringBuilder buffer = new StringBuilder();

            while (!exitEvent.WaitOne(50))
            {
                while (Console.KeyAvailable)
                {
                    ConsoleKeyInfo keyInfo = Console.ReadKey(intercept: true);

                    if (keyInfo.Key == ConsoleKey.Enter)
                    {
                        Console.WriteLine();
                        return buffer.ToString();
                    }

                    if (keyInfo.Key == ConsoleKey.Backspace)
                    {
                        if (buffer.Length > 0)
                        {
                            buffer.Length--;
                            Console.Write("\b \b");
                        }

                        continue;
                    }

                    if (keyInfo.Key == ConsoleKey.Escape)
                    {
                        while (buffer.Length > 0)
                        {
                            buffer.Length--;
                            Console.Write("\b \b");
                        }

                        continue;
                    }

                    if (!char.IsControl(keyInfo.KeyChar))
                    {
                        buffer.Append(keyInfo.KeyChar);
                        Console.Write(keyInfo.KeyChar);
                    }
                }
            }

            return null;
        }

        private static void PrintEmulatorWelcome(string pipeName)
        {
            Logger.Log(string.Empty, MessageType.consoleOnly);
            Logger.Log("Scanner Scale OPOS Wrapper Emulator", MessageType.normal);
            if (!string.IsNullOrWhiteSpace(pipeName))
            {
                Logger.Log($"Named pipe server: {pipeName}", MessageType.normal);
            }

            Logger.Log("Interactive commands are ready.", MessageType.normal);
            PrintEmulatorHelp();
            Logger.Log("Press Ctrl+C or type exit to stop", MessageType.normal);
            Logger.Log(string.Empty, MessageType.consoleOnly);
        }

        private static void PrintEmulatorHelp()
        {
            Logger.Log("Available emulator commands:", MessageType.normal);
            Logger.Log("  scan <barcode>", MessageType.normal);
            Logger.Log("  weight <lb>", MessageType.normal);
            Logger.Log("  status", MessageType.normal);
            Logger.Log("  clear", MessageType.normal);
            Logger.Log("  help", MessageType.normal);
            Logger.Log("  exit", MessageType.normal);
        }

        private static bool TryParseWeight(string rawWeight, out double weightInPounds)
        {
            return double.TryParse(
                    rawWeight,
                    NumberStyles.Float | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out weightInPounds
                )
                || double.TryParse(
                    rawWeight,
                    NumberStyles.Float | NumberStyles.AllowLeadingSign,
                    CultureInfo.CurrentCulture,
                    out weightInPounds
                );
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
                NamedPipesServer.SendDataToClient($"WEIGHT:{WeightFormat(scale.ScaleLiveWeight)}");
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
            string units = UnitAbbreviation(scale.WeightUnits);
            if (units == string.Empty)
            {
                return "Unknown weight unit";
            }

            double dWeight = 0.001 * (double)weight;
            return WeightFormat(dWeight, units);
        }

        private static string WeightFormat(double weightInPounds)
        {
            return WeightFormat(weightInPounds, "lb.");
        }

        private static string WeightFormat(double weight, string units)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.000} {1}",
                weight,
                units
            );
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
