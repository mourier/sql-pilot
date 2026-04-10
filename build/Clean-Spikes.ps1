#Requires -RunAsAdministrator
# Remove spike extensions from all SSMS versions
$paths = @(
    "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\SqlPilotSpike",
    "C:\Program Files (x86)\Microsoft SQL Server Management Studio 20\Common7\IDE\Extensions\SqlPilotSpike",
    "C:\Program Files (x86)\Microsoft SQL Server Management Studio 18\Common7\IDE\Extensions\SqlPilotSpike"
)
foreach ($p in $paths) {
    if (Test-Path $p) {
        Remove-Item $p -Recurse -Force
        Write-Host "Removed: $p" -ForegroundColor Yellow
    }
}

# Also clean caches
$dataDirs = @(
    (Get-ChildItem "$env:LOCALAPPDATA\Microsoft\SSMS" -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^22\.' } | Select-Object -First 1),
    (Get-ChildItem "$env:LOCALAPPDATA\Microsoft\SQL Server Management Studio" -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^18\.' } | Select-Object -First 1),
    (Get-ChildItem "$env:LOCALAPPDATA\Microsoft\SQL Server Management Studio" -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^20\.' } | Select-Object -First 1)
)
foreach ($d in $dataDirs) {
    if ($d) {
        foreach ($f in @("privateregistry.bin", "privateregistry.bin.LOG1", "privateregistry.bin.LOG2")) {
            $path = Join-Path $d.FullName $f
            if (Test-Path $path) { Remove-Item $path -Force -ErrorAction SilentlyContinue }
        }
        $cache = Join-Path $d.FullName "ComponentModelCache"
        if (Test-Path $cache) { Remove-Item $cache -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
Write-Host "Spikes cleaned, caches cleared." -ForegroundColor Green
Write-Host "Press any key..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
