using System.Text.Json;
using Pulse.Core;
using Pulse.Storage;
namespace Pulse.Core.Tests;
public sealed class AppearanceTests
{
 [Fact] public void LegacyVisualRetainsKeyAndExplicitDefaultsAreEquivalent()
 {
  var visual=VisualSpec.Default;
  var oldJson=JsonSerializer.Serialize(visual,Rules.Json).Replace(",\"effects\":null","");
  var restored=JsonSerializer.Deserialize<VisualSpec>(oldJson,Rules.Json)!;
  Assert.Null(restored.Effects);
  var config=new ActivityConfig("阅读","book","","count","次",1000,null,new(),restored with{Effects=new()});
  var normalized=Rules.Normalize(config).Visual;
  Assert.Null(normalized.Effects);
  Assert.Equal(Rules.VisualKey(Rules.Normalize(config with{Visual=restored}).Visual),Rules.VisualKey(normalized));
 }
 [Fact] public void SavedEffectsRoundTripAndAffectIdentityKey()
 {
  var effects=new OrbEffects("#ff0000",.7m,2.1m,.5m,2,.4m,"#00ff00",false);
  var config=new ActivityConfig("阅读","book","","count","次",1000,null,new(),VisualSpec.Default with{Effects=effects});
  var normalized=Rules.Normalize(config).Visual;
  var restored=JsonSerializer.Deserialize<VisualSpec>(JsonSerializer.Serialize(normalized,Rules.Json),Rules.Json)!;
  Assert.Equal(normalized.Effects,restored.Effects);
  Assert.Equal("#FF0000",restored.Effects!.GlowColor);Assert.False(restored.Effects.ShowParticles);
  Assert.NotEqual(Rules.VisualKey(VisualSpec.Default),Rules.VisualKey(restored));
  var path=Path.Combine(AppContext.BaseDirectory,"isolated-data",Guid.NewGuid()+".db");
  Guid id;
  using(var store=new SqliteStore(path))
  {var core=new PulseCore(store,new SystemClock());id=core.Create(Guid.NewGuid(),config).Id;}
  using(var store=new SqliteStore(path))
  {var core=new PulseCore(store,new SystemClock());Assert.Equal(normalized.Effects,core.Activity(id).Config.Visual.Effects);}
 }
}
