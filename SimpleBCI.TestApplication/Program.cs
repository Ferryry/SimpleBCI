using System;
using System.Linq;
using System.Reflection;

namespace SimpleBCI.TestApplication
{
    public class Program
    {
        private const string TestNamespace = "SimpleBCI.TestApplication.SimpleTests";

        static void Main(string[] args)
        {
            Assembly assembly = typeof(Program).Assembly;

            var testTypes = assembly.GetTypes()
                .Where(t =>
                    t.IsClass &&
                    !t.IsAbstract &&
                    !t.IsInterface &&
                    t.Namespace == TestNamespace &&
                    typeof(ISimpleTestApplication).IsAssignableFrom(t) &&
                    t.GetField(
                        "name",
                        BindingFlags.Public | BindingFlags.Static
                    )?.FieldType == typeof(string) &&
                    t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => new
                {
                    Type = t,
                    Name = (string)t.GetField(
                        "name",
                        BindingFlags.Public | BindingFlags.Static
                    )!.GetValue(null)!
                })
                .OrderBy(t => t.Name)
                .ToList();

            if (testTypes.Count == 0)
            {
                Console.WriteLine("No test applications found.");
                return;
            }

            Console.WriteLine("=== Available test applications ===");

            for (int i = 0; i < testTypes.Count; i++)
            {
                Console.WriteLine($"{i + 1}: {testTypes[i].Name}");
            }

            Console.WriteLine("Q: Quit");
            Console.Write("Select test application: ");

            string? input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input) ||
                input.Equals("q", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var selected = int.TryParse(input, out int index) &&
                           index >= 1 &&
                           index <= testTypes.Count
                ? testTypes[index - 1]
                : testTypes.FirstOrDefault(t =>
                    string.Equals(
                        t.Name,
                        input,
                        StringComparison.OrdinalIgnoreCase));

            if (selected == null)
            {
                Console.WriteLine("Invalid selection.");
                return;
            }

            StartTest(selected.Type);
        }

        private static void StartTest(Type type)
        {
            try
            {
                Console.WriteLine($"\nStarting: {type.Name}");

                if (Activator.CreateInstance(type)
                    is not ISimpleTestApplication instance)
                {
                    Console.WriteLine("Could not create test application.");
                    return;
                }

                instance.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}