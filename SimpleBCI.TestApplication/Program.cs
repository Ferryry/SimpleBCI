namespace SimpleBCI.TestApplication
{
    public class Program
    {
        public static void Main()
        {
            Console.WriteLine(
                String.Format(
                    "Choose test application:\n(1) {0}\n(2) {1}\n(3) {2}\n(4) {3}",
                    "Motor Imagery",
                    "Blink State",
                    "Emotion State",
                    "JSON-RPC + Blink Test"
                )
             );

            switch(Console.ReadKey().Key) {
                case ConsoleKey.D1:
                    SimpleTestApplication simpleTestApplication = new SimpleTestApplication();
                    simpleTestApplication.Run();
                    break;
                case ConsoleKey.D2:
                    SimpleBlinkTestApplication simpleBlinkTestApplication = new SimpleBlinkTestApplication();
                    simpleBlinkTestApplication.Run();
                    break;
                case ConsoleKey.D3:
                    SimpleFaaTestApplication simpleFaaTestApplication = new SimpleFaaTestApplication();
                    simpleFaaTestApplication.Run();
                    break;
                case ConsoleKey.D4:
                    SimpleJsonRpc simpleJsonRpc = new SimpleJsonRpc();
                    simpleJsonRpc.Run();
                    break;
                default:
                    Console.WriteLine("Key not recognized.");
                    break;
            }
        }
    }
}