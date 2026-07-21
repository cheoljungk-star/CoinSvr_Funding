@echo off
cd /d "D:\000.WORK\000.NET\CoinSvr_Funding"
echo [%date% %time%] ===== 사이클 시작 ===== >> automation_run.log
claude -p "CLAUDE_CODE_TASK.md 파일을 읽고 그 안에 설명된 사이클을 지금 실행해줘." --dangerously-skip-permissions --max-turns 70 --output-format json >> automation_run.log 2>&1
echo [%date% %time%] ===== 사이클 종료 ===== >> automation_run.log
exit /b 0