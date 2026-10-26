using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.Graphics.DirectX;
using Windows.UI;

namespace Pulse.Rendering;

public enum OrbLayer { Core, Decoration, Thumbnail }

// One device and brush set per identity. Targets live for the lifetime of a DPI size.
public sealed class OrbRenderer : IDisposable
{
    public const float CoreDiameter = 144;
    public const float DecorationDiameter = 256;
    private readonly CanvasDevice device;
    private readonly bool ownsDevice;
    private readonly CanvasLinearGradientBrush background;
    private readonly CanvasRadialGradientBrush flow, highlight, lowerLight, rim, halo, satelliteLight;
    private readonly CanvasGeometry coreClip;
    private readonly CanvasGeometry wave;
    private readonly CanvasTextFormat label = new() { FontFamily = "Segoe UI", FontSize = 11, HorizontalAlignment = CanvasHorizontalAlignment.Center };
    private readonly Vector2[] particles;
    private readonly Vector2[] stars;
    public OrbIdentity Identity { get; }

    public OrbRenderer(OrbIdentity identity, CanvasDevice? sharedDevice = null)
    {
        Identity = identity;
        stars = StarLayout.Create(identity.Seed);
        device = sharedDevice ?? new CanvasDevice(); ownsDevice = sharedDevice is null;
        var palette = identity.Palette ?? [identity.Top, identity.Middle, identity.Bottom];
        background = new(device, palette.Select((color,index) => new CanvasGradientStop {
            Position = (float)index / (palette.Length-1), Color = color }).ToArray())
            { StartPoint = new(35, 0), EndPoint = new(110, 144) };
        flow = SoftBrush(Color.FromArgb(130, identity.Bottom.R, identity.Bottom.G, identity.Bottom.B), 70, 47);
        highlight = SoftBrush(Color.FromArgb(155, 255, 255, 255), 35, 56);
        lowerLight = SoftBrush(Color.FromArgb(220, 255, 255, 255), 79, 38);
        rim = new(device, new[] {
            new CanvasGradientStop { Position = 0, Color = Color.FromArgb(0, 255, 255, 255) },
            new CanvasGradientStop { Position = .83f, Color = Color.FromArgb(0, 255, 255, 255) },
            new CanvasGradientStop { Position = .96f, Color = Color.FromArgb(65, 255, 255, 255) },
            new CanvasGradientStop { Position = 1, Color = Color.FromArgb(145, 255, 255, 255) }
        }) { Center = new(72, 72), RadiusX = 72, RadiusY = 72 };
        halo = new(device, new[] {
            new CanvasGradientStop { Position = 0, Color = Color.FromArgb(0, (identity.GlowColor??identity.Accent).R, (identity.GlowColor??identity.Accent).G, (identity.GlowColor??identity.Accent).B) },
            new CanvasGradientStop { Position = .48f, Color = Color.FromArgb(150, (identity.GlowColor??identity.Accent).R, (identity.GlowColor??identity.Accent).G, (identity.GlowColor??identity.Accent).B) },
            new CanvasGradientStop { Position = .68f, Color = Color.FromArgb(65, (identity.GlowColor??identity.Accent).R, (identity.GlowColor??identity.Accent).G, (identity.GlowColor??identity.Accent).B) },
            new CanvasGradientStop { Position = 1, Color = Color.FromArgb(0, (identity.GlowColor??identity.Accent).R, (identity.GlowColor??identity.Accent).G, (identity.GlowColor??identity.Accent).B) }
        });
        satelliteLight = new(device, new[] {
            new CanvasGradientStop { Position = 0, Color = Color.FromArgb(255, 255, 255, 255) },
            new CanvasGradientStop { Position = .35f, Color = identity.Accent },
            new CanvasGradientStop { Position = 1, Color = Color.FromArgb(255, (byte)(identity.Accent.R * .55f), (byte)(identity.Accent.G * .55f), (byte)(identity.Accent.B * .55f)) }
        }) { RadiusX = 12, RadiusY = 12 };
        coreClip = CanvasGeometry.CreateCircle(device, 72, 72, 71.75f);
        using (var path = new CanvasPathBuilder(device))
        {
            path.BeginFigure(new Vector2(-20, 105));
            path.AddCubicBezier(new Vector2(28, 35), new Vector2(76, 115), new Vector2(162, 63));
            path.AddLine(new Vector2(168, 170)); path.AddLine(new Vector2(-20, 170));
            path.EndFigure(CanvasFigureLoop.Closed);
            wave = CanvasGeometry.CreatePath(path);
        }
        var random = new Random(identity.Seed);
        particles = Enumerable.Range(0, 16).Select(_ => new Vector2((float)random.NextDouble() * 116 + 14, (float)random.NextDouble() * 92 + 5)).ToArray();
    }

    private CanvasRadialGradientBrush SoftBrush(Color color, float rx, float ry) => new(device, color, Color.FromArgb(0, color.R, color.G, color.B)) { RadiusX = rx, RadiusY = ry };

    public CanvasRenderTarget CreateTarget(int pixels) => new(device, pixels, pixels, 96,
        DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Premultiplied);

    public byte[] Render(CanvasRenderTarget target, OrbVisualState state, double time, OrbLayer layer)
    {
        DrawOrb(Identity, state, time, target, layer);
        return target.GetPixelBytes();
    }

    public void DrawOrb(OrbIdentity identity, OrbVisualState state, double time, CanvasRenderTarget target, OrbLayer layer)
    {
        if (identity != Identity) throw new ArgumentException("Use the brush set belonging to this identity.", nameof(identity));
        using var ds = target.CreateDrawingSession();
        ds.Clear(Color.FromArgb(0, 0, 0, 0));
        var diameter = layer == OrbLayer.Core ? CoreDiameter : DecorationDiameter;
        var scale = (float)target.Size.Width / diameter;
        ds.Transform = Matrix3x2.CreateScale(scale);
        var e = Math.Clamp(state.Energy, 0, 1);
        var smooth = e * e * (3 - 2 * e);
        if (layer != OrbLayer.Core)
        {
            if(layer!=OrbLayer.Thumbnail)
                ds.Transform=OrbRotation.Transform(time,identity.RotationMultiplier,DecorationDiameter)*Matrix3x2.CreateScale(scale);
            DrawDecoration(ds, state, time, smooth, layer == OrbLayer.Thumbnail);
            // Labels belong to the stationary screen plane, never to the rotating artwork.
            ds.Transform=Matrix3x2.CreateScale(scale);
            if(layer==OrbLayer.Decoration)DrawLabels(ds,state);
        }
        if (layer == OrbLayer.Decoration) return;
        var offset = layer == OrbLayer.Core ? Vector2.Zero : new Vector2((DecorationDiameter - CoreDiameter) / 2);
        ds.Transform = (layer==OrbLayer.Thumbnail?Matrix3x2.CreateScale(state.CoreScale,new Vector2(72)):Matrix3x2.Identity)
            * (layer==OrbLayer.Thumbnail?Matrix3x2.Identity:OrbRotation.Transform(time,identity.RotationMultiplier,CoreDiameter))
            * Matrix3x2.CreateTranslation(offset) * Matrix3x2.CreateScale(scale);
        using (ds.CreateLayer(1, coreClip))
        {
            ds.FillCircle(72, 72, 72, background);
            var motion = (float)time * (.10f + .10f * smooth) * identity.FlowSpeed;
            flow.Center = new(65 + 25 * MathF.Sin(motion), 82 + 19 * MathF.Cos(motion * .7f));
            flow.Opacity = .8f;
            ds.FillCircle(72, 72, 72, flow);
            var originalTransform = ds.Transform;
            ds.Transform = Matrix3x2.CreateRotation(.10f * MathF.Sin(motion), new Vector2(72)) * originalTransform;
            if (!identity.Minimal) ds.FillGeometry(wave, Color.FromArgb(38, Identity.Bottom.R, Identity.Bottom.G, Identity.Bottom.B));
            ds.Transform = originalTransform;
            lowerLight.Center = new(77 + 12 * MathF.Sin(motion * .6f), 130);
            lowerLight.Opacity = .55f;
            ds.FillCircle(72, 72, 72, lowerLight);
            highlight.Center = new(identity.HighlightX * CoreDiameter, identity.HighlightY * CoreDiameter);
            highlight.Opacity = Math.Clamp(identity.HighlightIntensity*(.85f + .30f * smooth + identity.BreathAmplitude*(.01f + .05f * smooth) * MathF.Sin((float)time * .9f)),0,1);
            ds.FillCircle(72, 72, 72, highlight);
            ds.FillCircle(72, 72, 72, rim);
            for (var i = 0; identity.ShowParticles && i < (identity.Minimal ? 4 : particles.Length); i++)
            {
                var p = particles[i] + new Vector2(MathF.Sin(motion + i) * 3, MathF.Cos(motion * .7f + i) * 4);
                var opacity = (byte)(90 + 100 * (.5 + .5 * Math.Sin(time * .7 + i * 2.3)));
                ds.FillCircle(p, i % 5 == 0 ? 1.5f : .8f, Color.FromArgb(opacity, identity.ParticleColor.R, identity.ParticleColor.G, identity.ParticleColor.B));
            }
        }
    }

    private void DrawDecoration(CanvasDrawingSession ds, OrbVisualState state, double time, float smooth, bool thumbnail)
    {
        const float center = DecorationDiameter / 2;
        halo.Center = new(center, center);
        halo.RadiusX = halo.RadiusY = 72 * state.GlowDiameterScale;
        halo.Opacity = state.GlowStrength;
        ds.FillCircle(center, center, halo.RadiusX, halo);
        if (thumbnail) return;
        // No record objects without input: decoration particles belong inside the core.
        var satellites = state.GroupSatellites ?? state.FeedbackCount / 10;
        var stars = state.GroupSatellites is null ? state.FeedbackCount % 10 : state.GroupStars;
        var starLimit = state.GroupSatellites is null ? 9 : 24;
        if (satellites > 0 || stars > 0)
        {
            const float orbit=107*.9f;
            if (satellites > 0) ds.DrawCircle(center, center, orbit, Color.FromArgb(50, Identity.Accent.R, Identity.Accent.G, Identity.Accent.B), .7f);
            for (var i = 0; i < Math.Min(satellites, 12); i++)
            {
                var a = (float)(time * .04 + i * Math.Tau / Math.Min(satellites, 12));
                var position = new Vector2(center + orbit * MathF.Cos(a), center + orbit * MathF.Sin(a));
                satelliteLight.Center = position + new Vector2(-3f, -3f);
                ds.FillCircle(position, 8, satelliteLight);
            }
            for (var i = 0; i < Math.Min(stars, starLimit); i++)
            {
                var opacity = (byte)(155 + 80 * (.5 + .5 * Math.Sin(time * .7 + i * 2.3)));
                ds.FillCircle(new Vector2(center) + StarLayout.Rotate(this.stars[i],time), 3, Color.FromArgb(opacity, 235, 228, 255));
            }
        }
    }

    private void DrawLabels(CanvasDrawingSession ds,OrbVisualState state)
    {
        var satellites=state.GroupSatellites??state.FeedbackCount/10;
        var stars=state.GroupSatellites is null?state.FeedbackCount%10:state.GroupStars;
        var starLimit=state.GroupSatellites is null?9:24;
        if(satellites>12)ds.DrawText($"+{satellites-12}",new Windows.Foundation.Rect(0,2,DecorationDiameter,16),Identity.Accent,label);
        if(stars>starLimit)ds.DrawText($"+{stars-starLimit} 份",new Windows.Foundation.Rect(0,20,DecorationDiameter,16),Identity.Accent,label);
        if (state.HoverText is not null)
            ds.DrawText(state.HoverText, new Windows.Foundation.Rect(0, 202, DecorationDiameter, 36), Color.FromArgb(235, 240, 240, 255), label);
    }

    public async Task ExportThumbnail(string path)
    {
        using var target = CreateTarget(256);
        DrawOrb(Identity, new(.35f), 0, target, OrbLayer.Thumbnail);
        await target.SaveAsync(Path.GetFullPath(path), CanvasBitmapFileFormat.Png);
    }

    public async Task<byte[]> ThumbnailBytes(float energy=.35f,double time=0)
    {
        using var target=CreateTarget(256);
        DrawOrb(Identity,OrbVisualState.FromMaturity(energy),time,target,OrbLayer.Thumbnail);
        using var stream=new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await target.SaveAsync(stream,CanvasBitmapFileFormat.Png);
        using var reader=new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
        var size=checked((uint)stream.Size);await reader.LoadAsync(size);
        var bytes=new byte[size];reader.ReadBytes(bytes);return bytes;
    }

    public void Dispose()
    {
        label.Dispose(); wave.Dispose(); coreClip.Dispose(); background.Dispose(); flow.Dispose(); highlight.Dispose();
        lowerLight.Dispose(); rim.Dispose(); halo.Dispose(); satelliteLight.Dispose(); if (ownsDevice) device.Dispose();
    }
}
