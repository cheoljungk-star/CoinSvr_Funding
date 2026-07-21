<#
.SYNOPSIS
  CoinSvr FundingHedger C전략 자동튜닝 사이클용 고정 분석 스크립트.
  매 사이클 Claude Code가 즉석에서 파싱 스크립트를 새로 작성/디버깅하는 대신,
  이 스크립트 하나를 재사용해서 턴(및 비용) 낭비를 줄이는 것이 목적.

.DESCRIPTION
  trade_results.jsonl + skipped_results.jsonl을 합쳐서:
    0) 상태파일(analysis_state.json)의 LastCutoffUtc를 기본 컷오프로 사용 - "마지막 분석
       이후"만 정확히 가져온다(-SinceHours는 상태파일이 없을 때의 폴백이거나, 명시적으로
       넘기면 상태파일보다 우선). 컷오프 날짜가 오늘 이전이면 rotate_data_logs.ps1이 만든
       DataArchive\<날짜>\ 아래도 함께 읽는다(라이브 파일은 오늘 것만 남아있으므로).
    1) A/B/C(paper) 평균/승률 비교, IsBait=false만 별도 집계
    2) 실측(Actual_ProfitPct) 대조 (trade_results.jsonl에만 존재)
    3) 트레일링청산(peak>0) 목록 + giveback 비율, MaxHoldMs 타임아웃(peak=0) 건수
    4) Trend30Pct 유효 레코드만 절댓값 구간(약/중/강)별 C/A/B 비교 (적응형 3분위, 이번 사이클 배치 기준)
    5) 베이지안 누적 승률 비교 (Beta-Bernoulli) - bayes_state.json에 전체 이력을 누적하여
       "C 승률", "C vs A_Est", "C vs B_Est"의 사후분포 평균/95% 신용구간을 계산한다.
       사이클마다 n=1~10건씩만 들어와 빈도주의 판단(누적평균/승률)이 매번 뒤집히는 문제를
       완화하기 위함 - 4)번과 달리 구간 경계를 고정값(추세 10%/30%)으로 써서 사이클 간
       누적이 의미를 갖게 한다. 신용구간이 50%를 걸치면 "판단보류"로 명시한다.
  를 콘솔에 표 형태로 출력한다. 여기서 나온 표를 근거로 자연어 요약(CLAUDE.md,
  automation_summary.log에 적을 문장)은 Claude가 직접 작성한다 — 이 스크립트는
  숫자 집계까지만 담당하고 해석/서술은 하지 않는다. 종료 전 analysis_state.json의
  LastCutoffUtc를 이번 실행 시각으로 갱신한다(다음 사이클이 이어받을 수 있도록).

.PARAMETER SinceHours
  상태파일(analysis_state.json)이 없을 때만 쓰이는 폴백. 이 시간(시간 단위) 이내에
  생성된 레코드만 분석 대상으로 삼는다. 기본 12시간. 이 값을 명시적으로 넘기면
  상태파일이 있어도 무시하고 강제로 -SinceHours 기준을 쓴다(수동 임시분석용).

.PARAMETER BaseDir
  trade_results.jsonl / skipped_results.jsonl / DataArchive / analysis_state.json이 있는
  디렉터리. 기본값은 스크립트 위치 기준 CoinSvr\bin\Debug\net9.0.

.EXAMPLE
  powershell -File analyze_cycle.ps1
  powershell -File analyze_cycle.ps1 -SinceHours 24
#>

param(
    [double]$SinceHours = 12,
    [string]$BaseDir = $(Join-Path $PSScriptRoot "CoinSvr\bin\Debug\net9.0")
)

$ErrorActionPreference = "Stop"
$sinceHoursExplicit = $PSBoundParameters.ContainsKey('SinceHours')

function Read-Jsonl {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return @() }
    Get-Content $Path -ErrorAction SilentlyContinue | Where-Object { $_.Trim() -ne "" } | ForEach-Object {
        try { $_ | ConvertFrom-Json } catch { }
    }
}

# rotate_data_logs.ps1이 "오늘 이전" 날짜를 DataArchive\<yyyyMMdd>\<파일명>으로 옮기므로,
# 컷오프가 과거 날짜에 걸치면 그 날짜들의 아카이브도 같이 읽어야 누락이 없다.
function Read-JsonlSince {
    param([string]$FileName, [datetime]$CutoffUtc, [string]$BaseDir, [string]$ArchiveRoot)

    $todayUtc = (Get-Date).ToUniversalTime().Date
    $records = @()

    $d = $CutoffUtc.Date
    while ($d -lt $todayUtc) {
        $archivePath = Join-Path (Join-Path $ArchiveRoot $d.ToString("yyyyMMdd")) $FileName
        if (Test-Path $archivePath) { $records += Read-Jsonl $archivePath }
        $d = $d.AddDays(1)
    }

    $records += Read-Jsonl (Join-Path $BaseDir $FileName)

    # @()로 강제 래핑 - 결과가 정확히 1건일 때 PowerShell이 배열이 아닌 단일 객체를 반환해
    # 이후 .Count가 공백으로 나오는 표시버그(2026-07-19까지 여러 회차에서 관측)를 여기서 방지.
    return @($records | Where-Object {
        $_.Timestamp -and ([datetime]::Parse($_.Timestamp).ToUniversalTime()) -ge $CutoffUtc
    })
}

$archiveRoot = Join-Path $BaseDir "DataArchive"
$stateFile = Join-Path $BaseDir "analysis_state.json"
$nowUtc = (Get-Date).ToUniversalTime()
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$tradePath = Join-Path $BaseDir "trade_results.jsonl"
$skipPath  = Join-Path $BaseDir "skipped_results.jsonl"

# 라이브 trade_results.jsonl/skipped_results.jsonl이 둘 다 없는 것은 "오늘 아직 신규 레코드가
# 없어서 rotate_data_logs.ps1이 만들지 않은" 정상 상태일 수 있다(archive-aware 읽기로 과거
# 데이터는 여전히 조회 가능) - BaseDir 자체가 잘못된 경우만 에러로 취급한다.
if (-not (Test-Path $BaseDir)) {
    Write-Host "ERROR: BaseDir 자체를 찾을 수 없음: $BaseDir"
    exit 1
}

if (-not $sinceHoursExplicit -and (Test-Path $stateFile)) {
    $state = Get-Content $stateFile -Raw | ConvertFrom-Json
    $cutoff = ([datetime]::Parse($state.LastCutoffUtc)).ToUniversalTime()
    Write-Host "상태파일 기반 컷오프 사용(analysis_state.json)"
} else {
    $cutoff = $nowUtc.AddHours(-$SinceHours)
    if ($sinceHoursExplicit) {
        Write-Host "SinceHours가 명시적으로 지정되어 상태파일 대신 이 값을 사용"
    } else {
        Write-Host "상태파일 없음 - SinceHours=$SinceHours 폴백 사용(최초 실행으로 추정)"
    }
}

$trades = Read-JsonlSince -FileName "trade_results.jsonl" -CutoffUtc $cutoff -BaseDir $BaseDir -ArchiveRoot $archiveRoot
$skips = Read-JsonlSince -FileName "skipped_results.jsonl" -CutoffUtc $cutoff -BaseDir $BaseDir -ArchiveRoot $archiveRoot

Write-Host "================================================================"
Write-Host " 분석 구간: cutoff UTC $($cutoff.ToString('yyyy-MM-ddTHH:mm:ssZ')) ~ 현재"
Write-Host " trade_results.jsonl 신규 레코드: $($trades.Count)건"
Write-Host " skipped_results.jsonl 신규 레코드: $($skips.Count)건"
Write-Host "================================================================"

# 다음 사이클이 정확히 "이번 실행 이후"부터 이어받도록, 분석 결과 유무와 무관하게 항상 갱신한다.
$stateJson = (@{ LastCutoffUtc = $nowUtc.ToString("yyyy-MM-ddTHH:mm:ssZ") } | ConvertTo-Json)
[System.IO.File]::WriteAllText($stateFile, $stateJson, $utf8NoBom)

if ($trades.Count -eq 0 -and $skips.Count -eq 0) {
    Write-Host "신규 레코드 없음 - 이번 구간 분석 대상 없음."
    exit 0
}

# ── 1) A/B/C paper 비교 (trade_results.jsonl 기준) ──────────────────
function Show-Stats {
    param([array]$Items, [string]$Field, [string]$Label)
    if ($Items.Count -eq 0) { Write-Host "  $Label`: n=0"; return }
    $vals = $Items | ForEach-Object { $_.$Field } | Where-Object { $_ -ne $null }
    if ($vals.Count -eq 0) { Write-Host "  $Label`: n=0 (전부 null)"; return }
    $avg = ($vals | Measure-Object -Average).Average
    $winCount = ($vals | Where-Object { $_ -gt 0 }).Count
    $winRate = [math]::Round(($winCount / $vals.Count) * 100, 1)
    Write-Host ("  {0}: n={1}, 평균={2:F4}%, 승률={3}% ({4}/{5})" -f $Label, $vals.Count, $avg, $winRate, $winCount, $vals.Count)
}

Write-Host ""
Write-Host "--- A/B/C paper 비교 (trade_results.jsonl, 전체) ---"
Show-Stats $trades "C_ProfitPct" "C(전략, paper)"
Show-Stats $trades "A_ProfitPct_Est" "A_Est"
Show-Stats $trades "B_ProfitPct_Est" "B_Est"

$nonBait = @($trades | Where-Object { -not $_.IsBait })
Write-Host ""
Write-Host "--- IsBait=false(실제 C진입)만 ---"
Show-Stats $nonBait "C_ProfitPct" "C(paper, non-bait)"
Show-Stats $nonBait "Actual_ProfitPct" "C(실측, non-bait)"

Write-Host ""
Write-Host "--- 실측(Actual_ProfitPct) 전체 (bait 포함) ---"
Show-Stats $trades "Actual_ProfitPct" "실측 전체"

# ── 2) 트레일링청산 / MaxHoldMs 타임아웃(peak=0) 분리 ────────────────
Write-Host ""
Write-Host "--- 청산 유형 분리 (peak 기준) ---"
$trailed = @($trades | Where-Object { $_.C_PeakProfitPct -gt 0 })
$timedOut = @($trades | Where-Object { $_.C_PeakProfitPct -eq 0 })
Write-Host "  트레일링청산(peak>0): $($trailed.Count)건"
foreach ($t in $trailed) {
    $giveback = if ($t.C_PeakProfitPct -ne 0) {
        [math]::Round((($t.C_PeakProfitPct - $t.C_ProfitPct) / $t.C_PeakProfitPct) * 100, 1)
    } else { "N/A" }
    Write-Host ("    {0}: peak={1:F4}% final={2:F4}% giveback={3}%" -f $t.Symbol, $t.C_PeakProfitPct, $t.C_ProfitPct, $giveback)
}
Write-Host "  MaxHoldMs 타임아웃(peak=0): $($timedOut.Count)건"
foreach ($t in $timedOut) {
    $actualStr = if ($t.Actual_ProfitPct -ne $null) { "{0:F4}%" -f $t.Actual_ProfitPct } else { "null" }
    Write-Host ("    {0}: paper={1:F4}% 실측={2}" -f $t.Symbol, $t.C_ProfitPct, $actualStr)
}

# ── 3) Trend30Pct 구간별 비교 (trade+skip 합산, null 제외) ───────────
Write-Host ""
Write-Host "--- Trend30Pct 구간별 비교 (trade_results + skipped_results 합산, null 레코드 제외) ---"

$combined = @()
foreach ($t in $trades) {
    if ($t.Trend30Pct -ne $null) {
        $combined += [PSCustomObject]@{
            Symbol   = $t.Symbol
            Source   = "Traded"
            Trend30  = $t.Trend30Pct
            CProfit  = $t.C_ProfitPct
            AEst     = $t.A_ProfitPct_Est
            BEst     = $t.B_ProfitPct_Est
        }
    }
}
foreach ($s in $skips) {
    if ($s.Trend30Pct -ne $null) {
        $combined += [PSCustomObject]@{
            Symbol   = $s.Symbol
            Source   = "Skipped($($s.SkipReason))"
            Trend30  = $s.Trend30Pct
            CProfit  = $s.C_ProfitPct_Sim
            AEst     = $s.A_ProfitPct_Est
            BEst     = $s.B_ProfitPct_Est
        }
    }
}

Write-Host "  Trend30Pct 유효 레코드: $($combined.Count)건 (전체 $($trades.Count + $skips.Count)건 중)"

if ($combined.Count -ge 6) {
    $absSorted = $combined | ForEach-Object { [math]::Abs($_.Trend30) } | Sort-Object
    $lowCut = $absSorted[[math]::Floor($absSorted.Count / 3)]
    $highCut = $absSorted[[math]::Floor($absSorted.Count * 2 / 3)]

    $buckets = @(
        @{ Name = "약한 추세 (|Trend30|<$([math]::Round($lowCut,3)))"; Filter = { [math]::Abs($_.Trend30) -lt $lowCut } }
        @{ Name = "중간 추세"; Filter = { [math]::Abs($_.Trend30) -ge $lowCut -and [math]::Abs($_.Trend30) -lt $highCut } }
        @{ Name = "강한 추세 (|Trend30|>=$([math]::Round($highCut,3)))"; Filter = { [math]::Abs($_.Trend30) -ge $highCut } }
    )
    foreach ($b in $buckets) {
        $sub = $combined | Where-Object $b.Filter
        if ($sub.Count -gt 0) {
            $avgC = ($sub | Measure-Object -Property CProfit -Average).Average
            $winC = [math]::Round((($sub | Where-Object { $_.CProfit -gt 0 }).Count / $sub.Count) * 100, 1)
            $avgA = ($sub | Measure-Object -Property AEst -Average).Average
            $avgB = ($sub | Measure-Object -Property BEst -Average).Average
            Write-Host ("  [{0}] n={1}: C평균={2:F4}%/승률{3}% | A_Est평균={4:F4}% | B_Est평균={5:F4}%" -f $b.Name, $sub.Count, $avgC, $winC, $avgA, $avgB)
        } else {
            Write-Host "  [$($b.Name)] n=0"
        }
    }
} else {
    Write-Host "  표본부족(6건 미만) - 구간 분리 생략, 아래 개별 레코드만 참고:"
    $combined | Sort-Object Trend30 | Format-Table Symbol, Source, Trend30, CProfit, AEst, BEst -AutoSize
}

# ── 4) 베이지안 누적 승률 비교 (Beta-Bernoulli, bayes_state.json에 전체 이력 누적) ──
# 위 3)번 구간은 "이번 사이클 배치"만의 적응형 3분위라 사이클마다 경계가 달라져 누적이
# 안 된다. 여기서는 고정 임계값(추세 10%/30%)을 써서 사후분포가 사이클을 넘어 누적되게
# 하고, 신용구간이 50%를 걸치면 "판단보류"로 명시해 n=1~3짜리 단일 사이클로 가설이
# 뒤집히는 문제를 완화한다.
# ponytail: Beta(a,b)의 정확한 역함수 대신 정규근사로 95% 신용구간을 계산한다 - a,b가
# 아주 작을 때(누적 초반) 근사오차가 있을 수 있으나 "판단보류" 여부를 가르는 용도로는
# 충분하다. 필요해지면 정확한 역베타로 교체.
function New-BetaCounts { [PSCustomObject]@{ Alpha = 1.0; Beta = 1.0 } }
function New-CompareCounts {
    [PSCustomObject]@{ CWin = (New-BetaCounts); CvA = (New-BetaCounts); CvB = (New-BetaCounts) }
}
function Add-BayesOutcome {
    param($Counts, [bool]$Win)
    if ($Win) { $Counts.Alpha += 1 } else { $Counts.Beta += 1 }
}
function Get-BetaVerdict {
    param([double]$Alpha, [double]$Beta)
    $mean = $Alpha / ($Alpha + $Beta)
    $var = ($Alpha * $Beta) / ([math]::Pow($Alpha + $Beta, 2) * ($Alpha + $Beta + 1))
    $sd = [math]::Sqrt($var)
    # [math]::Max(0, ...)처럼 정수 리터럴 0/1을 그대로 넘기면 PowerShell이 Max(Int32,Int32)
    # 오버로드로 잘못 바인딩해 double 인자를 조용히 0으로 잘라버리는 문제가 있어(실측 확인됨),
    # 0.0/1.0으로 명시해 Max(Double,Double)/Min(Double,Double)를 강제한다.
    $lo = [math]::Max(0.0, $mean - 1.96 * $sd)
    $hi = [math]::Min(1.0, $mean + 1.96 * $sd)
    $verdict = if ($lo -gt 0.5) { "우세" } elseif ($hi -lt 0.5) { "열세" } else { "판단보류" }
    return ("{0,5:F1}% [{1,5:F1}~{2,5:F1}%] {3} (a={4:F0} b={5:F0})" -f ($mean * 100), ($lo * 100), ($hi * 100), $verdict, $Alpha, $Beta)
}

$bayesStateFile = Join-Path $BaseDir "bayes_state.json"
if (Test-Path $bayesStateFile) {
    $bayesState = Get-Content $bayesStateFile -Raw | ConvertFrom-Json
} else {
    $bayesState = [PSCustomObject]@{
        Global      = New-CompareCounts
        TrendWeak   = New-CompareCounts
        TrendMid    = New-CompareCounts
        TrendStrong = New-CompareCounts
    }
}

# Global: trade_results.jsonl 기준(1번 섹션과 동일하게 trades만, skip 제외) - 이번 사이클 신규분만 누적
foreach ($t in $trades) {
    Add-BayesOutcome $bayesState.Global.CWin ($t.C_ProfitPct -gt 0)
    if ($null -ne $t.A_ProfitPct_Est) { Add-BayesOutcome $bayesState.Global.CvA ($t.C_ProfitPct -gt $t.A_ProfitPct_Est) }
    if ($null -ne $t.B_ProfitPct_Est) { Add-BayesOutcome $bayesState.Global.CvB ($t.C_ProfitPct -gt $t.B_ProfitPct_Est) }
}

# 추세강도: 3번 섹션과 동일하게 trade+skip 합산($combined) - 단 구간 경계는 고정값
foreach ($c in $combined) {
    $absTrend = [math]::Abs($c.Trend30)
    $bucket = if ($absTrend -lt 0.1) { $bayesState.TrendWeak } elseif ($absTrend -le 0.3) { $bayesState.TrendMid } else { $bayesState.TrendStrong }
    Add-BayesOutcome $bucket.CWin ($c.CProfit -gt 0)
    if ($null -ne $c.AEst) { Add-BayesOutcome $bucket.CvA ($c.CProfit -gt $c.AEst) }
    if ($null -ne $c.BEst) { Add-BayesOutcome $bucket.CvB ($c.CProfit -gt $c.BEst) }
}

[System.IO.File]::WriteAllText($bayesStateFile, ($bayesState | ConvertTo-Json -Depth 5), $utf8NoBom)

Write-Host ""
Write-Host "--- 베이지안 누적 승률 비교 (Beta-Bernoulli, 전체 이력 누적 - 사이클 리셋 없음) ---"
Write-Host ("  [전체]         C승률(>0)  : " + (Get-BetaVerdict $bayesState.Global.CWin.Alpha $bayesState.Global.CWin.Beta))
Write-Host ("  [전체]         C vs A_Est : " + (Get-BetaVerdict $bayesState.Global.CvA.Alpha $bayesState.Global.CvA.Beta))
Write-Host ("  [전체]         C vs B_Est : " + (Get-BetaVerdict $bayesState.Global.CvB.Alpha $bayesState.Global.CvB.Beta))
foreach ($pair in @(@("약한 추세(<10%)", "TrendWeak"), @("중간 추세(10~30%)", "TrendMid"), @("강한 추세(>30%)", "TrendStrong"))) {
    $name = $pair[0]; $b = $bayesState.($pair[1])
    Write-Host ("  [$name] C승률(>0)  : " + (Get-BetaVerdict $b.CWin.Alpha $b.CWin.Beta))
    Write-Host ("  [$name] C vs A_Est : " + (Get-BetaVerdict $b.CvA.Alpha $b.CvA.Beta))
    Write-Host ("  [$name] C vs B_Est : " + (Get-BetaVerdict $b.CvB.Alpha $b.CvB.Beta))
}

Write-Host ""
Write-Host "================================================================"
Write-Host " 분석 종료 - 위 결과를 근거로 CLAUDE.md / automation_summary.log에"
Write-Host " 서술형 요약을 작성할 것. 이 스크립트는 숫자 집계만 담당함."
Write-Host "================================================================"
