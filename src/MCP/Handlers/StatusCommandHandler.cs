namespace UnityExplorer.MCP.Handlers
{
    internal static class StatusCommandHandler
    {
        internal static void Register()
        {
            CommandDispatcher.RegisterHandler("game_status", HandleGameStatus);
        }

        private static CommandResponse HandleGameStatus(CommandRequest req)
        {
            float fps = 1f / Time.unscaledDeltaTime;
            var b = new JsonHelper.JsonBuilder();
            b.StartObject()
                .Key("fps").Value(Mathf.RoundToInt(fps))
            .EndObject();
            return CommandResponse.Ok(req.Id, b.ToString());
        }
    }
}
