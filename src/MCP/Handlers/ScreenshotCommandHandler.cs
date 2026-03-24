using System.Collections;

namespace UnityExplorer.MCP.Handlers
{
    internal static class ScreenshotCommandHandler
    {
        internal static void Register()
        {
            CommandDispatcher.RegisterHandler("capture_screenshot", HandleCaptureScreenshot);
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

                byte[] png = tex.EncodeToPNG();
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
