<#
.SYNOPSIS
  CoinSvr FundingHedger/SpikeScalp jsonl 로그 날짜별 로테이션 스크립트.

.DESCRIPTION
  trade_results.jsonl / skipped_results.jsonl / spike_scalp_results.jsonl /
  spike_scalp_skipped.jsonl / spike_scalp_postexit.jsonl / spike_scalp_trendveto_sim.jsonl
  6개 파일은 계속 append만 되고 잘리지 않아 무한히 커진다(2026-07-19 기준 spike_scalp_skipped.jsonl
  이미 19MB/11만 줄 이상). 이 스크립트는 "오늘(UTC) 이전" 날짜의 레코드만
  CoinSvr\bin\Debug\net9.0\DataArchive\<YYYYMMDD>\<파일명>으로 옮기고, 라이브 파일에는 오늘 날짜
  데이터(+ Timestamp 파싱 실패 레코드)만 남긴다. 오늘 날짜 데이터는 절대 옮기지 않는다 — 서비스가
  지금도 그 파일에 계속 append 중이기 때문.

  안전성: CoinSvr.exe는 이 6개 파일 전부를 `File.AppendAllText`로 매번 열고-쓰고-닫는다(핸들을
  계속 잡고 있지 않음, Fundinghedger .cs/SpikeScalpManager.cs 확인됨) — 그래서 로테이션 중 파일을
  잠깐 rename해도 안전하다: 그 틈에 새 레코드가 append되면 서비스가 같은 경로에 새 파일을 다시
  만들 뿐이고, 이 스크립트는 로테이션 종료 시 그 내용까지 읽어서 합쳐 되돌려 놓는다(데이터 유실
  없음, WhatIf 모드에서도 동일하게 병합 복구한다). 극히 짧은 순간(파일 교체 직후 수 ms) 정확히
  겹치면 C# 쪽에서 IOException이 날 수 있으나, 모든 로깅 함수가 이미 try/catch로 감싸여 있어 그
  한 줄만 UI 로그에 에러로 남고 서비스 자체는 영향 없다.

  이 스크립트는 매 분석 사이클(FundingHedger 6시간/SpikeScalp 1시간) 맨 앞에서 호출하도록
  설계되었다 — 옮길 게 없는 날(대부분의 실행)은 각 파일을 한 번 훑고 마는 정도라 빠르다(11만 줄
  급 파일도 regex 기반 분류로 1~2초).

.PARAMETER BaseDir
  jsonl 파일들이 있는 디렉터리. 기본값 CoinSvr\bin\Debug\net9.0.

.PARAMETER WhatIf
  실제로 옮기지 않고 무엇을 옮길지만 출력(라이브 파일은 처리 후 항상 원래 내용 + 처리 중 신규
  유입분으로 복구되므로 WhatIf든 아니든 라이브 파일 내용 자체는 안전).

.EXAMPLE
  powershell -File rotate_data_logs.ps1
  powershell -File rotate_data_logs.ps1 -WhatIf
#>

param(
    [string]$BaseDir = $(Join-Path $PSScriptRoot "CoinSvr\bin\Debug\net9.0"),
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

$targetFiles = @(
    "trade_results.jsonl",
    "skipped_results.jsonl",
    "spike_scalp_results.jsonl",
    "spike_scalp_skipped.jsonl",
    "spike_scalp_postexit.jsonl",
    "spike_scalp_trendveto_sim.jsonl"
)

$archiveRoot = Join-Path $BaseDir "DataArchive"
$todayUtc = (Get-Date).ToUniversalTime().ToString("yyyyMMdd")
$isWhatIf = [bool]$WhatIf
$tsDateRegex = [regex]'"Timestamp":"(\d{4})-(\d{2})-(\d{2})'

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# C#의 File.AppendAllText는 BOM 없는 UTF-8로 쓴다 - PowerShell 5.1의 `-Encoding utf8`은 BOM을
# 붙여버려서(Set-Content/Add-Content 공통) archive/라이브 파일 첫 줄이 `{`가 아니라 BOM+`{`로
# 시작해 일부 JSON 파서(특히 Python open()의 기본 'utf-8')가 첫 레코드만 파싱 실패하는 문제가
# 있었다 - .NET File 메서드로 직접 써서 BOM 없이 통일한다.
function Add-LinesNoBom {
    param([string]$Path, [string[]]$Lines)
    if ($Lines.Count -eq 0) { return }
    $content = (($Lines -join "`r`n") + "`r`n")
    if (Test-Path $Path) {
        [System.IO.File]::AppendAllText($Path, $content, $utf8NoBom)
    } else {
        [System.IO.File]::WriteAllText($Path, $content, $utf8NoBom)
    }
}

function Set-LinesNoBom {
    param([string]$Path, [string[]]$Lines)
    $content = if ($Lines.Count -eq 0) { "" } else { (($Lines -join "`r`n") + "`r`n") }
    [System.IO.File]::WriteAllText($Path, $content, $utf8NoBom)
}

function Get-LineDate {
    param([string]$Line)
    $m = $tsDateRegex.Match($Line)
    if ($m.Success) {
        return "$($m.Groups[1].Value)$($m.Groups[2].Value)$($m.Groups[3].Value)"
    }
    # 정규식이 못 잡는 형태(필드 순서가 다르거나 손상된 줄)일 때만 느린 전체 파싱으로 폴백.
    try {
        $obj = $Line | ConvertFrom-Json -ErrorAction Stop
        if ($obj.Timestamp) {
            return ([datetime]::Parse($obj.Timestamp)).ToUniversalTime().ToString("yyyyMMdd")
        }
    } catch { }
    return $null
}

function Rotate-OneFile {
    param([string]$FileName)

    $livePath = Join-Path $BaseDir $FileName
    if (-not (Test-Path $livePath)) {
        Write-Host "  [$FileName] 파일 없음 - 건너뜀"
        return
    }

    $rotatingPath = "$livePath.rotating"
    if (Test-Path $rotatingPath) {
        Write-Host "  [$FileName] 이전 로테이션 잔재($rotatingPath) 발견 - 이번엔 건너뜀(수동 확인 필요)"
        return
    }

    # 1) 원자적 rename: 이 순간 이후의 신규 append는 서비스가 같은 경로에 새 파일을 만들어 받는다.
    Rename-Item -Path $livePath -NewName (Split-Path $rotatingPath -Leaf) -ErrorAction Stop

    $failed = $false
    $keepLines = @()
    $archiveGroups = @()

    try {
        $rawLines = @(Get-Content -Path $rotatingPath -ErrorAction SilentlyContinue) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

        # 파이프라인 기반 분류(Where-Object/Group-Object) - 명령형 foreach+해시테이블 누적 방식에서
        # 원인 불명의 분류 오류(오늘 날짜 레코드가 간헐적으로 archive 쪽으로 잘못 분류)가 재현되어
        # 이 방식으로 교체, 격리 테스트에서 정상 동작 확인됨.
        $tagged = $rawLines | ForEach-Object {
            [PSCustomObject]@{ Line = $_; Date = (Get-LineDate -Line $_) }
        }

        $keepLines = @($tagged | Where-Object { $null -eq $_.Date -or $_.Date -ge $todayUtc } | ForEach-Object { $_.Line })
        $archiveItems = @($tagged | Where-Object { $null -ne $_.Date -and $_.Date -lt $todayUtc })
        $archiveGroups = @($archiveItems | Group-Object -Property Date | Sort-Object Name)

        $todayCount = ($tagged | Where-Object { $null -ne $_.Date -and $_.Date -ge $todayUtc }).Count
        $unparsed = ($tagged | Where-Object { $null -eq $_.Date }).Count
        $archivedTotal = $archiveItems.Count

        foreach ($grp in $archiveGroups) {
            if (-not $isWhatIf) {
                $destDir = Join-Path $archiveRoot $grp.Name
                if (-not (Test-Path $destDir)) {
                    New-Item -ItemType Directory -Path $destDir -Force | Out-Null
                }
                $destFile = Join-Path $destDir $FileName
                Add-LinesNoBom -Path $destFile -Lines @($grp.Group | ForEach-Object { $_.Line })
            }
        }
    }
    catch {
        $failed = $true
        Write-Host "  [$FileName] 처리 중 오류: $($_.Exception.Message) - 아카이브 건너뛰고 원본 그대로 라이브 복구"
        # 오류 시점까지 뭘 archive했는지 신뢰할 수 없으므로, 안전을 위해 원본 전체를 그대로 복구 대상으로 삼는다.
        $keepLines = @(Get-Content -Path $rotatingPath -ErrorAction SilentlyContinue)
        $archiveGroups = @()
        $todayCount = 0
        $unparsed = 0
        $archivedTotal = 0
    }

    if ($isWhatIf -and -not $failed) {
        foreach ($grp in $archiveGroups) {
            Write-Host "  [$FileName] (WhatIf) $($grp.Name) -> $($grp.Count)줄 archive 예정"
        }
        Write-Host ("  [{0}] (WhatIf) 라이브 유지 예정: 오늘 {1}줄 + 파싱실패 {2}줄, 아카이브 대상 {3}줄({4}개 날짜)" -f `
            $FileName, $todayCount, $unparsed, $archivedTotal, $archiveGroups.Count)
    }

    # WhatIf 모드에서는 아무것도 실제로 옮기면 안 되므로, 라이브 파일에 되돌릴 내용은 "오늘 것만
    # 남긴 keepLines"가 아니라 원본 전체(rotatingPath의 모든 줄)여야 한다 - 2026-07-19 사고
    # (WhatIf인데도 과거 날짜가 라이브에서 실제로 빠지고 archive에는 안 쓰여 데이터 유실) 재발 방지.
    $restoreLines = if ($isWhatIf -and -not $failed) {
        @(Get-Content -Path $rotatingPath -ErrorAction SilentlyContinue)
    } else {
        $keepLines
    }

    # 2) 로테이션 처리 중(또는 오류 복구 중) 서비스가 같은 경로에 다시 만든 신규 파일(있다면) 내용을
    #    합쳐서 되돌린다 - WhatIf든 아니든 라이브 파일 복구 절차는 동일하다(WhatIf는 archive 파일을
    #    안 쓸 뿐, 라이브 파일 자체는 항상 안전하게 원상복구).
    $newSinceRename = @()
    if (Test-Path $livePath) {
        $newSinceRename = @(Get-Content -Path $livePath -ErrorAction SilentlyContinue)
    }

    $final = @()
    $final += $restoreLines
    $final += $newSinceRename

    if ($final.Count -gt 0) {
        Set-LinesNoBom -Path $livePath -Lines $final
    }
    # final이 비었으면 livePath를 새로 만들지 않는다 - 다음 append가 알아서 생성.

    Remove-Item -Path $rotatingPath -Force -ErrorAction SilentlyContinue

    if (-not $isWhatIf -and -not $failed) {
        Write-Host ("  [{0}] 아카이브 {1}줄({2}개 날짜) / 라이브 유지 {3}줄(오늘 {4} + 파싱실패 {5} + 로테이션중신규유입 {6})" -f `
            $FileName, $archivedTotal, $archiveGroups.Count, $final.Count, $todayCount, $unparsed, $newSinceRename.Count)
    }
}

Write-Host "================================================================"
Write-Host " 데이터 로그 로테이션 - BaseDir: $BaseDir"
Write-Host " 오늘(UTC) 날짜: $todayUtc / ArchiveRoot: $archiveRoot"
if ($isWhatIf) { Write-Host " (WhatIf 모드 - 실제로 옮기지 않음)" }
Write-Host "================================================================"

foreach ($f in $targetFiles) {
    Rotate-OneFile -FileName $f
}

Write-Host "================================================================"
Write-Host " 로테이션 종료"
Write-Host "================================================================"
