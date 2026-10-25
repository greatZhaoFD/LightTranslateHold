[CmdletBinding()]
param([switch]$Test)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'src'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
if (!(Test-Path -LiteralPath $csc)) { throw '.NET Framework 4.8 compiler not found.' }

$refs = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Net.Http.dll',
    'System.Web.Extensions.dll',
    'System.Security.dll',
    (Join-Path $wpf 'UIAutomationClient.dll'),
    (Join-Path $wpf 'UIAutomationTypes.dll'),
    (Join-Path $wpf 'WindowsBase.dll')
)
foreach ($ref in $refs) {
    if (!(Test-Path -LiteralPath $ref) -and !(Test-Path -LiteralPath (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\$ref"))) {
        throw "Missing reference: $ref"
    }
}

$sources = @(Get-ChildItem -LiteralPath $src -Filter '*.cs' | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw 'No source files found.' }

$exe = Join-Path $root 'LightTranslateHold.exe'
$arguments = @(
    '/nologo',
    '/target:winexe',
    '/main:LightTranslateHold.Program',
    '/platform:anycpu',
    '/optimize+',
    '/utf8output',
    '/codepage:65001',
    ('/win32icon:' + (Join-Path $src 'assets\LightTranslateHold.ico')),
    ('/win32manifest:' + (Join-Path $src 'app.manifest')),
    ('/out:' + $exe)
)
foreach ($ref in $refs) { $arguments += ('/r:' + $ref) }

& $csc @($arguments + $sources)
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output "Built: $exe"

if ($Test) {
    $testRoot = Join-Path $env:TEMP ('LightTranslateHold-build-' + [Guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Force -Path $testRoot
    try {
        $testRunner = Join-Path $testRoot 'TestRunner.exe'
        $testArgs = @(
            '/nologo',
            '/target:exe',
            '/main:LightTranslateHold.ConsoleTest',
            '/platform:anycpu',
            '/optimize+',
            '/utf8output',
            '/codepage:65001',
            ('/out:' + $testRunner)
        )
        foreach ($ref in $refs) { $testArgs += ('/r:' + $ref) }
        & $csc @($testArgs + $sources)
        if ($LASTEXITCODE -ne 0) { throw 'Test runner compilation failed.' }

        $testData = Join-Path $testRoot 'test-data'
        $null = New-Item -ItemType Directory -Force -Path $testData
        & $testRunner --self-test --data-dir $testData
        if ($LASTEXITCODE -ne 0) { throw "Self-test failed (exit $LASTEXITCODE)." }
        Write-Output 'Self-test passed.'
    }
    finally {
        Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
