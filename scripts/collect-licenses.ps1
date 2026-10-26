param([Parameter(Mandatory)][string]$Destination,[string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
[IO.Directory]::CreateDirectory($Destination)|Out-Null
$inventory=[Collections.Generic.List[object]]::new();$notices=[Text.StringBuilder]::new('# habitOrbit distribution notices'+"`n")
$licenseRoot=Join-Path $root 'licenses'
$nugetRoot=$env:NUGET_PACKAGES
if(!$nugetRoot){$nugetLine=& $Dotnet nuget locals global-packages --list;if($LASTEXITCODE){throw 'Cannot locate the NuGet cache.'};$nugetRoot=($nugetLine|Select-Object -Last 1) -replace '^[^:]+:\s*',''}
$supplements=(Get-Content -LiteralPath (Join-Path $licenseRoot 'package-sources.json') -Raw|ConvertFrom-Json -AsHashtable).packages
$seen=@{}
function Add-PackageNotice([string]$Name,[string]$Version,[string]$License,[string]$Source){
 $key="$Name@$Version"
 if($seen.ContainsKey($key)){return}
 if(!(Test-Path -LiteralPath $Source -PathType Container)){throw "许可包目录缺失：$key"}
 $files=@(Get-ChildItem -LiteralPath $Source -File -Recurse|Where-Object {$_.Name -match '^(LICENSE|LICENCE|COPYING|COPYRIGHT|NOTICE|THIRD.PARTY)([._-].*)?$' -and $_.Extension -notin @('.rs','.a','.dll','.exe')})
 $references=[Collections.Generic.List[string]]::new()
 $supplement=$supplements[$key]
 foreach($file in $files){$references.Add('package/'+[IO.Path]::GetRelativePath($Source,$file.FullName).Replace('\','/'))}
 if($supplement){
  foreach($relative in $supplement.files){
   $path=[IO.Path]::GetFullPath((Join-Path $licenseRoot $relative))
   if(!$path.StartsWith([IO.Path]::GetFullPath($licenseRoot)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw "许可补充路径越界：$relative"}
   if(!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0){throw "许可补充文件缺失或为空：$key / $relative"}
   $files+=Get-Item -LiteralPath $path
   $references.Add('supplement/'+$relative)
  }
 }
 if($files.Count -eq 0){throw "缺少许可正文或上游通知：$key；请核实锁定版本并补充 licenses/package-sources.json。"}
 $inventory.Add([pscustomobject]@{name=$Name;version=$Version;license=$License;notice_files=@($references);notice_sources=@(if($supplement){$supplement.sources});material_note=$(if($supplement){$supplement.note})})
 [void]$notices.Append("`n## $Name $Version — $License`n")
 if($supplement){
  foreach($url in $supplement.sources){[void]$notices.Append("`nSource: $url`n")}
  if($supplement.note){[void]$notices.Append("`n$($supplement.note)`n")}
  $declaration=if(Test-Path -LiteralPath (Join-Path $Source 'Cargo.toml.orig')){Get-Item -LiteralPath (Join-Path $Source 'Cargo.toml.orig')}else{Get-ChildItem -LiteralPath $Source -Filter '*.nuspec'|Select-Object -First 1}
  if($declaration){[void]$notices.Append("`n### Original package declaration`n");[void]$notices.Append([IO.File]::ReadAllText($declaration.FullName))}
 }
 foreach($file in $files){[void]$notices.Append("`n### $($file.Name)`n");[void]$notices.Append([IO.File]::ReadAllText($file.FullName))}
 $seen[$key]=$true
}
$lock=Get-Content (Join-Path $root 'apps/manager/package-lock.json') -Raw|ConvertFrom-Json -AsHashtable
foreach($entry in $lock.packages.GetEnumerator()){
 if(!$entry.Key -or $entry.Value.dev){continue}
 $folder=Join-Path $root ('apps/manager/'+$entry.Key)
 $meta=Get-Content (Join-Path $folder 'package.json') -Raw|ConvertFrom-Json
 Add-PackageNotice $meta.name $meta.version $meta.license $folder
}
foreach($project in @('apps/host','src/Pulse.Rendering','src/Pulse.Storage','src/Pulse.Core','src/Pulse.Desktop')){
 $lock=Get-Content (Join-Path $root "$project/packages.lock.json") -Raw|ConvertFrom-Json -AsHashtable
 foreach($framework in $lock.dependencies.Values){foreach($entry in $framework.GetEnumerator()){
  if(!$entry.Value.resolved){continue}
  $folder=Join-Path $nugetRoot ($entry.Key.ToLowerInvariant()+'/'+$entry.Value.resolved)
  $nuspec=Get-ChildItem -LiteralPath $folder -Filter '*.nuspec'|Select-Object -First 1
  [xml]$xml=Get-Content $nuspec.FullName -Raw
  $license=[string]$xml.package.metadata.license.'#text';if(!$license){$license=[string]$xml.package.metadata.licenseUrl}
  Add-PackageNotice $entry.Key $entry.Value.resolved $license $folder
 }}
}
Push-Location (Join-Path $root 'apps/manager/src-tauri')
try{$metadata=cargo metadata --locked --format-version 1|ConvertFrom-Json;if($LASTEXITCODE -ne 0){throw 'Cargo许可元数据读取失败。'}}finally{Pop-Location}
foreach($package in $metadata.packages){if($package.source){Add-PackageNotice $package.name $package.version $package.license ([IO.Path]::GetDirectoryName($package.manifest_path))}}
foreach($relative in @('upstream/Orb-LICENSE.txt','upstream/Launcher3-NOTICE.txt','upstream/Apache-2.0.txt')){
 $path=Join-Path $licenseRoot $relative
 [void]$notices.Append("`n## Direct design / adaptation reference: $relative`n")
 [void]$notices.Append([IO.File]::ReadAllText($path))
}
$inventory|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $Destination 'dependency-inventory.json') -Encoding utf8
[IO.File]::WriteAllText((Join-Path $Destination 'THIRD_PARTY_NOTICES.txt'),$notices.ToString())
Copy-Item -LiteralPath (Join-Path $root 'licenses/Lucide.txt') -Destination $Destination
Copy-Item -LiteralPath (Join-Path $licenseRoot 'package-sources.json') -Destination $Destination
Copy-Item -LiteralPath (Join-Path $licenseRoot 'upstream') -Destination $Destination -Recurse -Force
Write-Host "许可清单：$($inventory.Count)项"
