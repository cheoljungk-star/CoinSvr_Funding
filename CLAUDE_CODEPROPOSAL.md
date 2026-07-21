# CoinSvr 코드수정 제안 자동화 — 세션 간 연속성 로그

이 파일은 `CLAUDE_CODE_TASK_CODEPROPOSAL.md`에 정의된 6시간 자동 사이클이 세션 간 맥락을
이어가기 위해 누적 기록하는 파일이다. 매 사이클 종료 시 하단에 새 항목을 append하며, 기존
내용은 지우지 않는다.

관련 파일:
- 제안서 저장 위치: `code_proposals/*.md` (프로젝트 루트, `Status: PENDING_REVIEW`로 생성되고
  사람이 검토 후 `REVIEWED_APPLIED`/`REVIEWED_REJECTED`로 직접 갱신)
- 사이클별 한줄요약: `code_proposal_automation_summary.log` (프로젝트 루트)
- 이 자동화가 참고하는 근거 원본: `CLAUDE.md`(FundingHedger), `CLAUDE_SPIKE.md`(SpikeScalp) —
  읽기 전용, 이 작업에서 쓰지 않음.

이 자동화는 **`.cs` 파일을 절대 직접 수정하지 않는다** — config에 없는 하드코딩 상수 또는
분석용 추가 로그 필드에 한해, 실제 반영은 사람이 대화 세션에서 검토·승인한 뒤에만 이뤄지는
제안서만 생성한다. 상세 규칙은 `CLAUDE_CODE_TASK_CODEPROPOSAL.md` 참고.

(첫 사이클 실행 시 아래에 "## 코드제안 사이클 로그 [...]" 항목이 추가된다.)
