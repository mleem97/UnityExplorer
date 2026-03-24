using System.Collections;

namespace UnityExplorer.MCP.Handlers
{
    internal static class ScreenshotCommandHandler
    {
        private static MethodInfo encodeToPngMethod;

        internal static void Register()
        {
            CommandDispatcher.RegisterHandler("capture_screenshot", HandleCaptureScreenshot);

            // Find EncodeToPNG — it may be on ImageConversion (Unity 2017.2+) or Texture2D (older)
            Type imageConversion = ReflectionUtility.GetTypeByName("UnityEngine.ImageConversion");
            if (imageConversion != null)
            {
                encodeToPngMethod = imageConversion.GetMethod("EncodeToPNG",
                    BindingFlags.Public | BindingFlags.Static,
                    null, new Type[] { typeof(Texture2D) }, null);
            }

            if (encodeToPngMethod == null)
            {
                encodeToPngMethod = typeof(Texture2D).GetMethod("EncodeToPNG",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, Type.EmptyTypes, null);
            }
        }

        private static byte[] EncodeToPNG(Texture2D tex)
        {
            if (encodeToPngMethod == null)
                throw new Exception("No EncodeToPNG method found on ImageConversion or Texture2D");

            if (encodeToPngMethod.IsStatic)
                return (byte[])encodeToPngMethod.Invoke(null, new object[] { tex });
            else
                return (byte[])encodeToPngMethod.Invoke(tex, null);
        }

        private static CommandResponse HandleCaptureScreenshot(CommandRequest req)
        {
            // Start coroutine to capture at end of frame — response is sent async
            ExplorerBehaviour.Instance.StartCoroutine(CaptureCoroutine(req.Id));
            return null; // null signals dispatcher: don't send response, we'll handle it
        }

        private static IEnumerator CaptureCoroutine(string requestId)
        {
            yield return new WaitForEndOfFrame();

            try
            {
                int width = Screen.width;
                int height = Screen.height;
                Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                byte[] png = EncodeToPNG(tex);
                UnityEngine.Object.Destroy(tex);

                string base64 = Convert.ToBase64String(png);

                var b = new JsonHelper.JsonBuilder();
                b.StartObject()
                    .Key("width").Value(width)
                    .Key("height").Value(height)
                    .Key("format").Value("png")
                    .Key("data").Value(base64)
                .EndObject();

                MCPBridge.SendResponse(CommandResponse.Ok(requestId, b.ToString()));
            }
            catch (Exception ex)
            {
                MCPBridge.SendResponse(CommandResponse.Fail(requestId, $"Screenshot failed: {ex.Message}"));
            }
        }
    }
}
