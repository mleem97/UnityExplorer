using System.Globalization;
using UnityExplorer.Runtime;

namespace UnityExplorer.MCP.Handlers
{
    internal static class InspectionCommandHandler
    {
        private const BindingFlags PUBLIC_FLAGS = BindingFlags.Public | BindingFlags.Instance;
        private const BindingFlags ALL_FLAGS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        internal static void Register()
        {
            CommandDispatcher.RegisterHandler("get_components", HandleGetComponents);
            CommandDispatcher.RegisterHandler("inspect_component", HandleInspect);
            CommandDispatcher.RegisterHandler("get_value", HandleGetValue);
            CommandDispatcher.RegisterHandler("set_value", HandleSetValue);
            CommandDispatcher.RegisterHandler("invoke_method", HandleInvokeMethod);
        }

        private static CommandResponse HandleGetComponents(CommandRequest req)
        {
            int instanceId = req.GetInt("instance_id");
            GameObject go = ObjectRegistry.Resolve<GameObject>(instanceId);
            if (go == null)
                return CommandResponse.Fail(req.Id, $"Object with instance ID {instanceId} not found or has been destroyed.");

            Component[] components = go.GetComponents<Component>();
            var b = new JsonHelper.JsonBuilder();
            b.StartObject().Key("components").StartArray();

            foreach (Component comp in components)
            {
                if (!comp) continue;
                int compId = ObjectRegistry.Register(comp);
                Type compType = comp.GetActualType();

                // Check for Behaviour.enabled
                bool enabled = true;
                if (comp is Behaviour behaviour)
                    enabled = behaviour.enabled;

                b.StartObject()
                    .Key("component_id").Value(compId)
                    .Key("type").Value(compType.FullName)
                    .Key("enabled").Value(enabled)
                .EndObject();
            }

            b.EndArray().EndObject();
            return CommandResponse.Ok(req.Id, b.ToString());
        }

        private static CommandResponse HandleInspect(CommandRequest req)
        {
            int compId = req.GetInt("component_id");
            string memberFilter = req.GetString("member_filter");
            bool includePrivate = req.GetBool("include_private");

            object target = ObjectRegistry.Resolve(compId);
            if (target == null)
                return CommandResponse.Fail(req.Id, $"Object with ID {compId} not found or has been destroyed.");

            Type type = target.GetActualType();
            BindingFlags flags = includePrivate ? ALL_FLAGS : PUBLIC_FLAGS;

            var b = new JsonHelper.JsonBuilder();
            b.StartObject().Key("type").Value(type.FullName).Key("members").StartArray();

            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (UERuntimeHelper.IsBlacklisted(field)) continue;
                if (!string.IsNullOrEmpty(memberFilter) && !field.Name.ContainsIgnoreCase(memberFilter))
                    continue;
                try
                {
                    object val = field.GetValue(target);
                    b.StartObject()
                        .Key("name").Value(field.Name)
                        .Key("member_type").Value("field")
                        .Key("value_type").Value(field.FieldType.Name)
                        .Key("value").Raw(SerializeValue(val))
                        .Key("is_readonly").Value(field.IsInitOnly || field.IsLiteral)
                    .EndObject();
                }
                catch { }
            }

            foreach (PropertyInfo prop in type.GetProperties(flags))
            {
                if (UERuntimeHelper.IsBlacklisted(prop)) continue;
                if (!string.IsNullOrEmpty(memberFilter) && !prop.Name.ContainsIgnoreCase(memberFilter))
                    continue;
                if (prop.GetIndexParameters().Length > 0) continue; // skip indexers

                try
                {
                    object val = null;
                    string valJson = "null";
                    if (prop.CanRead)
                    {
                        val = prop.GetValue(target, null);
                        valJson = SerializeValue(val);
                    }

                    b.StartObject()
                        .Key("name").Value(prop.Name)
                        .Key("member_type").Value("property")
                        .Key("value_type").Value(prop.PropertyType.Name)
                        .Key("value").Raw(valJson)
                        .Key("is_readonly").Value(!prop.CanWrite)
                    .EndObject();
                }
                catch { }
            }

            foreach (MethodInfo method in type.GetMethods(flags))
            {
                if (method.IsSpecialName) continue;
                if (UERuntimeHelper.IsBlacklisted(method)) continue;
                if (!string.IsNullOrEmpty(memberFilter) && !method.Name.ContainsIgnoreCase(memberFilter))
                    continue;

                string sig = method.Name + "(" +
                    string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name).ToArray()) +
                    ")";

                b.StartObject()
                    .Key("name").Value(method.Name)
                    .Key("member_type").Value("method")
                    .Key("value_type").Value(method.ReturnType.Name)
                    .Key("value").Value(sig)
                    .Key("is_readonly").Value(true)
                .EndObject();
            }

            b.EndArray().EndObject();
            return CommandResponse.Ok(req.Id, b.ToString());
        }

        private static CommandResponse HandleGetValue(CommandRequest req)
        {
            int compId = req.GetInt("component_id");
            string memberName = req.GetString("member_name");

            object target = ObjectRegistry.Resolve(compId);
            if (target == null)
                return CommandResponse.Fail(req.Id, $"Object with ID {compId} not found or has been destroyed.");

            Type type = target.GetActualType();

            // Try field first, then property
            FieldInfo field = type.GetField(memberName, ALL_FLAGS);
            if (field != null)
            {
                object val = field.GetValue(target);
                var b = new JsonHelper.JsonBuilder();
                b.StartObject()
                    .Key("name").Value(memberName)
                    .Key("value_type").Value(field.FieldType.Name)
                    .Key("value").Raw(SerializeValue(val))
                .EndObject();
                return CommandResponse.Ok(req.Id, b.ToString());
            }

            PropertyInfo prop = type.GetProperty(memberName, ALL_FLAGS);
            if (prop != null && prop.CanRead)
            {
                object val = prop.GetValue(target, null);
                var b = new JsonHelper.JsonBuilder();
                b.StartObject()
                    .Key("name").Value(memberName)
                    .Key("value_type").Value(prop.PropertyType.Name)
                    .Key("value").Raw(SerializeValue(val))
                .EndObject();
                return CommandResponse.Ok(req.Id, b.ToString());
            }

            return CommandResponse.Fail(req.Id, $"Member '{memberName}' not found on type {type.FullName}.");
        }

        private static CommandResponse HandleSetValue(CommandRequest req)
        {
            int compId = req.GetInt("component_id");
            string memberName = req.GetString("member_name");
            object rawValue = req.Params.ContainsKey("value") ? req.Params["value"] : null;

            object target = ObjectRegistry.Resolve(compId);
            if (target == null)
                return CommandResponse.Fail(req.Id, $"Object with ID {compId} not found or has been destroyed.");

            Type type = target.GetActualType();

            // Try field first
            FieldInfo field = type.GetField(memberName, ALL_FLAGS);
            if (field != null)
            {
                object converted = CoerceValue(rawValue, field.FieldType);
                field.SetValue(target, converted);
                object newVal = field.GetValue(target);

                var b = new JsonHelper.JsonBuilder();
                b.StartObject()
                    .Key("new_value").Raw(SerializeValue(newVal))
                .EndObject();
                return CommandResponse.Ok(req.Id, b.ToString());
            }

            PropertyInfo prop = type.GetProperty(memberName, ALL_FLAGS);
            if (prop != null && prop.CanWrite)
            {
                object converted = CoerceValue(rawValue, prop.PropertyType);
                prop.SetValue(target, converted, null);
                object newVal = prop.CanRead ? prop.GetValue(target, null) : converted;

                var b = new JsonHelper.JsonBuilder();
                b.StartObject()
                    .Key("new_value").Raw(SerializeValue(newVal))
                .EndObject();
                return CommandResponse.Ok(req.Id, b.ToString());
            }

            return CommandResponse.Fail(req.Id, $"Writable member '{memberName}' not found on type {type.FullName}.");
        }

        private static CommandResponse HandleInvokeMethod(CommandRequest req)
        {
            int compId = req.GetInt("component_id");
            string methodName = req.GetString("method_name");
            List<object> args = req.GetArray("args") ?? new List<object>();
            List<object> argTypes = req.GetArray("arg_types");

            object target = ObjectRegistry.Resolve(compId);
            if (target == null)
                return CommandResponse.Fail(req.Id, $"Object with ID {compId} not found or has been destroyed.");

            Type type = target.GetActualType();
            MethodInfo[] candidates = type.GetMethods(ALL_FLAGS)
                .Where(m => m.Name == methodName)
                .ToArray();

            if (candidates.Length == 0)
                return CommandResponse.Fail(req.Id, $"Method '{methodName}' not found on type {type.FullName}.");

            // Disambiguate
            MethodInfo method = null;
            if (candidates.Length == 1)
            {
                method = candidates[0];
            }
            else if (argTypes != null)
            {
                method = candidates.FirstOrDefault(m =>
                {
                    var pars = m.GetParameters();
                    if (pars.Length != argTypes.Count) return false;
                    for (int i = 0; i < pars.Length; i++)
                    {
                        string expected = argTypes[i]?.ToString().ToLowerInvariant();
                        if (expected != pars[i].ParameterType.Name.ToLowerInvariant()) return false;
                    }
                    return true;
                });
            }
            else
            {
                // Try to match by arg count
                var byCount = candidates.Where(m => m.GetParameters().Length == args.Count).ToArray();
                if (byCount.Length == 1) method = byCount[0];
            }

            if (method == null)
            {
                string sigs = string.Join("\n", candidates.Select(m =>
                    m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name).ToArray()) + ")"
                ).ToArray());
                return CommandResponse.Fail(req.Id,
                    $"Cannot disambiguate method '{methodName}'. Use arg_types to specify. Available:\n{sigs}");
            }

            // Coerce args
            ParameterInfo[] parameters = method.GetParameters();
            object[] coercedArgs = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                object raw = i < args.Count ? args[i] : null;
                coercedArgs[i] = CoerceValue(raw, parameters[i].ParameterType);
            }

            object result = method.Invoke(target, coercedArgs);

            var b = new JsonHelper.JsonBuilder();
            b.StartObject()
                .Key("return_value").Raw(SerializeValue(result))
            .EndObject();
            return CommandResponse.Ok(req.Id, b.ToString());
        }

        // ── Value Serialization ──────────────────────────────────────────

        internal static string SerializeValue(object val)
        {
            if (val == null) return "null";

            Type type = val.GetType();

            if (val is bool b) return b ? "true" : "false";
            if (val is string s)
                return JsonHelper.EscapeString(s);
            if (val is int || val is long || val is short || val is byte || val is sbyte
                || val is uint || val is ulong || val is ushort)
                return val.ToString();
            if (val is float f) return f.ToString(CultureInfo.InvariantCulture);
            if (val is double d) return d.ToString(CultureInfo.InvariantCulture);
            if (val is decimal dec) return dec.ToString(CultureInfo.InvariantCulture);

            if (type.IsEnum) return $"\"{val}\"";

            // Vector2/3/4, Color, Quaternion
            if (val is Vector2 v2)
                return $"{{\"x\":{v2.x.ToString(CultureInfo.InvariantCulture)},\"y\":{v2.y.ToString(CultureInfo.InvariantCulture)}}}";
            if (val is Vector3 v3)
                return $"{{\"x\":{v3.x.ToString(CultureInfo.InvariantCulture)},\"y\":{v3.y.ToString(CultureInfo.InvariantCulture)},\"z\":{v3.z.ToString(CultureInfo.InvariantCulture)}}}";
            if (val is Vector4 v4)
                return $"{{\"x\":{v4.x.ToString(CultureInfo.InvariantCulture)},\"y\":{v4.y.ToString(CultureInfo.InvariantCulture)},\"z\":{v4.z.ToString(CultureInfo.InvariantCulture)},\"w\":{v4.w.ToString(CultureInfo.InvariantCulture)}}}";
            if (val is Quaternion q)
                return $"{{\"x\":{q.x.ToString(CultureInfo.InvariantCulture)},\"y\":{q.y.ToString(CultureInfo.InvariantCulture)},\"z\":{q.z.ToString(CultureInfo.InvariantCulture)},\"w\":{q.w.ToString(CultureInfo.InvariantCulture)}}}";
            if (val is Color c)
                return $"{{\"r\":{c.r.ToString(CultureInfo.InvariantCulture)},\"g\":{c.g.ToString(CultureInfo.InvariantCulture)},\"b\":{c.b.ToString(CultureInfo.InvariantCulture)},\"a\":{c.a.ToString(CultureInfo.InvariantCulture)}}}";

            // Unity Object reference
            if (val is UnityEngine.Object unityObj)
            {
                int refId = ObjectRegistry.Register(unityObj);
                return $"{{\"type\":\"{type.FullName}\",\"instance_id\":{refId},\"name\":\"{unityObj.name}\"}}";
            }

            // Collections (limited)
            if (val is System.Collections.IList list)
            {
                int totalCount = list.Count;
                bool truncated = totalCount > 100;
                var lb = new JsonHelper.JsonBuilder();
                lb.StartObject()
                    .Key("items").StartArray();
                int count = 0;
                foreach (object item in list)
                {
                    if (count >= 100) break;
                    lb.Raw(SerializeValue(item));
                    count++;
                }
                lb.EndArray()
                    .Key("count").Value(totalCount)
                    .Key("truncated").Value(truncated)
                .EndObject();
                return lb.ToString();
            }

            // Fallback: type + toString
            var fb = new JsonHelper.JsonBuilder();
            fb.StartObject()
                .Key("type").Value(type.FullName)
                .Key("toString").Value(val.ToString());

            if (val is UnityEngine.Object uObj2)
                fb.Key("instance_id").Value(ObjectRegistry.Register(uObj2));
            else
                fb.Key("ref_id").Value(ObjectRegistry.RegisterManaged(val));

            fb.EndObject();
            return fb.ToString();
        }

        // ── Value Coercion ───────────────────────────────────────────────

        internal static object CoerceValue(object raw, Type targetType)
        {
            if (raw == null) return null;

            // Enum
            if (targetType.IsEnum)
            {
                if (raw is string enumName)
                    return Enum.Parse(targetType, enumName, true);
                return Enum.ToObject(targetType, Convert.ToInt64(raw));
            }

            // Unity structs from JSON objects
            if (raw is Dictionary<string, object> dict)
            {
                if (targetType == typeof(Vector2))
                    return new Vector2(GetFloat(dict, "x"), GetFloat(dict, "y"));
                if (targetType == typeof(Vector3))
                    return new Vector3(GetFloat(dict, "x"), GetFloat(dict, "y"), GetFloat(dict, "z"));
                if (targetType == typeof(Vector4))
                    return new Vector4(GetFloat(dict, "x"), GetFloat(dict, "y"), GetFloat(dict, "z"), GetFloat(dict, "w"));
                if (targetType == typeof(Quaternion))
                    return new Quaternion(GetFloat(dict, "x"), GetFloat(dict, "y"), GetFloat(dict, "z"), GetFloat(dict, "w"));
                if (targetType == typeof(Color))
                    return new Color(GetFloat(dict, "r"), GetFloat(dict, "g"), GetFloat(dict, "b"),
                        dict.ContainsKey("a") ? GetFloat(dict, "a") : 1f);
            }

            // Primitives
            try
            {
                return Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
            }
            catch
            {
                throw new ArgumentException(
                    $"Cannot convert value '{raw}' ({raw.GetType().Name}) to type {targetType.Name}. " +
                    $"Use execute_csharp for complex type assignments.");
            }
        }

        private static float GetFloat(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out object val))
            {
                if (val is double d) return (float)d;
                if (val is long l) return l;
            }
            return 0f;
        }
    }
}
