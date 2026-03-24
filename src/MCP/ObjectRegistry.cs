namespace UnityExplorer.MCP
{
    /// <summary>
    /// Maps instance IDs to objects for cross-request referencing.
    /// Unity objects use GetInstanceID() (positive). Non-Unity objects get synthetic negative IDs.
    /// </summary>
    internal static class ObjectRegistry
    {
        private static readonly Dictionary<int, WeakReference> unityObjects = new();
        private static readonly Dictionary<int, WeakReference> managedObjects = new();
        private static int nextManagedId = -1;
        private static int cleanupCounter;

        /// <summary>Register a Unity object and return its instance ID.</summary>
        internal static int Register(UnityEngine.Object obj)
        {
            if (!obj) return 0;
            int id = obj.GetInstanceID();
            unityObjects[id] = new WeakReference(obj);
            MaybeCleanup();
            return id;
        }

        /// <summary>Register any managed object and return a synthetic negative ID.</summary>
        internal static int RegisterManaged(object obj)
        {
            if (obj == null) return 0;

            // If it's a Unity object, use the Unity path
            if (obj is UnityEngine.Object unityObj)
                return Register(unityObj);

            int id = nextManagedId--;
            managedObjects[id] = new WeakReference(obj);
            MaybeCleanup();
            return id;
        }

        /// <summary>Look up any registered object by ID (positive = Unity, negative = managed).</summary>
        internal static object Resolve(int id)
        {
            if (id == 0) return null;

            Dictionary<int, WeakReference> dict = id > 0 ? unityObjects : managedObjects;
            if (dict.TryGetValue(id, out WeakReference weakRef))
            {
                object target = weakRef.Target;
                if (target != null)
                {
                    // Extra check for Unity objects that may have been destroyed
                    if (target is UnityEngine.Object unityObj && !unityObj)
                    {
                        dict.Remove(id);
                        return null;
                    }
                    return target;
                }
                dict.Remove(id);
            }
            return null;
        }

        /// <summary>Resolve and cast to a specific type.</summary>
        internal static T Resolve<T>(int id) where T : class
        {
            return Resolve(id) as T;
        }

        private static void MaybeCleanup()
        {
            cleanupCounter++;
            if (cleanupCounter < 100) return;
            cleanupCounter = 0;
            Cleanup(unityObjects);
            Cleanup(managedObjects);
        }

        private static void Cleanup(Dictionary<int, WeakReference> dict)
        {
            List<int> dead = null;
            foreach (var kvp in dict)
            {
                bool isDead = !kvp.Value.IsAlive;
                if (!isDead && kvp.Value.Target is UnityEngine.Object obj && !obj)
                    isDead = true;

                if (isDead)
                {
                    if (dead == null) dead = new List<int>();
                    dead.Add(kvp.Key);
                }
            }
            if (dead != null)
                foreach (int id in dead)
                    dict.Remove(id);
        }
    }
}
