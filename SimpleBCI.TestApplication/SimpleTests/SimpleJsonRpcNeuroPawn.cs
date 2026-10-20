using System.Net;
using System.Net.Sockets;
using StreamJsonRpc;
using SimpleBCI.API;
using SimpleBCI.API.States;

namespace SimpleBCI.TestApplication.SimpleTests
{
    /// <summary>
    /// Simple JSON-RPC implementation to use this Api.
    /// </summary>
    internal class SimpleJsonRpcNeuroPawn : ISimpleTestApplication
    {
        public static string name = "JsonRPC Test (NeuroPawn)";
        private readonly SimpleBCI instance;
        private readonly int port = 5080;
        private TcpListener listener;
        private CancellationTokenSource cancellationTokenSource;

        public SimpleJsonRpcNeuroPawn()
        {
            string serial = "COM4";
            ChannelInput[] channels =
            {
                new(0, ElectrodePosition.Af7), // NeuroPawn input 1
                new(1, ElectrodePosition.Af8), // NeuroPawn input 2
                new(2, ElectrodePosition.Unspecific),
                new(3, ElectrodePosition.Unspecific),
                new(4, ElectrodePosition.Unspecific),
                new(5, ElectrodePosition.Unspecific),
                new(6, ElectrodePosition.Unspecific),
                new(7, ElectrodePosition.Unspecific),
            };

            instance = new SimpleBCI(
                new SimpleBCIBoardSelection(BoardType.NeuropawnKnightBoard),
                new SimpleBCIParameters(
                    serial,
                    null,
                    null,
                    channels,
                    new NeuroPawnKnightConfiguration(
                    [
                        new(1, Gain: 12),
                        new(2, Gain: 12),
                    ])));

            cancellationTokenSource = new CancellationTokenSource();

            listener = new TcpListener(IPAddress.Any, port);
        }

        private async Task ServerLoop()
        {
            try
            {
                instance.StartAPI();

                while (!cancellationTokenSource.IsCancellationRequested)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(client);
                }
            }
            finally
            {
                listener.Stop();
                listener.Dispose();
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            {
                Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");

                try
                {
                    using JsonRpc rpc = JsonRpc.Attach(
                        client.GetStream(), new ApiHandler(instance));

                    await rpc.Completion;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Client Exception: {ex.Message}");
                }

                Console.WriteLine($"Client disconnected.");
            }
        }

        /// <summary>
        /// Simple ApiHandler layer between JSON-RPC server and SimpleBCI Api.
        /// </summary>
        private class ApiHandler
        {
            private readonly SimpleBCI instance;

            public ApiHandler(SimpleBCI instance)
            {
                this.instance = instance;
            }

            public BlinkState GetBlinkState()
            {
                BlinkState result = instance.GetCurrentBlinkState();

                Console.WriteLine($"GetBlinkState() -> {result}");

                return result;
            }

            public MotorImageryState GetMotorImageryState()
            {
                MotorImageryState result = instance.GetMotorImageryState();

                Console.WriteLine($"GetMotorImageryState() -> {result}");

                return result;
            }
        }

        public void Run()
        {
            listener.Start();

            Console.WriteLine("Starting JSON-RPC server.");
            Console.WriteLine($"Listening on TPC port {port}");
            Console.WriteLine("Press Q to shutdown JSON-RPC server.");

            Task task = ServerLoop();

            while (!cancellationTokenSource.IsCancellationRequested)
            {
                if (Console.KeyAvailable)
                {
                    ConsoleKey key = Console.ReadKey(true).Key;

                    switch (key)
                    {
                        case ConsoleKey.Q:
                            cancellationTokenSource.Cancel();
                            break;
                    }
                }

                Thread.Sleep(10);
            }
        }
    }
}
