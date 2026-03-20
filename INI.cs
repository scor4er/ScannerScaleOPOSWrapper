using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Scanner_Scale_OPOS_Wrapper
{
    internal class INI
    {
        public int Debug { get; private set; }
        public string ScannerLogicalName { get; private set; }
        public string ScaleLogicalName { get; private set; }
        public int ScaleEnabled { get; private set; }
        public Constants.RuntimeMode Mode { get; private set; }
        public string PipeName { get; private set; }

        public INI()
        {
            ReadINI();
        }

        private void ReadINI()
        {
            try
            {
                IConfiguration config = new ConfigurationBuilder()
                    .AddIniFile("Settings.ini")
                    .Build();

                IConfigurationSection section = config.GetSection("GENERAL");

                ScannerLogicalName = section["SCANNER_NAME"] ?? "DefaultScanner";
                ScaleLogicalName = section["SCALE_NAME"] ?? "DefaultScale";
                PipeName = string.IsNullOrWhiteSpace(section["PIPE_NAME"])
                    ? Constants.DefaultPipeName
                    : section["PIPE_NAME"].Trim();

                if (
                    !Enum.TryParse(
                        section["MODE"],
                        ignoreCase: true,
                        result: out Constants.RuntimeMode mode
                    )
                )
                {
                    mode = Constants.DefaultRuntimeMode;
                }
                Mode = mode;

                // Use TryParse for safer conversion
                ScaleEnabled = int.TryParse(section["SCALE_ENABLED"], out int scaleEnabled)
                    ? scaleEnabled
                    : 1;
                Debug = int.TryParse(section["DEBUG"], out int debug) ? debug : 1;

                LogLoadedValues();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error reading INI file: {ex.Message}", Constants.MessageType.ini);
                SetDefaults(); // Fallback to defaults
            }
        }

        private void LogLoadedValues()
        {
            Logger.Log(
                $"INI Scanner Logical Name: {ScannerLogicalName}",
                Constants.MessageType.normal
            );
            Logger.Log($"INI Scale Logical Name: {ScaleLogicalName}", Constants.MessageType.normal);
            Logger.Log($"INI Scale Enabled: {ScaleEnabled}", Constants.MessageType.normal);
            Logger.Log($"INI Debug Level: {Debug}", Constants.MessageType.normal);
            Logger.Log($"INI Runtime Mode: {Mode}", Constants.MessageType.normal);
            Logger.Log($"INI Pipe Name: {PipeName}", Constants.MessageType.normal);
        }

        private void SetDefaults()
        {
            ScannerLogicalName = "ZEBRA_SCANNER";
            ScaleLogicalName = "ZEBRA_SCALE";
            ScaleEnabled = 1;
            Debug = 1;
            Mode = Constants.DefaultRuntimeMode;
            PipeName = Constants.DefaultPipeName;
            Logger.Log("Using default configuration values", Constants.MessageType.normal);
            LogLoadedValues();
        }
    }
}
