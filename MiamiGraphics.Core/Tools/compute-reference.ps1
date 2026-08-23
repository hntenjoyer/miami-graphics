param(
    [Parameter(Mandatory=$true)][string]$cleanUpdate,
    [Parameter(Mandatory=$true)][string]$gtaExe
)

if (-not (Test-Path $cleanUpdate)) {
    Write-Error "File not found: $cleanUpdate"
    exit 1
}

$file = Get-Item $cleanUpdate
$hash = (Get-FileHash -Path $cleanUpdate -Algorithm SHA256).Hash.ToLower()
$size = $file.Length

if (Test-Path $gtaExe) {
    $exeVersion = (Get-Item $gtaExe).VersionInfo.FileVersion
} else {
    $exeVersion = '1.0.3788.0'
    Write-Warning "GTA5.exe not found at $gtaExe - using fallback $exeVersion"
}

Write-Output "exeVersion = $exeVersion"
Write-Output "size       = $size"
Write-Output "sha256     = $hash"
