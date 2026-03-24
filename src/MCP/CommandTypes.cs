namespace UnityExplorer.MCP
{
    /// <summary>Deserialized command request from the MCP server.</summary>
    internal class CommandRequest
    {
        public string Id;
        public string Command;
        public Dictionary<string, object> Params;

        public static CommandRequest FromJson(string json)
        {
            var dict = JsonHelper.Parse(json);
            return new CommandRequest
            {
                Id = JsonHelper.GetString(dict, "id"),
                Command = JsonHelper.GetString(dict, "command"),
                Params = JsonHelper.GetObject(dict, "params") ?? new Dictionary<string, object>()
            };
        }

        // Param accessors
        public string GetString(string key, string fallback = null) => JsonHelper.GetString(Params, key, fallback);
        public int GetInt(string key, int fallback = 0) => JsonHelper.GetInt(Params, key, fallback);
        public bool GetBool(string key, bool fallback = false) => JsonHelper.GetBool(Params, key, fallback);
        public List<object> GetArray(string key) => JsonHelper.GetArray(Params, key);
    }

    /// <summary>Response to send back to the MCP server.</summary>
    internal class CommandResponse
    {
        public string Id;
        public bool Success;
        public string ResultJson;  // pre-built JSON for the result field
        public string Error;

        public static CommandResponse Ok(string id, string resultJson)
            => new CommandResponse { Id = id, Success = true, ResultJson = resultJson };

        public static CommandResponse Fail(string id, string error)
            => new CommandResponse { Id = id, Success = false, Error = error };

        public string ToJson()
        {
            var b = new JsonHelper.JsonBuilder();
            b.StartObject();
            b.Key("id").Value(Id);
            b.Key("success").Value(Success);
            if (Success)
                b.Key("result").Raw(ResultJson ?? "{}");
            else
                b.Key("error").Value(Error ?? "Unknown error");
            b.EndObject();
            return b.ToString();
        }
    }
}
