# run_spike_analysis.ps1
# CoinSvr SpikeScalp 전용 1시간 주기 자동분석 - FundingHedger 6시간 스크립트와 완전 별개.
# ⚠️ $workDir 경로는 실제 배포 경로에 맞게 확인/수정할 것.
$ErrorActionPreference = "Stop"

# 콘솔/파일 출력 인코딩을 UTF-8로 고정 - claude 응답의 한글이 깨지지 않도록.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$workDir = "D:\000.WORK\000.NET\CoinSvr_Funding"
$taskFile = Join-Path $workDir "CLAUDE_CODE_TASK_SPIKE.md"
$logFile  = Join-Path $workDir "spike_analysis_run.log"

Set-Location $workDir

$ts = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

try {
    if (-not (Test-Path $taskFile)) {
        Add-Content -Path $logFile -Value "[$ts] Error: CLAUDE_CODE_TASK_SPIKE.md not found" -Encoding utf8
        exit 1
    }

    Add-Content -Path $logFile -Value "[$ts] SpikeScalp analysis start" -Encoding utf8

    $prompt = Get-Content -Path $taskFile -Raw -Encoding utf8
    # ⚠️ FundingHedger 기존 6시간 스크립트와 동일한 claude 호출 플래그를 맞춰야 함.
    # config 파일 쓰기가 세션 내에서 막힌다면(권한 프롬프트가 비대화형에서 자동승인 안 되는 경우),
    # 기존 스크립트에 --dangerously-skip-permissions 같은 플래그가 있는지 확인해서 동일하게 추가할 것.
    $output = claude -p "$prompt" 2>&1

    $output | Add-Content -Path $logFile -Encoding utf8
    Add-Content -Path $logFile -Value "[$ts] SpikeScalp analysis done" -Encoding utf8
}
catch {
    Add-Content -Path $logFile -Value "[$ts] Error: $($_.Exception.Message)" -Encoding utf8
}
