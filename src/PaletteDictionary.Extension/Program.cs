// COM host pattern follows Microsoft's Command Palette extension template (MIT).
using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer.CsWinRT;

namespace PaletteDictionary.Extension;

public static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 0 || (args[0] != "-RegisterProcessAsComServer" && args[0] != "--smoke-server")) return;
        using var stopped = new ManualResetEvent(false);
        var server = new Shmuelie.WinRTServer.ComServer();
        if (args[0] == "--smoke-server")
        {
            var extension = new SmokeExtension(stopped);
            server.RegisterClass<SmokeExtension, IExtension>(() => extension);
        }
        else
        {
            var extension = new DictionaryExtension(stopped);
            server.RegisterClass<DictionaryExtension, IExtension>(() => extension);
        }
        server.Start();
        stopped.WaitOne();
        server.Stop();
        server.UnsafeDispose();
    }
}
