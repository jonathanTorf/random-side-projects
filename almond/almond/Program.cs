using System.Runtime.InteropServices;
namespace almond
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        [STAThread]
        static void Main()
        {
            AllocConsole();

            Console.WriteLine("Console ready.");

            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }
    }
}