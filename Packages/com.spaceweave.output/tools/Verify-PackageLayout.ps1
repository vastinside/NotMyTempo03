# Static layout check for com.spaceweave.output (no Unity required).
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not (Test-Path (Join-Path $root 'package.json'))) {
    $root = Split-Path $PSScriptRoot -Parent
}
$fails = [System.Collections.Generic.List[string]]::new()
$pkg = Get-Content (Join-Path $root 'package.json') -Raw | ConvertFrom-Json
if ($pkg.name -ne 'com.spaceweave.output') { $fails.Add('package name') }
if ($pkg.version -ne '0.1.0') { $fails.Add('package version') }
if ($pkg.dependencies.'jp.keijiro.klak.spout' -ne '2.0.6') { $fails.Add('spout dep') }
if ($pkg.dependencies.'jp.keijiro.klak.ndi' -ne '2.1.6') { $fails.Add('ndi dep') }
if (-not $pkg.scopedRegistries) { $fails.Add('scopedRegistries') }

$required = @(
    'Runtime/SpaceWeave.Output.asmdef',
    'Editor/SpaceWeave.Output.Editor.asmdef',
    'Runtime/Scripts/SpaceWeaveOutputManager.cs',
    'Runtime/Scripts/SpaceWeaveOutputContract.cs',
    'Runtime/Scripts/SpaceWeaveFallbackPattern.cs',
    'Runtime/Scripts/SpaceWeaveFinalTextureEvidence.cs',
    'Runtime/Scripts/SpaceWeaveDiagnosticRig.cs',
    'Runtime/Shaders/SpaceWeaveCubemapToEquirect.shader',
    'Runtime/Shaders/SpaceWeaveCylindricalFromCubemap.shader',
    'Runtime/Shaders/SpaceWeaveCubemapPack.shader',
    'Runtime/Shaders/SpaceWeaveFisheyeFromCubemap.shader',
    'Runtime/Shaders/SpaceWeaveEacFromCubemap.shader',
    'Runtime/Shaders/SpaceWeaveSourceTruthPattern.shader',
    'Editor/SpaceWeaveSpoutOutputMirrorWindow.cs',
    'Samples~/OutputValidation/Scenes/SpaceWeave_Sample.unity',
    'Samples~/OutputValidation/Scripts/SpaceWeaveSampleBootstrap.cs',
    'README.md',
    'INSTALL.md'
)
foreach ($r in $required) {
    if (-not (Test-Path (Join-Path $root $r))) { $fails.Add("missing $r") }
}

$scene = Get-Content (Join-Path $root 'Samples~/OutputValidation/Scenes/SpaceWeave_Sample.unity') -Raw
foreach ($needle in @('SpaceWeaveOutputManager', 'SpoutSender', 'NdiSender', 'senderBaseName: SpaceWeave')) {
    if ($scene -notlike "*$needle*") { $fails.Add("scene missing $needle") }
}

$leak = Select-String -Path (Join-Path $root 'Runtime/Scripts/*.cs'), (Join-Path $root 'Editor/*.cs') `
    -Pattern 'CAVEOutputManager|CubemapRendererImproved|CAVEPipelineController|AuditCompetingWriters' `
    -SimpleMatch -ErrorAction SilentlyContinue
if ($leak) { $fails.Add("Grimsholt leak: $($leak.Path)") }

if ($fails.Count -eq 0) {
    Write-Host 'STATIC_VERIFY_PASS'
    exit 0
}
Write-Host 'STATIC_VERIFY_FAIL'
$fails | ForEach-Object { Write-Host " - $_" }
exit 1
