using System.Net;
using System.Net.Sockets;
using StreamJsonRpc;
using SimpleBCI.API;
using SimpleBCI.API.States;

namespace SimpleBCI.TestApplication.SimpleTests
{
    /// <summary>
    /// JSON-RPC endpoint for games that use NeuroPawn motor imagery.
    /// </summary>
    internal sealed class SimpleJsonRpcMotorImageryNeuroPawn : ISimpleTestApplication
    {
        public static string name = "JsonRPC Motor Imagery (NeuroPawn | 8 motor electrodes)";

        private const int Port = 5080;
        private const int CalibrationTrials = 10;
        private readonly BciClient instance;
        private readonly TcpListener listener = new(IPAddress.Any, Port);
        private readonly CancellationTokenSource cancellationTokenSource = new();

        public SimpleJsonRpcMotorImageryNeuroPawn()
        {
            ChannelInput[] channels =
            {
                new(0, ElectrodePosition.Fc3), // NeuroPawn input 1
                new(1, ElectrodePosition.Fc4), // NeuroPawn input 2
                new(2, ElectrodePosition.C5),  // NeuroPawn input 3
                new(3, ElectrodePosition.C3),  // NeuroPawn input 4
                new(4, ElectrodePosition.C4),  // NeuroPawn input 5
                new(5, ElectrodePosition.C6),  // NeuroPawn input 6
                new(6, ElectrodePosition.Cp3), // NeuroPawn input 7
                new(7, ElectrodePosition.Cp4), // NeuroPawn input 8
            };

            instance = new BciClient(
                new SimpleBCIBoardSelection(BoardType.NeuropawnKnightBoard),
                new SimpleBCIParameters(
                    "COM4",
                    null,
                    null,
                    channels,
                    new NeuroPawnKnightConfiguration(
                        Enumerable.Range(1, 8)
                            .Select(channel => new NeuroPawnChannelConfiguration(channel, Gain: 12)))));
        }

        private async Task ServerLoop()
        {
            try
            {
                instance.Start();
                Console.WriteLine("Motor imagery board started. Collecting 6 seconds of EEG before calibration.");
                await Task.Delay(6000, cancellationTokenSource.Token);

                for (int i = 0; i < CalibrationTrials; i++)
                {
                    string hand = i % 2 == 0 ? "LEFT" : "RIGHT";
                    Console.WriteLine("Get ready... keep still and look at the screen.");
                    await Task.Delay(1000, cancellationTokenSource.Token);
                    Console.WriteLine($"Calibration [{i + 1}/{CalibrationTrials}]: imagine squeezing your {hand} hand repeatedly.");
                    await Task.Delay(4000, cancellationTokenSource.Token);
                    instance.AddMotorImageryCalibrationTrial();
                    Console.WriteLine("Calibration trial recorded. Rest.");
                    await Task.Delay(2500, cancellationTokenSource.Token);
                }

                instance.FinishMotorImageryCalibration();
                listener.Start();
                Console.WriteLine("Calibration complete. Snake can now connect on TCP port 5080.");

                while (!cancellationTokenSource.IsCancellationRequested)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync(cancellationTokenSource.Token);
                    _ = HandleClientAsync(client);
                }
            }
            catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            finally
            {
                listener.Stop();
                instance.Stop();
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            {
                Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");
                try
                {
                    using JsonRpc rpc = JsonRpc.Attach(client.GetStream(), new ApiHandler(instance));
                    await rpc.Completion;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Client exception: {ex.Message}");
                }

                Console.WriteLine("Client disconnected.");
            }
        }

        private sealed class ApiHandler
        {
            private readonly BciClient instance;

            public ApiHandler(BciClient instance) => this.instance = instance;

            /// <summary>Record one calibration epoch. Call 4 seconds after its cue.</summary>
            public void AddMotorImageryCalibrationTrial()
            {
                instance.AddMotorImageryCalibrationTrial();
                Console.WriteLine("AddMotorImageryCalibrationTrial() -> recorded");
            }

            /// <summary>Finish calibration after at least 6 trials (10 recommended).</summary>
            public void FinishMotorImageryCalibration()
            {
                instance.FinishMotorImageryCalibration();
                Console.WriteLine("FinishMotorImageryCalibration() -> ready");
            }

            public bool IsMotorImageryCalibrated()
            {
                return instance.IsMotorImageryCalibrated;
            }

            /// <summary>Return Left, Right, or Neutral. Call 4 seconds after the game cue.</summary>
            public MotorImageryState GetMotorImageryState()
            {
                MotorImageryState result = instance.GetMotorImageryState();
                Console.WriteLine($"GetMotorImageryState() -> {result}");
                return result;
            }
        }

        public void Run()
        {
            Console.WriteLine("Starting motor imagery board and calibration.");
            Console.WriteLine("RPC methods: IsMotorImageryCalibrated, AddMotorImageryCalibrationTrial, FinishMotorImageryCalibration, GetMotorImageryState");
            Console.WriteLine("Snake RPC access will open after the 10 calibration trials are complete.");
            Console.WriteLine("Press Q to shut down.");

            Task serverTask = ServerLoop();
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
                {
                    cancellationTokenSource.Cancel();
                    listener.Stop();
                    break;
                }

                Thread.Sleep(10);
            }

            serverTask.GetAwaiter().GetResult();
            cancellationTokenSource.Dispose();
        }
    }
}
