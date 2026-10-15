using OpenKogama.Api;

namespace SimonSays;

public sealed class SimonSaysPlugin : IPlugin
{
    const string Color = "yellow";

    static readonly string[] Tasks = ["/wave"];

    public void Load(IServer server) =>
        server.AddCommand("simonsays", (_, _) => Give(server, Tasks[0]));

    static void Give(IServer server, string task)
    {
        string text = $"Simon says: {task}";
        server.Broadcast(text, Color);
        server.Notify(text);
    }
}
