using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Sideclip.Cli;

internal static class ConsoleAttach
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint dwProcessId);

    public static void Ensure()
    {
        AttachConsole(AttachParentProcess);
        TryBind(standardError: false);
        TryBind(standardError: true);
    }

    private static void TryBind(bool standardError)
    {
        try
        {
            var stream = standardError ? Console.OpenStandardError() : Console.OpenStandardOutput();
            if (stream is null || !stream.CanWrite)
                return;
            var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
            if (standardError)
                Console.SetError(writer);
            else
                Console.SetOut(writer);
        }
        catch
        {
        }
    }
}
