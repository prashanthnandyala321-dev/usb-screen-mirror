# USB Screen Mirror Pro Build Script
$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  Building USB Screen Mirror Pro (WPF Executable)  " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $cscPath)) {
    Write-Error "C# Compiler csc.exe not found at $cscPath"
}

$source = Join-Path $PSScriptRoot "UsbScreenMirror.cs"
$output = Join-Path $PSScriptRoot "UsbScreenMirror.exe"

# Assembly References
$references = @(
    "System.dll",
    "System.Drawing.dll",
    "System.Windows.Forms.dll",
    "System.Management.dll",
    "System.Core.dll",
    "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll",
    "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll"
)

$refArgs = $references | ForEach-Object { "/r:`"$_`"" }

Write-Host "Compiling $source -> $output ..." -ForegroundColor Yellow
$cmdArgs = @("/target:winexe", "/optimize+", "/out:`"$output`"") + $refArgs + @("`"$source`"")

$process = Start-Process -FilePath $cscPath -ArgumentList $cmdArgs -NoNewWindow -Wait -PassThru

if ($process.ExitCode -eq 0 -and (Test-Path $output)) {
    Write-Host "SUCCEEDED! Binary compiled successfully:" -ForegroundColor Green
    Write-Host "  Executable: $output" -ForegroundColor Green
} else {
    Write-Error "Compilation FAILED with exit code $($process.ExitCode)"
}
