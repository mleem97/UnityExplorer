using UnityEngine.SceneManagement;
using UnityExplorer.ObjectExplorer;

namespace UnityExplorer.MCP.Handlers
{
    internal static class SceneCommandHandler
    {
        internal static void Register()
        {
            CommandDispatcher.RegisterHandler("list_scenes", HandleListScenes);
            CommandDispatcher.RegisterHandler("load_scene", HandleLoadScene);
            CommandDispatcher.RegisterHandler("get_scene_objects", HandleGetSceneObjects);
        }

        private static CommandResponse HandleListScenes(CommandRequest req)
        {
            var b = new JsonHelper.JsonBuilder();
            b.StartObject().Key("scenes").StartArray();

            foreach (Scene scene in SceneHandler.LoadedScenes)
            {
                int rootCount = 0;
                try { if (scene.IsValid()) rootCount = scene.rootCount; } catch { }

                b.StartObject()
                    .Key("name").Value(scene.name ?? $"Scene_{scene.handle}")
                    .Key("build_index").Value(scene.buildIndex)
                    .Key("is_loaded").Value(true)
                    .Key("root_count").Value(rootCount)
                .EndObject();
            }

            b.EndArray();

            // Also include available scene names from build settings
            b.Key("available_scenes").StartArray();
            foreach (string name in SceneHandler.AllSceneNames)
                b.Value(name);
            b.EndArray();

            b.EndObject();
            return CommandResponse.Ok(req.Id, b.ToString());
        }

        private static CommandResponse HandleLoadScene(CommandRequest req)
        {
            string sceneName = req.GetString("scene_name");
            if (string.IsNullOrEmpty(sceneName))
                return CommandResponse.Fail(req.Id, "scene_name is required");

            string modeStr = req.GetString("mode", "single");
            LoadSceneMode mode = modeStr == "additive" ? LoadSceneMode.Additive : LoadSceneMode.Single;

            SceneManager.LoadScene(sceneName, mode);

            var b = new JsonHelper.JsonBuilder();
            b.StartObject()
                .Key("success").Value(true)
                .Key("scene_name").Value(sceneName)
            .EndObject();
            return CommandResponse.Ok(req.Id, b.ToString());
        }

        private static CommandResponse HandleGetSceneObjects(CommandRequest req)
        {
            string sceneName = req.GetString("scene_name");

            // Determine which scene to query without mutating SelectedScene
            IEnumerable<GameObject> rootObjects;
            if (!string.IsNullOrEmpty(sceneName))
            {
                Scene? target = null;
                foreach (Scene s in SceneHandler.LoadedScenes)
                {
                    if (s.name == sceneName)
                    {
                        target = s;
                        break;
                    }
                }
                if (!target.HasValue)
                    return CommandResponse.Fail(req.Id, $"Scene '{sceneName}' not found or not loaded.");

                Scene scene = target.Value;
                if (scene.IsValid())
                    rootObjects = RuntimeHelper.GetRootGameObjects(scene);
                else
                {
                    // For virtual scenes (DontDestroyOnLoad, HideAndDontSave), get all root objects
                    var list = new List<GameObject>();
                    UnityEngine.Object[] allObjects = RuntimeHelper.FindObjectsOfTypeAll(typeof(GameObject));
                    foreach (UnityEngine.Object obj in allObjects)
                    {
                        GameObject go = obj.TryCast<GameObject>();
                        if (go && go.transform.parent == null && !go.scene.IsValid())
                            list.Add(go);
                    }
                    rootObjects = list;
                }
            }
            else
            {
                rootObjects = SceneHandler.CurrentRootObjects;
            }

            var b = new JsonHelper.JsonBuilder();
            b.StartObject().Key("objects").StartArray();

            foreach (GameObject go in rootObjects)
            {
                if (!go) continue;
                int id = ObjectRegistry.Register(go);
                b.StartObject()
                    .Key("instance_id").Value(id)
                    .Key("name").Value(go.name)
                    .Key("child_count").Value(go.transform.childCount)
                    .Key("active").Value(go.activeSelf)
                .EndObject();
            }

            b.EndArray().EndObject();
            return CommandResponse.Ok(req.Id, b.ToString());
        }
    }
}
