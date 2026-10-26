param(
 [ValidateSet('Debug','Release')][string]$Configuration='Release',
 [string]$Dotnet='dotnet',
 [string]$TargetDirectory=$env:CARGO_TARGET_DIR,
 [switch]$Locked,
 [switch]$SkipInstall
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$TargetDirectory){$TargetDirectory=Join-Path $root 'target'}
$TargetDirectory=[IO.Path]::GetFullPath($TargetDirectory)
$stage=Join-Path $root 'artifacts/app'
$project=Join-Path $root 'apps/host/Pulse.Host.csproj'
$publishArgs=@('publish',$project,'-c',$Configuration,'-p:Platform=x64','--self-contained','true','-o',$stage)
if($Locked){$publishArgs+='-p:RestoreLockedMode=true'}
& $Dotnet @publishArgs
if($LASTEXITCODE){throw 'Host publish failed.'}
$previousTarget=$env:CARGO_TARGET_DIR
Push-Location (Join-Path $root 'apps/manager')
try {
 if(!$SkipInstall){npm ci;if($LASTEXITCODE){throw 'Manager dependency installation failed.'}}
 $env:CARGO_TARGET_DIR=$TargetDirectory
 $tauriArgs=@('run','tauri','--','build','--no-bundle')
 if($Configuration -eq 'Debug'){$tauriArgs+='--debug'}
 if($Locked){$tauriArgs+=@('--','--locked')}
 & npm @tauriArgs
 if($LASTEXITCODE){throw 'Manager build failed.'}
} finally {Pop-Location;$env:CARGO_TARGET_DIR=$previousTarget}
$profile=if($Configuration -eq 'Debug'){'debug'}else{'release'}
Copy-Item -LiteralPath (Join-Path $TargetDirectory "$profile/pulse-manager.exe") -Destination (Join-Path $stage 'Pulse.exe')
Write-Output "Built application: $stage/Pulse.exe"
