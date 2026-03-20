using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Scanner_Scale_OPOS_Wrapper.Constants;

namespace Scanner_Scale_OPOS_Wrapper
{
    internal class Logger
    {
        //This is a thread safety object
        private static readonly object _lockObject = new object();
        public static int debug;
        public static bool forceConsoleOutput;

        public static void Log(string message, MessageType messageType)
        {
            lock (_lockObject)
            {
                bool writeToConsole = debug == 1 || forceConsoleOutput;

                string dateTime = DateTime.Now.ToString();
                string dateOnly = DateTime.Now.ToString("yyyy-MM-dd");
                string path = $".\\Logs\\log_{dateOnly}.txt";

                // Ensure the Logs directory exists
                if (!Directory.Exists(".\\Logs"))
                {
                    Directory.CreateDirectory(".\\Logs");
                }

                // Delete log files older than 7 days
                for (int i = 1; i < 8; i++)
                {
                    string oldLogPath = $".\\Logs\\log_{DateTime.Now.AddDays(-i).ToString("yyyy-MM-dd")}.txt";
                    if (File.Exists(oldLogPath))
                    {
                        try
                        {
                            File.Delete(oldLogPath);
                        }
                        catch (Exception ex)
                        {
                            // If deletion fails, log the error to the current log file
                            File.AppendAllLines(
                                $".\\log_{dateOnly}.txt",
                                new[] { $"{dateTime} - ERROR DELETING OLD LOG FILE - {ex.Message}" }
                            );
                        }
                    }
                }

                
                switch (messageType)
                {
                    case MessageType.normal:
                        File.AppendAllLines(path, new[] { $"{dateTime} - {message}" });
                        if (writeToConsole)
                            Console.WriteLine(message);
                        break;

                    case MessageType.scale_error:
                        File.AppendAllLines(
                            path,
                            new[] { $"{dateTime} - SCALE ERROR - {message}" }
                        );
                        if (writeToConsole)
                            Console.WriteLine(message);
                        break;

                    case MessageType.scanner_error:
                        File.AppendAllLines(
                            path,
                            new[] { $"{dateTime} - SCANNER ERROR - {message}" }
                        );
                        if (writeToConsole)
                            Console.WriteLine(message);
                        break;

                    case MessageType.ini:
                        File.AppendAllLines(
                            path,
                            new[] { $"{dateTime} - INI READ ERROR - {message}" }
                        );
                        if (writeToConsole)
                            Console.WriteLine(message);
                        break;
                    case MessageType.namedPipes_error:
                        File.AppendAllLines(
                            path,
                            new[] { $"{dateTime} - NAMED PIPES ERROR - {message}" }
                        );
                        if (writeToConsole)
                            Console.WriteLine(message);
                        break;

                    case MessageType.consoleOnly:
                        if (writeToConsole)
                            Console.WriteLine(message);
                        break;

                    default:
                        File.AppendAllLines(
                            path,
                            new[] { $"{dateTime} - MISC MSG/ERROR - {message}" }
                        );
                        if (writeToConsole)
                            Console.WriteLine(message);
                        break;
                }
            }
        }
    }
}
