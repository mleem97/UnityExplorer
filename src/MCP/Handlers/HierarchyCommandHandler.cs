namespace UnityExplorer.MCP.Handlers
{
    internal static class HierarchyCommandHandler
    {
        internal static void Register()
        {
            CommandDispatcher.RegisterHandler("get_children", HandleGetChildren);
            CommandDispatcher.RegisterHandler("get_hierarchy", HandleGetHierarchy);
        }

        private static CommandResponse HandleGetChildren(CommandRequest req)
        {
            int instanceId = req.GetInt("instance_id");
            GameObject go = ObjectRegistry.Resolve<GameObject>(instanceId);
            if (go == null)
                return CommandResponse.Fail(req.Id, $"Object with instance ID {instanceId} not found or has been destroyed.");

            var b = new JsonHelper.JsonBuilder();
            b.StartObject().Key("children").StartArray();

            for (int i = 0; i < go.transform.childCount; i++)
            {
                Transform child = go.transform.GetChild(i);
                if (!child) continue;
                int childId = ObjectRegistry.Register(child.gameObject);
                b.StartObject()
                    .Key("instance_id").Value(childId)
                    .Key("name").Value(child.name)
                    .Key("child_count").Value(child.childCount)
                    .Key("active").Value(child.gameObject.activeSelf)
                .EndObject();
            }

            b.EndArray().EndObject();
            return CommandResponse.Ok(req.Id, b.ToString());
        }

        private static CommandResponse HandleGetHierarchy(CommandRequest req)
        {
            int instanceId = req.GetInt("instance_id");
            int depth = req.GetInt("depth", 3);
            GameObject go = ObjectRegistry.Resolve<GameObject>(instanceId);
            if (go == null)
                return CommandResponse.Fail(req.Id, $"Object with instance ID {instanceId} not found or has been destroyed.");

            var b = new JsonHelper.JsonBuilder();
            b.StartObject().Key("tree");
            BuildTree(b, go.transform, depth);
            b.EndObject();
            return CommandResponse.Ok(req.Id, b.ToString());
        }

        private static void BuildTree(JsonHelper.JsonBuilder b, Transform t, int remainingDepth)
        {
            int id = ObjectRegistry.Register(t.gameObject);
            b.StartObject()
                .Key("instance_id").Value(id)
                .Key("name").Value(t.name)
                .Key("active").Value(t.gameObject.activeSelf);

            b.Key("children").StartArray();
            if (remainingDepth > 0)
            {
                for (int i = 0; i < t.childCount; i++)
                {
                    Transform child = t.GetChild(i);
                    if (!child) continue;
                    BuildTree(b, child, remainingDepth - 1);
                }
            }
            b.EndArray();
            b.EndObject();
        }
    }
}
