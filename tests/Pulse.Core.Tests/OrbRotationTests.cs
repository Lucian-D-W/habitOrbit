using System.Numerics;
using System.Text.Json;
using Pulse.Desktop;
using Pulse.Rendering;
namespace Pulse.Core.Tests;

public sealed class OrbRotationTests
{
    [Theory]
    [InlineData(.85)] [InlineData(1)] [InlineData(1.188)] [InlineData(1.5)]
    public void OverlayMultipliesOriginalOrbitWithoutReplacingIt(double multiplier)
    {
        foreach(var seconds in new[]{0d,1d,37d,300d})
        {
            var original=StarLayout.Rotate(new Vector2(96,0),seconds);
            var composed=Vector2.Transform(original,Matrix3x2.CreateRotation(OrbRotation.Angle(seconds,multiplier)));
            var expected=StarLayout.Rotate(new Vector2(96,0),seconds*multiplier);
            Assert.True(Vector2.Distance(composed,expected)<.001);
            if(multiplier==1)Assert.Equal(0,OrbRotation.Angle(seconds,multiplier));
        }
        Assert.True(OrbRotation.Angle(30,.85)<0);
        Assert.True(OrbRotation.Angle(30,1.5)>0);
    }
    [Fact] public void CoreAndDecorationUseTheSameAngleAboutTheirOwnCenters()
    {
        var core=OrbRotation.Transform(37,1.188,144);var decoration=OrbRotation.Transform(37,1.188,256);
        Assert.True(Vector2.Distance(Vector2.Transform(new(72,72),core),new(72,72))<.0001);
        Assert.True(Vector2.Distance(Vector2.Transform(new(128,128),decoration),new(128,128))<.0001);
        var a=Vector2.Normalize(Vector2.Transform(new(110,72),core)-new Vector2(72));
        var b=Vector2.Normalize(Vector2.Transform(new(220,128),decoration)-new Vector2(128));
        Assert.True(Vector2.Distance(a,b)<.0001);
    }
    [Fact] public void SettingsPreserveAssignedMultipliersAndPlacementAcrossReloadAndOtherEdits()
    {
        var path=Path.Combine(Path.GetTempPath(),"pulse-rotation-"+Guid.NewGuid(),"desktop-settings.json");
        try
        {
            var file=new SettingsFile(path,e=>throw e);var settings=DesktopSettings.Default;var id=Guid.NewGuid();
            settings.Orbs[id]=new(true,"test-monitor",81,99);settings.OrbRotationMultipliers[id]=.85;
            settings.OrbRotationMultipliers[Guid.NewGuid()]=1.188;settings.OrbRotationMultipliers[Guid.NewGuid()]=1.5;
            file.Save(settings);var loaded=file.Load();file.Save(loaded with{AnimationMode="economy",LastView="matrix"});
            var again=file.Load();Assert.Equal(settings.OrbRotationMultipliers,again.OrbRotationMultipliers);
            Assert.Equal(settings.Orbs[id],again.Orbs[id]);
        }
        finally{Directory.Delete(Path.GetDirectoryName(path)!,true);}
    }
    [Fact] public void LegacyAndInvalidRotationSettingsDoNotResetPlacement()
    {
        var path=Path.Combine(Path.GetTempPath(),"pulse-rotation-"+Guid.NewGuid(),"desktop-settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var id=Guid.NewGuid();var file=new SettingsFile(path,e=>throw e);
            var legacy=JsonSerializer.Serialize(new{version=1,animation_mode="normal",start_with_windows=false,last_view="gallery",orbs=new Dictionary<Guid,object>{{id,new{visible=true,monitor="test",center_x_dip=10,center_y_dip=20}}},orb_rotation_periods=new Dictionary<Guid,double>{{id,120}}});
            File.WriteAllText(path,legacy);var loaded=file.Load();Assert.Empty(loaded.OrbRotationMultipliers);Assert.True(loaded.Orbs[id].Visible);
            loaded.OrbRotationMultipliers[id]=0;file.Save(loaded);var valid=file.Load();Assert.Empty(valid.OrbRotationMultipliers);Assert.Equal(loaded.Orbs[id],valid.Orbs[id]);
        }
        finally{Directory.Delete(Path.GetDirectoryName(path)!,true);}
    }
    [Theory]
    [InlineData(0)] [InlineData(.849)] [InlineData(1.501)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void RejectsOutOfRangeMultipliers(double multiplier)=>Assert.Throws<ArgumentOutOfRangeException>(()=>OrbRotation.Angle(1,multiplier));
}
