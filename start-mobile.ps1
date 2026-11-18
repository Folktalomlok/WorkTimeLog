<#
.SYNOPSIS
  รันเซิร์ฟเวอร์ WorkTimeLog แล้วเปิด Cloudflare Tunnel เพื่อให้เปิดบนมือถือผ่านอินเทอร์เน็ตได้

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\start-mobile.ps1          # เริ่ม (กด Ctrl+C เพื่อหยุดทั้งหมด)
  powershell -ExecutionPolicy Bypass -File .\start-mobile.ps1 -Stop    # หยุดเซิร์ฟเวอร์และ tunnel ที่ค้างอยู่
#>
param(
    [int]$Port = 5000,
    [switch]$Stop
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch {}
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
Set-Location -LiteralPath $PSScriptRoot

$logDir     = Join-Path $PSScriptRoot 'logs'
$serverLog  = Join-Path $logDir 'server.log'
$serverErr  = Join-Path $logDir 'server.err.log'
$tunnelLog  = Join-Path $logDir 'tunnel.log'
$tunnelOut  = Join-Path $logDir 'tunnel.out.log'
$pidFile    = Join-Path $logDir 'pids.txt'
New-Item -ItemType Directory -Force $logDir | Out-Null

function Stop-Tree([int]$ProcessId) {
    if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
        & taskkill.exe /PID $ProcessId /T /F 2>&1 | Out-Null
    }
}

function Stop-All {
    if (Test-Path $pidFile) {
        Get-Content $pidFile | ForEach-Object { if ($_ -match '^\d+$') { Stop-Tree ([int]$_) } }
        Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
    }
    # เผื่อมี process ค้างที่ไม่ได้เริ่มจากสคริปต์นี้
    Get-Process WorkTimeLog.Api, cloudflared -ErrorAction SilentlyContinue | ForEach-Object { Stop-Tree $_.Id }
}

if ($Stop) {
    Stop-All
    Write-Host 'หยุดเซิร์ฟเวอร์และ tunnel แล้ว' -ForegroundColor Green
    return
}

# ---------- โหลด PATH ใหม่ (กรณีเพิ่งติดตั้ง dotnet / cloudflared) ----------
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
$env:DOTNET_NOLOGO = '1'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'ไม่พบ .NET SDK — ติดตั้งด้วย: winget install Microsoft.DotNet.SDK.8'
}

$cloudflared = (Get-Command cloudflared -ErrorAction SilentlyContinue).Source
if (-not $cloudflared) {
    $cloudflared = @(
        "${env:ProgramFiles(x86)}\cloudflared\cloudflared.exe",
        "$env:ProgramFiles\cloudflared\cloudflared.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $cloudflared) {
    throw 'ไม่พบ cloudflared — ติดตั้งด้วย: winget install --id Cloudflare.cloudflared แล้วรันสคริปต์ใหม่'
}

function Test-Server {
    try { Invoke-WebRequest "http://localhost:$Port/api/projects" -UseBasicParsing -TimeoutSec 3 | Out-Null; $true }
    catch { $false }
}

$pids = @()
try {
    # ---------- 1) เซิร์ฟเวอร์ ----------
    if (Test-Server) {
        Write-Host "พบเซิร์ฟเวอร์ทำงานอยู่แล้วที่พอร์ต $Port — ใช้ตัวเดิม" -ForegroundColor Yellow
    } else {
        Write-Host 'กำลังเริ่มเซิร์ฟเวอร์ (dotnet run) ...' -ForegroundColor Cyan
        $server = Start-Process dotnet -ArgumentList 'run', '--project', 'src/WorkTimeLog.Api' `
            -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput $serverLog -RedirectStandardError $serverErr
        $pids += $server.Id
        Set-Content $pidFile $pids
        $deadline = (Get-Date).AddSeconds(120)
        while (-not (Test-Server)) {
            if ($server.HasExited) { throw "เซิร์ฟเวอร์หยุดทำงาน ดู log ที่ $serverLog" }
            if ((Get-Date) -gt $deadline) { throw "เซิร์ฟเวอร์ไม่ตอบภายใน 120 วินาที ดู log ที่ $serverLog" }
            Start-Sleep -Milliseconds 700
        }
        Write-Host "เซิร์ฟเวอร์พร้อม: http://localhost:$Port" -ForegroundColor Green
    }

    # ---------- 2) Cloudflare Tunnel ----------
    Write-Host 'กำลังเปิด Cloudflare Tunnel ...' -ForegroundColor Cyan
    Remove-Item $tunnelLog -Force -ErrorAction SilentlyContinue
    $tunnel = Start-Process $cloudflared `
        -ArgumentList 'tunnel', '--no-autoupdate', '--url', "http://localhost:$Port", '--logfile', "`"$tunnelLog`"" `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput $tunnelOut -RedirectStandardError (Join-Path $logDir 'tunnel.err.log')
    $pids += $tunnel.Id
    Set-Content $pidFile $pids

    $url = $null
    $deadline = (Get-Date).AddSeconds(60)
    while (-not $url) {
        if ($tunnel.HasExited) { throw "cloudflared หยุดทำงาน ดู log ที่ $tunnelLog" }
        if ((Get-Date) -gt $deadline) { throw "ไม่ได้รับลิงก์จาก cloudflared ภายใน 60 วินาที ดู log ที่ $tunnelLog" }
        Start-Sleep -Milliseconds 700
        foreach ($f in @($tunnelLog, (Join-Path $logDir 'tunnel.err.log'))) {
            if (Test-Path $f) {
                $m = Select-String -Path $f -Pattern 'https://[a-z0-9-]+\.trycloudflare\.com' -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($m) { $url = $m.Matches[0].Value; break }
            }
        }
    }

    # รอให้ลิงก์ใช้งานได้จริง (ช่วงแรกอาจได้ 502 / DNS ยังไม่พร้อม)
    $ready = $false
    for ($i = 0; $i -lt 30 -and -not $ready; $i++) {
        try { Invoke-WebRequest "$url/api/projects" -UseBasicParsing -TimeoutSec 5 | Out-Null; $ready = $true }
        catch { Start-Sleep -Seconds 2 }
    }

    try { Set-Clipboard -Value $url } catch {}
    Write-Host ''
    Write-Host '==============================================================' -ForegroundColor Green
    Write-Host ' เปิดบนมือถือได้ที่ลิงก์นี้ (คัดลอกไว้ใน clipboard แล้ว):' -ForegroundColor Green
    Write-Host "   $url" -ForegroundColor White
    if (-not $ready) { Write-Host ' (ลิงก์อาจต้องรออีกสักครู่จึงเปิดได้)' -ForegroundColor Yellow }
    Write-Host " บนเครื่องนี้: http://localhost:$Port    Swagger: $url/swagger"
    Write-Host ' กด Ctrl+C เพื่อหยุดเซิร์ฟเวอร์และ tunnel'
    Write-Host '==============================================================' -ForegroundColor Green

    while ($true) {
        Start-Sleep -Seconds 2
        if ($tunnel.HasExited) { Write-Host 'cloudflared หยุดทำงาน' -ForegroundColor Red; break }
        if ($server -and $server.HasExited) { Write-Host 'เซิร์ฟเวอร์หยุดทำงาน' -ForegroundColor Red; break }
    }
}
finally {
    # ทำงานทั้งตอนกด Ctrl+C และตอนเกิด error
    foreach ($p in $pids) { Stop-Tree $p }
    Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
    Write-Host 'หยุดเซิร์ฟเวอร์และ tunnel แล้ว' -ForegroundColor Yellow
}
