using UnityExplorer.UI.Panels;

namespace UnityExplorer.MCP.Handlers
{
    internal static class LogCommandHandler
    {
        internal static void Register()
        {
            CommandDispatcher.RegisterHandler("get_logs", HandleGetLogs);
        }

        private static CommandResponse HandleGetLogs(CommandRequest req)
        {
            int count = req.GetInt("count", 50);
            string logTypeFilter = req.GetString("log_type", "all");

            // Access logs via reflection since LogPanel.Logs is private
            FieldInfo logsField = typeof(LogPanel).GetField("Logs", BindingFlags.NonPublic | BindingFlags.Static);
            if (logsField == null)
                return CommandResponse.Fail(req.Id, "Cannot access log data.");

            var allLogs = (List<LogPanel.LogInfo>)logsField.GetValue(null);

            var b = new JsonHelper.JsonBuilder();
            b.StartObject().Key("logs").StartArray();

            int added = 0;
            // Iterate from most recent
            for (int i = allLogs.Count - 1; i >= 0 && added < count; i--)
            {
                LogPanel.LogInfo log = allLogs[i];

                if (logTypeFilter != "all")
                {
                    bool match = logTypeFilter switch
                    {
                        "log" => log.type == LogType.Log,
                        "warning" => log.type == LogType.Warning || log.type == LogType.Assert,
                        "error" => log.type == LogType.Error || log.type == LogType.Exception,
                        _ => true
                    };
                    if (!match) continue;
                }

                b.StartObject()
                    .Key("message").Value(log.message)
                    .Key("type").Value(log.type.ToString())
                .EndObject();
                added++;
            }

            b.EndArray().EndObject();
            return CommandResponse.Ok(req.Id, b.ToString());
        }
    }
}
