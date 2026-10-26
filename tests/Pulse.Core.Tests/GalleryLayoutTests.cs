using System.Text.Json;
using Pulse.Core;

namespace Pulse.Core.Tests;

// Pure model compatibility checks: no Store, Host, profile, or test database.
public sealed class GalleryLayoutTests
{
    private static readonly Guid A=Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B=Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid G=Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static GalleryLayout Legacy()=>new(7,[A],[new(G,"练习",[B])]){Favorites=[B]};

    [Fact] public void LegacyDocumentGetsExistingOrderWithoutMutation()
    {
        var old=Legacy();var next=old.Normalize([A,B]);
        Assert.Null(old.TopOrder);Assert.Equal([$"card:{A}",$"group:{G}"],next.TopOrder!);
        Assert.Equal(old.Revision,next.Revision);Assert.Equal(old.Favorites,next.Favorites);next.Validate([A,B]);
    }
    [Fact] public void MixedOrderSurvivesJsonAndNormalization()
    {
        var mixed=Legacy() with{TopOrder=[$"group:{G}",$"card:{A}"]};
        var json=JsonSerializer.Serialize(mixed,Rules.Json);
        var read=JsonSerializer.Deserialize<GalleryLayout>(json,Rules.Json)!.Normalize([A,B]);
        Assert.Equal(mixed.TopOrder,read.TopOrder);read.Validate([A,B]);
    }
    [Fact] public void NormalizationDropsDeletedEntriesAndAppendsNewMembers()
    {
        var value=Legacy() with{TopOrder=[$"group:{G}",$"card:{A}",$"card:{A}","card:gone"]};
        var next=value.Normalize([B]);Assert.Equal([$"group:{G}"],next.TopOrder!);Assert.Empty(next.Ungrouped);next.Validate([B]);
        var restored=next.Normalize([A,B]);Assert.Equal([$"group:{G}",$"card:{A}"],restored.TopOrder!);
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("group-member")]
    public void ValidationRejectsInvalidTopOrder(string kind)
    {
        var value=Legacy() with{TopOrder=kind switch{
            "missing"=>[$"group:{G}"],
            "duplicate"=>[$"card:{A}",$"group:{G}",$"card:{A}"],
            _=>[$"card:{A}",$"group:{G}",$"card:{B}"]}};
        Assert.Throws<PulseException>(()=>value.Validate([A,B]));
    }
}
