using System.IO;

namespace ArcheCore.WorldServer
{
    public static class ServerPaths
    {
        public static readonly string Root =
            Directory.GetCurrentDirectory();

        public static readonly string Config =
            Path.Combine(
                Root,
                "Config");

        public static readonly string Logs =
            Path.Combine(
                Root,
                "Logs");

        public static readonly string Data =
            Path.Combine(
                Root,
                "Data");

        public static readonly string Lua = Path.Combine(Root, "Lua", "Server");

        public static void Initialize()
        {
            Directory.CreateDirectory(Config);
            Directory.CreateDirectory(Logs);
            Directory.CreateDirectory(Data);
            Directory.CreateDirectory(Lua);
        }
    }
}