using System.Diagnostics;
using System.Text.Json;
using Pulse.Desktop;
using Pulse.Rendering;

namespace Pulse.Host;

internal static class OrbDevelopment
{
    public static int Run()
    {
        using var orb = new DesktopOrb(OrbIdentity.Mystic);
        var palette = 0;
        nint panel = 0;
        panel = NativeWindow.Create("Pulse Orb 开发调校 · 点击反馈尚未持久化", NativeWindow.OverlappedWindow, 0,
            60, 60, 440, 230, (message, wp, lp) =>
            {
                if (message == 0x111)
                {
                    switch ((int)(wp & 0xffff))
                    {
                        case 101: orb.SetEnergy(0); break;
                        case 102: orb.SetEnergy(.5f); break;
                        case 103: orb.SetEnergy(1); break;
                        case 104: palette = (palette + 1) % OrbIdentity.Examples.Length; orb.SetIdentity(OrbIdentity.Examples[palette]); break;
                        case 105: orb.ToggleVisible(); break;
                        case 106: orb.ToggleEconomy(); break;
                        case 107: orb.Recenter(); break;
                        case 108: NativeWindow.PostQuitMessage(0); break;
                    }
                    return 0;
                }
                if (message == 0x10) { NativeWindow.PostQuitMessage(0); return 0; }
                return null;
            });
        try
        {
            NativeWindow.Button(panel,101,"Energy 0",18,18);
            NativeWindow.Button(panel,102,"Energy 0.5",150,18);
            NativeWindow.Button(panel,103,"Energy 1",282,18);
            NativeWindow.Button(panel,104,"切换身份",18,65);
            NativeWindow.Button(panel,105,"隐藏 / 显示",150,65);
            NativeWindow.Button(panel,106,"15 / 30 fps",282,65);
            NativeWindow.Button(panel,107,"移回工作区",18,112);
            NativeWindow.Button(panel,108,"退出开发预览",150,112,247);
            orb.Show(true); NativeWindow.ShowWindow(panel,5);
            return NativeWindow.MessageLoop();
        }
        finally { NativeWindow.Destroy(panel); Console.WriteLine(JsonSerializer.Serialize(orb.Metrics)); }
    }

    public static int Check(string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<string>();
        RotationRenderingChecks.Check(checks);
        static void Require(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); }
        var gesture = new OrbGesture();
        for (var i=0; i<5; i++)
        {
            gesture.Press(new(10,10),new(50,50),8,8);
            Require(gesture.Release(new(10,10)).Clicked,"Rapid click lost");
        }
        checks.Add("five releases / desktop double clicks count independently");
        gesture.Press(new(10,10),new(50,50),8,8);
        Require(!gesture.Move(new(14,14)).PositionChanged,"Threshold applied too early");
        Require(gesture.Move(new(80,120)).PositionChanged,"Drag transition missed");
        var drop=gesture.Release(new(150,200));
        Require(!drop.Clicked && drop.Center==new ScreenPoint(190,240),"Dragging counted or lost pointer origin");
        gesture.Press(new(10,10),new(50,50),12,12); gesture.Cancel();
        Require(!gesture.Release(new(10,10)).Clicked,"Cancellation fabricated click");
        Require(!gesture.Release(new(10,10)).Clicked,"Duplicate release fabricated click");
        checks.Add("DPI-scaled threshold / drag-out / cancel / duplicate release");
        foreach(var identity in OrbIdentity.Examples)
        {
            using var renderer = new OrbRenderer(identity);
            using var target = renderer.CreateTarget(144);
            foreach(var energy in new[]{0f,.5f,1f})
            {
                var pixels=renderer.Render(target,new(energy),0,OrbLayer.Core);
                Require(pixels[3]==0,"Core corner opaque");
                for(var y=0;y<144;y++) for(var x=0;x<144;x++)
                {
                    var offset=(y*144+x)*4; var alpha=pixels[offset+3];
                    Require(pixels[offset]<=alpha && pixels[offset+1]<=alpha && pixels[offset+2]<=alpha,"Pixels not premultiplied");
                    if((x-72)*(x-72)+(y-72)*(y-72)<68*68) Require(alpha>0,"Unexpected core hit-test hole");
                }
            }
            var first=renderer.Render(target,new(.35f),0,OrbLayer.Core);
            var second=renderer.Render(target,new(.35f),0,OrbLayer.Core);
            Require(first.SequenceEqual(second),"Fixed-time render is unstable");
            renderer.ExportThumbnail(Path.Combine(directory,identity.Name.ToLowerInvariant()+".png")).GetAwaiter().GetResult();
        }
        checks.Add("three identities / low-mid-high energy / alpha bounds / stable shared PNG rendering");
        using(var baseline=new OrbRenderer(OrbIdentity.Mystic))
        using(var target=baseline.CreateTarget(256))
        {
            var reference=baseline.Render(target,new(.35f),3,OrbLayer.Thumbnail);
            OrbIdentity[] variants=[OrbIdentity.Mystic with{GlowColor=Windows.UI.Color.FromArgb(255,255,50,20)},
                OrbIdentity.Mystic with{HighlightIntensity=0},OrbIdentity.Mystic with{FlowSpeed=0},
                OrbIdentity.Mystic with{BreathAmplitude=0},OrbIdentity.Mystic with{ParticleColor=Windows.UI.Color.FromArgb(255,255,0,0)},
                OrbIdentity.Mystic with{ShowParticles=false}];
            foreach(var identity in variants)
            {
                using var variant=new OrbRenderer(identity);using var output=variant.CreateTarget(256);
                Require(!reference.SequenceEqual(variant.Render(output,new(.35f),3,OrbLayer.Thumbnail)),"Appearance control does not affect shared rendering");
            }
            using var legacy=new OrbRenderer(OrbIdentity.Mystic with{GlowIntensity=0,GlowScale=1.2f});
            using var legacyTarget=legacy.CreateTarget(256);
            Require(reference.SequenceEqual(legacy.Render(legacyTarget,new(.35f),3,OrbLayer.Thumbnail)),"Legacy halo controls override maturity");
            var initial=OrbVisualState.FromMaturity(0);var mature=OrbVisualState.FromMaturity(1);
            Require(Math.Abs(initial.CoreScale-.7f)<.001 && Math.Abs(mature.CoreScale-.8f)<.001 &&
                initial.GlowStrength<mature.GlowStrength,"Growth does not unify size and halo strength");
            foreach(var (energy,ratio) in new[]{(0f,1.1f),(.5f,1.65f),(1f,2.2f)})
            {
                var state=OrbVisualState.FromMaturity(energy);
                Require(Math.Abs(state.GlowDiameterScale/state.CoreScale-ratio)<.00001f,"Incorrect growth halo diameter ratio");
                Require(72*state.GlowDiameterScale<OrbRenderer.DecorationDiameter/2,"Growth halo exceeds decoration canvas");
                var pixels=baseline.Render(target,state,0,OrbLayer.Thumbnail);
                for(var i=0;i<256;i++)
                    Require(pixels[i*4+3]==0 && pixels[((255*256)+i)*4+3]==0 &&
                        pixels[(i*256)*4+3]==0 && pixels[(i*256+255)*4+3]==0,"Growth halo clipped at thumbnail edge");
            }
        }
        checks.Add("six appearance controls / legacy halo ignored / maturity halo 1.1-1.65-2.2x / unclipped edges");
        var foreground=NativeWindow.GetForegroundWindow();
        var process=Process.GetCurrentProcess();
        var gdiBefore=NativeWindow.GetGuiResources(process.Handle,0);
        var userBefore=NativeWindow.GetGuiResources(process.Handle,1);
        object metrics;
        object pacing;
        using(var orb = new DesktopOrb(OrbIdentity.Mystic))
        {
            var coreStyle=(long)NativeWindow.GetWindowLongPtrW(orb.Core,-20);
            var decorationStyle=(long)NativeWindow.GetWindowLongPtrW(orb.Decoration,-20);
            Require((decorationStyle & NativeWindow.Transparent)!=0 && (coreStyle & NativeWindow.Transparent)==0,"Incorrect cross-process input styles");
            Require((coreStyle & NativeWindow.NoActivate)!=0 && (decorationStyle & NativeWindow.NoActivate)!=0,"Activation styles missing");
            Require(NativeWindow.CircleContains(orb.Core,orb.CorePixels/2,orb.CorePixels/2) && !NativeWindow.CircleContains(orb.Core,0,0),"Core region is not circular");
            // Hidden HWNDs: exercise real native presentation without operating user UI.
            for(var i=0;i<120;i++) orb.RenderFrame();
            var cpuBefore=process.TotalProcessorTime;
            var paced=Stopwatch.StartNew();
            for(var i=0;i<90;i++)
            {
                orb.RenderFrame();
                var delay=(i+1)*1000.0/30-paced.Elapsed.TotalMilliseconds;
                if(delay>0) Thread.Sleep((int)Math.Ceiling(delay));
            }
            var cpuMs=(process.TotalProcessorTime-cpuBefore).TotalMilliseconds;
            pacing=new {frames=90,wallMs=paced.Elapsed.TotalMilliseconds,fps=90/paced.Elapsed.TotalSeconds,
                processCpuMs=cpuMs,normalizedCpuPercent=cpuMs/paced.Elapsed.TotalMilliseconds/Environment.ProcessorCount*100,
                workingSetBytes=process.WorkingSet64,gpuUtilization="not measured"};
            Require(NativeWindow.GetForegroundWindow()==foreground,"Hidden presenter stole foreground");
            metrics=orb.Metrics;
        }
        var gdiAfter=NativeWindow.GetGuiResources(process.Handle,0);
        var userAfter=NativeWindow.GetGuiResources(process.Handle,1);
        Require(gdiAfter<=gdiBefore+2,"GDI objects leaked across disposal");
        Require(userAfter<=userBefore+2,"USER objects leaked across disposal");
        checks.Add("real hidden layered HWND / circular HRGN / no activation / disposal budget");
        var report=new {checks,metrics,pacing,gdiBefore,gdiAfter,userBefore,userAfter,uiAcceptance="pending human",userDataWritten=false,checkedAt=DateTimeOffset.UtcNow};
        var json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});
        File.WriteAllText(Path.Combine(directory,"technical-checks.json"),json);
        Console.WriteLine(json);
        return 0;
    }
}
