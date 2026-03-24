using UnityExplorer.ObjectExplorer;

namespace UnityExplorer.MCP.Handlers
{
    internal static class SearchCommandHandler
    {
        internal static void Register()
        {
            CommandDispatcher.RegisterHandler("search_objects", HandleSearchObjects);
            CommandDispatcher.RegisterHandler("search_classes", HandleSearchClasses);
            CommandDispatcher.RegisterHandler("search_singletons", HandleSearchSingletons);
        }

        private static CommandResponse HandleSearchObjects(CommandRequest req)
        {
            string nameFilter = req.GetString("name_filter");
            string typeFilter = req.GetString("type_filter");
            string sceneFilterStr = req.GetString("scene_filter", "any");
            string childFilterStr = req.GetString("child_filter", "any");
            int maxResults = req.GetInt("max_results", 100);

            SceneFilter sceneFilter = ParseSceneFilter(sceneFilterStr);
            ChildFilter childFilter = ParseChildFilter(childFilterStr);

            List<object> results = SearchProvider.UnityObjectSearch(nameFilter, typeFilter, childFilter, sceneFilter);

            bool truncated = results.Count > maxResults;
            if (truncated) results = results.GetRange(0, maxResults);

            var b = new JsonHelper.JsonBuilder();
            b.StartObject().Key("results").StartArray();

            foreach (object obj in results)
            {
                if (obj is UnityEngine.Object unityObj && unityObj)
                {
                    int id = ObjectRegistry.Register(unityObj);
                    string sceneName = "";
                    if (unityObj is GameObject go)
                        sceneName = go.scene.name ?? "";
                    else if (unityObj is Component comp && comp.gameObject)
                        sceneName = comp.gameObject.scene.name ?? "";

                    b.StartObject()
                        .Key("instance_id").Value(id)
                        .Key("name").Value(unityObj.name)
                        .Key("type").Value(unityObj.GetActualType().FullName)
                        .Key("scene").Value(sceneName)
                    .EndObject();
                }
            }

            b.EndArray()
                .Key("truncated").Value(truncated)
            .EndObject();
            return CommandResponse.Ok(req.Id, b.ToString());
        }

        private static CommandResponse HandleSearchClasses(CommandRequest req)
        {
            string nameFilter = req.GetString("name_filter");
            int maxResults = req.GetInt("max_results", 100);

            if (string.IsNullOrEmpty(nameFilter))
                return CommandResponse.Fail(req.Id, "name_filter is required");

            List<object> results = SearchProvider.ClassSearch(nameFilter);

            bool truncated = results.Count > maxResults;
            if (truncated) results = results.GetRange(0, maxResults);

            var b = new JsonHelper.JsonBuilder();
            b.StartObject().Key("results").StartArray();

            foreach (object obj in results)
            {
                if (obj is Type type)
                {
                    b.StartObject()
                        .Key("full_name").Value(type.FullName)
                        .Key("assembly").Value(type.Assembly.GetName().Name)
                        .Key("namespace").Value(type.Namespace ?? "")
                    .EndObject();
                }
            }

            b.EndArray()
                .Key("truncated").Value(truncated)
            .EndObject();
            return CommandResponse.Ok(req.Id, b.ToString());
        }

        private static CommandResponse HandleSearchSingletons(CommandRequest req)
        {
            string typeFilter = req.GetString("type_filter");
            int maxResults = req.GetInt("max_results", 50);

            if (string.IsNullOrEmpty(typeFilter))
                return CommandResponse.Fail(req.Id, "type_filter is required");

            List<object> results = SearchProvider.InstanceSearch(typeFilter);

            bool truncated = results.Count > maxResults;
            if (truncated) results = results.GetRange(0, maxResults);

            var b = new JsonHelper.JsonBuilder();
            b.StartObject().Key("results").StartArray();

            foreach (object obj in results)
            {
                if (obj == null) continue;
                int refId;
                if (obj is UnityEngine.Object unityObj)
                    refId = ObjectRegistry.Register(unityObj);
                else
                    refId = ObjectRegistry.RegisterManaged(obj);

                b.StartObject()
                    .Key("ref_id").Value(refId)
                    .Key("type").Value(obj.GetActualType().FullName)
                    .Key("value_summary").Value(obj.ToString())
                .EndObject();
            }

            b.EndArray()
                .Key("truncated").Value(truncated)
            .EndObject();
            return CommandResponse.Ok(req.Id, b.ToString());
        }

        private static SceneFilter ParseSceneFilter(string s)
        {
            switch (s?.ToLowerInvariant())
            {
                case "actively_loaded": return SceneFilter.ActivelyLoaded;
                case "dont_destroy_on_load": return SceneFilter.DontDestroyOnLoad;
                case "hide_and_dont_save": return SceneFilter.HideAndDontSave;
                default: return SceneFilter.Any;
            }
        }

        private static ChildFilter ParseChildFilter(string s)
        {
            switch (s?.ToLowerInvariant())
            {
                case "root_object": return ChildFilter.RootObject;
                case "has_parent": return ChildFilter.HasParent;
                default: return ChildFilter.Any;
            }
        }
    }
}
