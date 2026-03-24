namespace UnityExplorer.MCP
{
    /// <summary>
    /// Receives commands from the WebSocket thread, queues them, and dispatches
    /// on Unity's main thread during Update().
    /// </summary>
    internal static class CommandDispatcher
    {
        private static readonly LockedQueue<string> incomingMessages = new();

        private static readonly Dictionary<string, Func<CommandRequest, CommandResponse>> handlers = new();

        internal static void RegisterHandler(string command, Func<CommandRequest, CommandResponse> handler)
        {
            handlers[command] = handler;
        }

        internal static void EnqueueCommand(string rawJson)
        {
            incomingMessages.Enqueue(rawJson);
        }

        internal static void Update()
        {
            int maxPerFrame = 10;
            while (maxPerFrame-- > 0 && incomingMessages.TryDequeue(out string raw))
            {
                CommandResponse response;
                CommandRequest request = null;
                try
                {
                    request = CommandRequest.FromJson(raw);

                    if (handlers.TryGetValue(request.Command, out var handler))
                    {
                        response = handler(request);
                    }
                    else
                    {
                        response = CommandResponse.Fail(request.Id, $"Unknown command: {request.Command}");
                    }
                }
                catch (Exception ex)
                {
                    string id = request?.Id ?? "unknown";
                    response = CommandResponse.Fail(id, $"{ex.GetType().Name}: {ex.Message}");
                    ExplorerCore.LogWarning($"[MCP] Error handling command: {ex}");
                }

                MCPBridge.SendResponse(response);
            }
        }

        internal static void Init()
        {
            Handlers.StatusCommandHandler.Register();
            Handlers.SceneCommandHandler.Register();
            Handlers.HierarchyCommandHandler.Register();
            Handlers.SearchCommandHandler.Register();
            Handlers.InspectionCommandHandler.Register();
            Handlers.ConsoleCommandHandler.Register();
            Handlers.LogCommandHandler.Register();
        }
    }
}
