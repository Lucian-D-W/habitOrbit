using Microsoft.Graphics.Canvas;
using Windows.Graphics.DirectX;
using Windows.UI;

namespace Pulse.Rendering;

public static class GraphicsReadiness
{
    // Dependency/pixel-format probe, not the product Orb renderer.
    public static object Check()
    {
        using var device = new CanvasDevice();
        using var target = new CanvasRenderTarget(device, 8, 8, 96,
            DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Premultiplied);
        using (var drawing = target.CreateDrawingSession())
            drawing.Clear(Color.FromArgb(128, 200, 100, 50));
        var pixel = target.GetPixelBytes(0, 0, 1, 1);
        if (pixel.Length != 4 || Math.Abs(pixel[0] - 25) > 1 || Math.Abs(pixel[1] - 50) > 1
            || Math.Abs(pixel[2] - 100) > 1 || pixel[3] != 128)
            throw new InvalidOperationException("Unexpected BGRA premultiplied readback: " + Convert.ToHexString(pixel));
        return new { format = "BGRA8 premultiplied", pixel = Convert.ToHexString(pixel), forcedSoftwareRenderer = device.ForceSoftwareRenderer };
    }
}
