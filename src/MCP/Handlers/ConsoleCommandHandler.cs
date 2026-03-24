using Mono.CSharp;
using System.Text;
using UnityExplorer.CSConsole;

namespace UnityExplorer.MCP.Handlers
{
    internal static class ConsoleCommandHandler
    {
        internal static void Register()
        {
            CommandDispatcher.RegisterHandler("execute_csharp", HandleExecuteCSharp);
        }

        private static CommandResponse HandleExecuteCSharp(CommandRequest req)
        {
            if (ConsoleController.SRENotSupported)
            {
                return CommandResponse.Fail(req.Id,
                    "C# console is not available on this build (System.Reflection.Emit not supported). " +
                    "This commonly happens on IL2CPP builds with stripped assemblies.");
            }

            string code = req.GetString("code");
            if (string.IsNullOrEmpty(code))
                return CommandResponse.Fail(req.Id, "code is required");

            // Ensure evaluator is initialized
            if (ConsoleController.Evaluator == null)
            {
                ConsoleController.ResetConsole();
                if (ConsoleController.Evaluator == null)
                    return CommandResponse.Fail(req.Id, "Failed to initialize C# evaluator.");
            }

            // Save and restore _textWriter to avoid corrupting UI console output
            var originalWriter = ConsoleController.Evaluator._textWriter;
            try
            {
                var outputWriter = new System.IO.StringWriter();
                ConsoleController.Evaluator._textWriter = outputWriter;

                CompiledMethod repl = ConsoleController.Evaluator.Compile(code);

                if (repl != null)
                {
                    object ret = null;
                    repl.Invoke(ref ret);

                    var b = new JsonHelper.JsonBuilder();
                    b.StartObject()
                        .Key("success").Value(true)
                        .Key("output").Value(outputWriter.ToString().Trim());

                    if (ret != null)
                        b.Key("return_value").Raw(InspectionCommandHandler.SerializeValue(ret));
                    else
                        b.Key("return_value").Null();

                    b.EndObject();
                    return CommandResponse.Ok(req.Id, b.ToString());
                }
                else
                {
                    // Using directive or class definition
                    string output = outputWriter.ToString();
                    if (ScriptEvaluator._reportPrinter.ErrorsCount > 0)
                    {
                        return CommandResponse.Fail(req.Id,
                            $"Compilation error: {output.Trim()}");
                    }

                    var b = new JsonHelper.JsonBuilder();
                    b.StartObject()
                        .Key("success").Value(true)
                        .Key("output").Value("Code compiled successfully (using directive or class definition).")
                        .Key("return_value").Null()
                    .EndObject();
                    return CommandResponse.Ok(req.Id, b.ToString());
                }
            }
            catch (Exception ex)
            {
                return CommandResponse.Fail(req.Id, $"{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                ConsoleController.Evaluator._textWriter = originalWriter;
            }
        }
    }
}
