using Pulse.Rendering;

namespace Pulse.Host;

internal static class RotationRenderingChecks
{
    public static void Check(List<string> checks)
    {
        static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        using var straight=new OrbRenderer(OrbIdentity.Mystic with {RotationMultiplier=1});
        using var turned=new OrbRenderer(OrbIdentity.Mystic with {RotationMultiplier=1.5});
        var time=3*Math.PI/2/(.04*.5);
        foreach(var (layer,size) in new[]{(OrbLayer.Core,144),(OrbLayer.Decoration,256)})
        {
            using var a=straight.CreateTarget(size);using var b=turned.CreateTarget(size);
            var state=OrbVisualState.FromMaturity(.6f,35);
            // Same original animation time, plus overlay angles 0 and 270 degrees.
            var first=straight.Render(a,state,time,layer);var second=turned.Render(b,state,time,layer);
            double error=0,unrotatedError=0;
            for(var y=0;y<size;y++)for(var x=0;x<size;x++)for(var c=0;c<4;c++)
            {
                error+=Math.Abs(first[(y*size+x)*4+c]-second[((size-1-x)*size+y)*4+c]);
                unrotatedError+=Math.Abs(first[(y*size+x)*4+c]-second[(y*size+x)*4+c]);
            }
            var mean=error/(size*size*4);
            Require(mean<2,$"{layer} does not rotate as one picture: mean pixel error {mean}");
            Require(unrotatedError>error*3,$"{layer} did not visibly rotate");
        }
        checks.Add("actual Core/Decoration pixels preserve original animation and agree with a 270-degree overlay");
        using(var a=straight.CreateTarget(256))using(var b=turned.CreateTarget(256))
        {
            var labelState=OrbVisualState.FromMaturity(0,0,"Upright label 123");
            var first=straight.Render(a,labelState,time,OrbLayer.Decoration);
            var second=turned.Render(b,labelState,time,OrbLayer.Decoration);
            Require(first.Zip(second,(x,y)=>Math.Abs(x-y)).Average()<.1,"Hover label rotates with the artwork");
            var thumbnail=straight.Render(a,labelState,time,OrbLayer.Thumbnail);
            Require(thumbnail.SequenceEqual(turned.Render(b,labelState,time,OrbLayer.Thumbnail)),"Rotation changed static thumbnail framing");
            foreach(var multiplier in new[]{.85,1d,1.188,1.5})
            {
                using var renderer=new OrbRenderer(OrbIdentity.Mystic with{RotationMultiplier=multiplier});
                using var markerTarget=renderer.CreateTarget(256);
                const double elapsed=32;
                var markers=renderer.Render(markerTarget,OrbVisualState.FromMaturity(0,11),elapsed,OrbLayer.Decoration);
                var satellite=StarLayout.Rotate(new System.Numerics.Vector2(107*.9f,0),elapsed*multiplier)+new System.Numerics.Vector2(128);
                Require(markers[((int)satellite.Y*256+(int)satellite.X)*4+3]>240,$"Satellite original orbit plus overlay missing at {multiplier}");
                var star=StarLayout.Rotate(StarLayout.Create(OrbIdentity.Mystic.Seed)[0],elapsed*multiplier)+new System.Numerics.Vector2(128);
                Require(markers[((int)star.Y*256+(int)star.X)*4+3]>100,$"Star original orbit plus overlay missing at {multiplier}");
            }
        }
        checks.Add("upright labels / unchanged thumbnails / original satellite and star motion retained at 0.85, 1.00, 1.188, 1.50");
    }
}
