namespace SimpleBCI.TestApplication
{
    public class Program
    {
        public static void Main()
        {
            Console.Write("Choose test application (1) motor imagery or (2) blink state or (3) JSON-RPC: ");

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