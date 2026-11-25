$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('BAAxisReview-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$sdk = Join-Path $env:LOCALAPPDATA 'BAAxis/dotnet/dotnet.exe'
if (-not (Test-Path $sdk)) { $sdk = (Get-Command dotnet).Source }
foreach ($name in @('Model.cs', 'Runner.cs', 'SelfTest.cs', 'RestartDetection.cs', 'SkillPlan.cs', 'UnifiedAxis.cs')) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $name) -Destination $scratch
}
# Intercept the final SendInput boundary in a temporary copy. No input reaches Windows.
$native = Get-Content -Raw (Join-Path $sourceRoot 'Native.cs')
$declaration = '[DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);'
if (-not $native.Contains($declaration)) { throw 'Native input boundary changed; update this acceptance harness.' }
$native.Replace($declaration, 'private static uint SendInput(uint count, Input[] inputs, int size) => Acceptance.CaptureInput(count, inputs, size);') | Set-Content (Join-Path $scratch 'Native.cs') -Encoding utf8
Copy-Item (Join-Path $PSScriptRoot 'Acceptance.cs.txt') (Join-Path $scratch 'Acceptance.cs')
Copy-Item (Join-Path $sourceRoot 'Assets/restart-menu.png') (Join-Path $scratch 'restart-menu.png')
@'
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup>
  <OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework>
  <UseWindowsForms>true</UseWindowsForms><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
 </PropertyGroup>
 <ItemGroup><EmbeddedResource Include="restart-menu.png" LogicalName="BAAxis.Assets.restart-menu.png" /></ItemGroup>
</Project>
'@ | Set-Content (Join-Path $scratch 'Review.csproj') -Encoding utf8
Push-Location $scratch
try {
    & $sdk build -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Acceptance build failed.' }
    & $sdk (Join-Path $scratch 'bin/Release/net10.0-windows/Review.dll')
    $result = $LASTEXITCODE
    Copy-Item (Join-Path $scratch 'results.txt') (Join-Path $PSScriptRoot 'latest-results.txt')
    Write-Output "Acceptance exit code $result. Temporary project $scratch"
} finally { Pop-Location }
exit $result

