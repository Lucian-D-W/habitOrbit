using System.IO.Compression;using System.Text.Json;using Pulse.Core;using Pulse.Storage;
namespace Pulse.Core.Tests;
public sealed class BackupTests
{
 [Fact] public void OnlineWalBackupRestoresSnapshotAndPreservesLaterFacts()
 {
  var root=Path.Combine(Path.GetTempPath(),"pulse-test-"+Guid.NewGuid());Directory.CreateDirectory(root);var db=Path.Combine(root,"pulse.db");
  SqliteStore store=new(db);var core=new PulseCore(store,new SystemClock());
  try
  {
   var a=core.Create(Guid.NewGuid(),new("备份","circle","","count","次",1000,null,new(),VisualSpec.Default));core.Add(a.Id,Guid.NewGuid());
   var backup=Path.Combine(root,"saved.pulse-backup");BackupArchive.Export(store,"{\"version\":1}",backup);
   core.Add(a.Id,Guid.NewGuid());Assert.Equal(2000,core.Detail(a.Id,DateOnly.FromDateTime(DateTime.Now)).Summary.AmountAtoms);
   var stage=BackupArchive.Prepare(backup,root);var old=BackupArchive.ReplaceFiles(root,stage,store.CloseForRestore,()=>{store=new(db);core=new(store,new SystemClock());});
   Assert.Equal(1000,core.Detail(a.Id,DateOnly.FromDateTime(DateTime.Now)).Summary.AmountAtoms);
   using var previous=new SqliteStore(Path.Combine(old,"pulse.db"));Assert.Equal(2000,new PulseCore(previous,new SystemClock()).Detail(a.Id,DateOnly.FromDateTime(DateTime.Now)).Summary.AmountAtoms);
   core.Add(a.Id,Guid.NewGuid());Assert.Equal(2000,core.Detail(a.Id,DateOnly.FromDateTime(DateTime.Now)).Summary.AmountAtoms);
  }
  finally{store.Dispose();Directory.Delete(root,true);}
 }
 [Fact] public void InvalidArchiveCannotReplaceCurrentData()
 {
  var root=Path.Combine(Path.GetTempPath(),"pulse-test-"+Guid.NewGuid());Directory.CreateDirectory(root);
  try{var archive=Path.Combine(root,"invalid.pulse-backup");using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)){zip.CreateEntry("../pulse.db");}Assert.Throws<PulseException>(()=>BackupArchive.Prepare(archive,root));Assert.Single(Directory.GetFiles(root));}
  finally{Directory.Delete(root,true);}
 }
 [Fact] public void FailedReopenRollsBackWithoutDiscardingEitherDatabase()
 {
  var root=Path.Combine(Path.GetTempPath(),"pulse-test-"+Guid.NewGuid());Directory.CreateDirectory(root);var db=Path.Combine(root,"pulse.db");SqliteStore store=new(db);var core=new PulseCore(store,new SystemClock());
  try
  {
   var a=core.Create(Guid.NewGuid(),new("回滚","circle","","count","次",1000,null,new(),VisualSpec.Default));core.Add(a.Id,Guid.NewGuid());var backup=Path.Combine(root,"saved.pulse-backup");BackupArchive.Export(store,"{}",backup);core.Add(a.Id,Guid.NewGuid());
   var stage=BackupArchive.Prepare(backup,root);var attempts=0;
   Assert.Throws<IOException>(()=>BackupArchive.ReplaceFiles(root,stage,store.CloseForRestore,()=>{if(attempts++==0)throw new IOException("模拟重新连接失败");store=new(db);core=new(store,new SystemClock());}));
   Assert.Equal(2000,core.Detail(a.Id,DateOnly.FromDateTime(DateTime.Now)).Summary.AmountAtoms);Assert.False(File.Exists(Path.Combine(root,"restore-pending.json")));Assert.Single(Directory.GetFiles(Path.Combine(root,"backups"),"failed-restored.db",SearchOption.AllDirectories));
  }
  finally{store.Dispose();Directory.Delete(root,true);}
 }
}
