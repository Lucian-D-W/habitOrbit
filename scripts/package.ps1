param([switch]$SkipBuild,[string]$Dotnet='dotnet',[string]$TargetDirectory=$env:CARGO_TARGET_DIR,
 [string]$DependencyDirectory,[string]$Iscc='iscc')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$DependencyDirectory){$DependencyDirectory=Join-Path $root '.tools/runtime-installers'}
$DependencyDirectory=[IO.Path]::GetFullPath($DependencyDirectory)
$stage=Join-Path $root 'artifacts/app';$output=Join-Path $root 'artifacts/installer'
if(!$SkipBuild){& (Join-Path $PSScriptRoot 'build.ps1') -Locked -Dotnet $Dotnet -TargetDirectory $TargetDirectory}
foreach($file in @('Pulse.exe','Pulse.Host.exe')){if(!(Test-Path -LiteralPath (Join-Path $stage $file))){throw "Missing build output: $file"}}
& (Join-Path $PSScriptRoot 'collect-licenses.ps1') -Destination (Join-Path $stage 'licenses') -Dotnet $Dotnet
foreach($document in @('LICENSE','COPYRIGHT.md','THIRD_PARTY_NOTICES.md')){Copy-Item -LiteralPath (Join-Path $root $document) -Destination $stage}
Copy-Item -LiteralPath (Join-Path $root 'docs/user-guide.md') -Destination (Join-Path $stage '使用说明.md')
$dependencies=@('MicrosoftEdgeWebView2RuntimeInstallerX64.exe','vc_redist.x64.exe')
foreach($file in $dependencies){$path=Join-Path $DependencyDirectory $file;if((Get-AuthenticodeSignature -LiteralPath $path).Status -ne 'Valid'){throw "Runtime installer signature is not valid: $file"}}
$manifest=@{app='星以为常 habitOrbit';version='0.1.0';architecture='x64';host_self_contained=$true;msix=$false;dependencies=@($dependencies|ForEach-Object {$path=Join-Path $DependencyDirectory $_;@{file=$_;sha256=(Get-FileHash -LiteralPath $path).Hash;version=(Get-Item $path).VersionInfo.ProductVersion}})}
$manifest|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $stage 'release-manifest.json') -Encoding utf8
& $Iscc "/DStage=$stage" "/DOutput=$output" "/DRuntimeInstallers=$DependencyDirectory" (Join-Path $PSScriptRoot 'package.iss')
if($LASTEXITCODE){throw 'Installer build failed.'}
Write-Output (Join-Path $output 'habitOrbit-Setup-x64.exe')
