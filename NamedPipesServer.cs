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
        private static NamedPipeServerStream listeningPipeServer;
        internal static StreamWriter pipeWriter;
        private static readonly object pipeLock = new object();
        private static bool isServerRunning = false;

        internal static void StartNamedPipeServer(string pipeName)
        {
            lock (pipeLock)
            {
                if (isServerRunning)
                {
                    Logger.Log("Server is already running.", MessageType.normal);
                    return;
                }

                isServerRunning = true;
            }

            // Start the server in a background task
            Task.Run(() => ListenForConnections(pipeName));
        }

        internal static bool IsClientConnected
        {
            get
            {
                lock (pipeLock)
                {
                    return pipeServer?.IsConnected == true;
                }
            }
        }

        internal static void StopNamedPipeServer()
        {
            NamedPipeServerStream connectedPipe = null;
            NamedPipeServerStream pendingPipe = null;
            StreamWriter writer = null;

            lock (pipeLock)
            {
                if (!isServerRunning && pipeServer == null && listeningPipeServer == null)
                {
                    return;
                }

                isServerRunning = false;
                writer = pipeWriter;
                pipeWriter = null;
                connectedPipe = pipeServer;
                pipeServer = null;
                pendingPipe = listeningPipeServer;
                listeningPipeServer = null;
            }

            try
            {
                writer?.Dispose();
            }
            catch
            {
            }

            try
            {
                connectedPipe?.Dispose();
            }
            catch
            {
            }

            try
            {
                if (pendingPipe != null && !ReferenceEquals(pendingPipe, connectedPipe))
                {
                    pendingPipe.Dispose();
                }
            }
            catch
            {
            }
        }

        private static void ListenForConnections(string pipeName)
        {
            while (isServerRunning)
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

                    lock (pipeLock)
                    {
                        if (!isServerRunning)
                        {
                            currentPipeServer.Dispose();
                            return;
                        }

                        listeningPipeServer = currentPipeServer;
                    }

                    Logger.Log("Waiting for pipe client...", MessageType.normal);

                    IAsyncResult waitHandle = currentPipeServer.BeginWaitForConnection(null, null);
                    while (isServerRunning && !waitHandle.AsyncWaitHandle.WaitOne(250))
                    {
                    }

                    if (!isServerRunning)
                    {
                        currentPipeServer.Dispose();
                        return;
                    }

                    currentPipeServer.EndWaitForConnection(waitHandle);
                    Logger.Log("Pipe client connected.", MessageType.normal);

                    // Safely update the shared pipe objects under a lock
                    lock (pipeLock)
                    {
                        listeningPipeServer = null;

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
                    if (isServerRunning)
                    {
                        Logger.Log(
                            $"An IO exception occurred: {ex.Message}",
                            MessageType.namedPipes_error
                        );
                    }
                }
                catch (ObjectDisposedException)
                {
                    if (isServerRunning)
                    {
                        Logger.Log(
                            "Server pipe was disposed unexpectedly.",
                            MessageType.namedPipes_error
                        );
                    }
                }
                catch (Exception ex)
                {
                    if (isServerRunning)
                    {
                        Logger.Log(
                            $"An unexpected error occurred in the server loop: {ex.Message}",
                            MessageType.namedPipes_error
                        );
                    }
                }
                finally
                {
                    lock (pipeLock)
                    {
                        if (ReferenceEquals(listeningPipeServer, currentPipeServer))
                        {
                            listeningPipeServer = null;
                        }
                    }

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
            bool disconnectClient = false;

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
                    disconnectClient = true;
                }
                catch (ObjectDisposedException)
                {
                    Logger.Log(
                        "Pipe writer is disposed. Client has disconnected.",
                        MessageType.namedPipes_error
                    );
                    disconnectClient = true;
                }
                catch (Exception ex)
                {
                    Logger.Log(
                        $"An unexpected error occurred while writing: {ex.Message}",
                        MessageType.namedPipes_error
                    );
                }
            }

            if (disconnectClient)
            {
                DisconnectClient();
            }
        }

        private static void DisconnectClient()
        {
            StreamWriter writer = null;
            NamedPipeServerStream connectedPipe = null;

            lock (pipeLock)
            {
                writer = pipeWriter;
                pipeWriter = null;
                connectedPipe = pipeServer;
                pipeServer = null;
            }

            try
            {
                writer?.Dispose();
            }
            catch
            {
            }

            try
            {
                connectedPipe?.Dispose();
            }
            catch
            {
            }
        }
    }
}
