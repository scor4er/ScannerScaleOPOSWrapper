using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using static Scanner_Scale_OPOS_Wrapper.Constants;

namespace Scanner_Scale_OPOS_Wrapper
{
    class NamedPipesServer
    {
        private static NamedPipeServerStream pipeServer;
        internal static StreamWriter pipeWriter;
        private static readonly object pipeLock = new object();
        private static bool isServerRunning = false;

        internal static void StartNamedPipeServer(string pipeName)
        {
            if (isServerRunning)
            {
                Logger.Log("Server is already running.", MessageType.normal);
                return;
            }

            // Start the server in a background task
            Task.Run(() => ListenForConnections(pipeName));
        }

        private static void ListenForConnections(string pipeName)
        {
            isServerRunning = true;
            while (true)
            {
                // Create a new pipe server instance for each connection
                NamedPipeServerStream currentPipeServer = null;
                try
                {
                    currentPipeServer = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.Out,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Message,
                        PipeOptions.Asynchronous
                    );
                    Logger.Log("Waiting for pipe client...", MessageType.normal);

                    currentPipeServer.WaitForConnection();
                    Logger.Log("Pipe client connected.", MessageType.normal);

                    // Safely update the shared pipe objects under a lock
                    lock (pipeLock)
                    {
                        // Dispose of previous connections if they exist
                        pipeWriter?.Dispose();
                        pipeServer?.Dispose();

                        pipeServer = currentPipeServer;
                        pipeWriter = new StreamWriter(pipeServer, Encoding.UTF8)
                        {
                            AutoFlush = true,
                        };
                    }
                }
                catch (IOException ex)
                {
                    Logger.Log(
                        $"An IO exception occurred: {ex.Message}",
                        MessageType.namedPipes_error
                    );
                }
                catch (ObjectDisposedException)
                {
                    Logger.Log(
                        "Server pipe was disposed unexpectedly.",
                        MessageType.namedPipes_error
                    );
                }
                catch (Exception ex)
                {
                    Logger.Log(
                        $"An unexpected error occurred in the server loop: {ex.Message}",
                        MessageType.namedPipes_error
                    );
                }
                finally
                {
                    // Ensure the temporary pipe server is disposed if an error occurred before the lock was taken
                    if (currentPipeServer != null && currentPipeServer != pipeServer)
                    {
                        currentPipeServer.Dispose();
                    }
                }
            }
        }

        internal static void SendDataToClient(string data)
        {
            // Use a lock to ensure thread-safe access to the pipe objects
            lock (pipeLock)
            {
                try
                {
                    if (pipeWriter != null && pipeServer != null && pipeServer.IsConnected)
                    {
                        pipeWriter.WriteLine(data);
                    }
                    else
                    {
                        Logger.Log(
                            "Pipe is not connected. Message not sent.",
                            MessageType.namedPipes_error
                        );
                    }
                }
                catch (IOException ex)
                {
                    Logger.Log(
                        $"Error writing to pipe: {ex.Message}. Client has likely disconnected.",
                        MessageType.namedPipes_error
                    );
                }
                catch (ObjectDisposedException)
                {
                    Logger.Log(
                        "Pipe writer is disposed. Client has disconnected.",
                        MessageType.namedPipes_error
                    );
                }
                catch (Exception ex)
                {
                    Logger.Log(
                        $"An unexpected error occurred while writing: {ex.Message}",
                        MessageType.namedPipes_error
                    );
                }
            }
        }
    }
}
