<#
.SYNOPSIS
  게임(CostaDelSol.exe)을 화면 밖에서 띄워 스크린샷을 찍고 끈다 — 사람 화면 · 포커스 · 스피커 · 세이브를 안 건드린다.

.DESCRIPTION
  환경 변수(CdsHelper.Support/Local/Helpers/OffscreenTest.cs)를 걸어 띄운다.
    CDS_OFFSCREEN=1   창을 (-8000,-8000)에 포커스 · 작업 표시줄 단추 없이
    CDS_SAVEDIR       세이브 · 설정을 <OutDir>\save 로(사용자 %APPDATA%\CdsHelper 는 읽기만)
    CDS_MUTE          화면 밖이면 저절로 소리 끔
    CDS_START         sea:<도시>:<선체 key> — 타이틀을 건너뛰고 그 도시 앞바다에서 그 배로 항해 지도에
    CDS_HEADING/SAIL  뱃머리(0 북 · 1 북서 · 2 서 · 3 남서 · 4 남 · 5 남동 · 6 동 · 7 북동)와 저절로 나아가기
    CDS_SHOT          <png>:<초>;… — 그 초에 찍고 마지막 것을 찍으면 앱이 스스로 닫힌다
  -PostAt 을 주면 그 초에 창에 0x8001 을 보내 <OutDir>\save\cds_snapshot.png 를 받는다(앱이 떠 있는 채로 찍는 길).
  끝나면 이 스크립트가 띄운 PID 만 끈다 — taskkill /IM 은 안 쓴다(사람이 띄워 둔 게임이 있을 수 있다).

.EXAMPLE
  pwsh -File tools/offscreen-shot.ps1 -Build -Start sea:24:ketch -Heading 2 -Sail -Shots 6,9 -Name ketch-west
#>
param(
    [string]$Exe = "",
    [switch]$Build,
    [string]$BuildDir = "",
    [string]$OutDir = "",
    [string]$Start = "",
    [int]$Heading = -1,
    [switch]$Sail,
    [double[]]$Shots = @(),
    [double]$PostAt = 0,
    [string]$Name = "shot",
    [int]$Timeout = 90,
    [switch]$KeepSave
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) "cds-offscreen" }
if (-not $BuildDir) { $BuildDir = Join-Path $OutDir "build" }
New-Item -ItemType Directory -Force $OutDir | Out-Null

# 굽기 — 사용자의 bin\Debug 가 아니라 따로 둔 폴더로(띄워 둔 게임이 bin 을 잠그고 있을 수 있다).
if ($Build) {
    Write-Host "굽는 중 → $BuildDir"
    dotnet build (Join-Path $repo "CostaDelSol.Play/CostaDelSol.Play.csproj") -v q --nologo -o $BuildDir | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "빌드 실패" }
}
if (-not $Exe) { $Exe = Join-Path $BuildDir "CostaDelSol.exe" }
if (-not (Test-Path $Exe)) { throw "exe 가 없다: $Exe (-Build 를 붙이거나 -Exe 로 알려 주세요)" }

# 시험 폴더 — 사람 세이브 · 설정은 안 쓴다. 판마다 새로(-KeepSave 면 이어서).
$save = Join-Path $OutDir "save"
if ((Test-Path $save) -and -not $KeepSave) { Remove-Item -Recurse -Force $save }
New-Item -ItemType Directory -Force $save | Out-Null

$psi = [Diagnostics.ProcessStartInfo]::new($Exe)
$psi.UseShellExecute = $false
$psi.WorkingDirectory = Split-Path -Parent $Exe
$psi.EnvironmentVariables["CDS_OFFSCREEN"] = "1"
$psi.EnvironmentVariables["CDS_SAVEDIR"] = $save
$psi.EnvironmentVariables["CDS_MUTE"] = "1"
if ($Start) { $psi.EnvironmentVariables["CDS_START"] = $Start }
if ($Heading -ge 0) { $psi.EnvironmentVariables["CDS_HEADING"] = "$Heading" }
if ($Sail) { $psi.EnvironmentVariables["CDS_SAIL"] = "1" }
$pngs = @()
if ($Shots.Count -gt 0) {
    $spec = ($Shots | ForEach-Object {
        $png = Join-Path $OutDir ("{0}-{1}s.png" -f $Name, $_)
        if (Test-Path $png) { Remove-Item -Force $png }
        $script:pngs += $png
        "{0}:{1}" -f $png, ([string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0}", $_))
    }) -join ";"
    $psi.EnvironmentVariables["CDS_SHOT"] = $spec
}

$proc = [Diagnostics.Process]::Start($psi)
$procId = $proc.Id
Write-Host "띄웠다 PID $procId (화면 밖)"

try {
    if ($PostAt -gt 0) {
        Add-Type -Namespace Cds -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
'@
        Start-Sleep -Milliseconds ([int]($PostAt * 1000))
        $hwndFile = Join-Path $save "cds_hwnd.txt"
        $snap = Join-Path $save "cds_snapshot.png"
        if (-not (Test-Path $hwndFile)) { throw "창 핸들 파일이 없다: $hwndFile" }
        $hwnd = [IntPtr][long](Get-Content $hwndFile -Raw).Trim()
        $before = if (Test-Path $snap) { (Get-Item $snap).LastWriteTimeUtc } else { [DateTime]::MinValue }
        [Cds.Win]::PostMessageW($hwnd, 0x8001, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        $deadline = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $deadline -and -not ((Test-Path $snap) -and (Get-Item $snap).LastWriteTimeUtc -gt $before)) {
            Start-Sleep -Milliseconds 200
        }
        if ((Test-Path $snap) -and (Get-Item $snap).LastWriteTimeUtc -gt $before) {
            $png = Join-Path $OutDir ("{0}-post.png" -f $Name)
            Copy-Item -Force $snap $png
            $pngs += $png
        } else { Write-Warning "스냅샷이 안 왔다" }
    }
    if ($Shots.Count -gt 0) {
        # CDS_SHOT 은 마지막 것을 찍으면 스스로 닫힌다 — 그때까지 기다린다.
        if (-not $proc.WaitForExit($Timeout * 1000)) { Write-Warning "$Timeout 초 안에 안 닫혔다" }
    }
}
finally {
    # 내가 띄운 PID 만 끈다.
    $proc.Refresh()
    if (-not $proc.HasExited) {
        Stop-Process -Id $procId -Force
        $proc.WaitForExit(10000) | Out-Null
        Write-Host "PID $procId 를 껐다"
    } else { Write-Host "PID $procId 는 스스로 닫혔다" }
}

$log = Join-Path $save "cds_test.log"
if (Test-Path $log) { Write-Host "--- cds_test.log"; Get-Content $log | Out-Host }
foreach ($p in $pngs) { Write-Host ("{0}  {1}" -f $(if (Test-Path $p) { "OK " } else { "없음" }), $p) }
