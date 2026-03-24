namespace UnityExplorer.MCP.Handlers
{
    internal static class ScreenshotCommandHandler
    {
        private static MethodInfo encodeToPngMethod;
        private static string screenshotDir;

        internal static void Register()
        {
            CommandDispatcher.RegisterHandler("capture_screenshot", HandleCaptureScreenshot);

            screenshotDir = Path.Combine(ExplorerCore.ExplorerFolder, "Screenshots");
            Directory.CreateDirectory(screenshotDir);

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
            try
            {
                Camera cam = Camera.main;
                if (cam == null)
                {
                    Camera[] cams = Camera.allCameras;
                    if (cams.Length > 0) cam = cams[0];
                }

                if (cam == null)
                    return CommandResponse.Fail(req.Id, "No active camera found.");

                int width = cam.pixelWidth;
                int height = cam.pixelHeight;

                // Render camera to a RenderTexture
                RenderTexture rt = new RenderTexture(width, height, 24);
                RenderTexture prev = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prev;

                // Read pixels from RenderTexture
                RenderTexture prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;
                UnityEngine.Object.Destroy(rt);

                // Encode and save to file
                byte[] png = EncodeToPNG(tex);
                UnityEngine.Object.Destroy(tex);

                string filename = $"screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
                string filePath = Path.Combine(screenshotDir, filename);
                File.WriteAllBytes(filePath, png);

                var b = new JsonHelper.JsonBuilder();
                b.StartObject()
                    .Key("width").Value(width)
                    .Key("height").Value(height)
                    .Key("path").Value(filePath)
                .EndObject();

                return CommandResponse.Ok(req.Id, b.ToString());
            }
            catch (Exception ex)
            {
                return CommandResponse.Fail(req.Id, $"Screenshot failed: {ex.Message}");
            }
        }
    }
}
