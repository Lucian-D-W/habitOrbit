using System.Text.Json;
using Pulse.Core;

namespace Pulse.Storage;
public sealed partial class SqliteStore
{
    public GalleryLayout ReadGallery()
    {
        lock(gate)
        {
            using var command=connection.CreateCommand();command.CommandText="SELECT body FROM gallery_layout WHERE id=1";
            return command.ExecuteScalar() is string body?JsonSerializer.Deserialize<GalleryLayout>(body,Rules.Json)??throw new IOException("布局为空。"):GalleryLayout.Empty;
        }
    }
    public GalleryLayout SaveGallery(GalleryLayout layout,IReadOnlyList<Guid> existing)
    {
        lock(gate)
        {
            layout.Validate(existing);
            using var transaction=connection.BeginTransaction();using var command=connection.CreateCommand();command.Transaction=transaction;
            command.CommandText="SELECT body FROM gallery_layout WHERE id=1";
            var current=command.ExecuteScalar() is string body?JsonSerializer.Deserialize<GalleryLayout>(body,Rules.Json)!:GalleryLayout.Empty;
            Rules.Require(layout.Revision==current.Revision,"layout_changed","布局已在其他窗口更新，请刷新后重试。");
            var saved=layout with{Revision=checked(current.Revision+1),Groups=layout.Groups.Select(g=>g with{Name=g.Name.Trim()}).ToArray()};
            command.CommandText="INSERT INTO gallery_layout(id,body) VALUES(1,$body) ON CONFLICT(id) DO UPDATE SET body=excluded.body";
            command.Parameters.AddWithValue("$body",JsonSerializer.Serialize(saved,Rules.Json));command.ExecuteNonQuery();transaction.Commit();return saved;
        }
    }
}
