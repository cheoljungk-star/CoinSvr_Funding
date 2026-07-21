using MySqlConnector;   // NuGet: MySqlConnector
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CoinSvr
{
    /// <summary>
    /// Async MySQL helper (MySqlConnector).
    /// - 매 호출마다 연결 열고/닫기(풀링 사용)
    /// - transient 오류(서버 끊김 등) 1회 재시도
    /// - 기존 메서드 시그니처 유지 (동시 호출 안전)
    /// </summary>
    public class DB_MYSQL : IDisposable, IAsyncDisposable
    {
        private bool _disposed;
        private readonly string _connectionString;
        private readonly SemaphoreSlim _dbSemaphore = new SemaphoreSlim(50);

        public DB_MYSQL(string connectionString)
        {
            _connectionString = connectionString;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        ~DB_MYSQL()
        {
            Dispose();
        }

        // ---- 내부 공통 유틸 ----

        private static bool IsTransient(MySqlException ex)
        {
            // 서버 끊김/로스트
            if (ex.Number is 2006 or 2013 or 2055) return true;

            // 읽기/쓰기 중 네트워크 오류를 MySqlException이 감싼 케이스
            if (ex.InnerException is IOException) return true;
            if (ex.InnerException is System.Net.Sockets.SocketException) return true;

            // 메시지 기반(라이브러리 버전별 문구 상이)
            var msg = ex.Message ?? "";
            if (msg.Contains("Failed to read the result set", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("Reading from the stream has failed", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("fatal error encountered during command execution", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        private static bool IsHostBlocked(MySqlException ex) => ex.Number == 1129; // ER_HOST_IS_BLOCKED
        private static bool IsTooManyConnections(MySqlException ex) => ex.Number == 1040; // ER_CON_COUNT_ERROR


        // ---- 기존 메서드 ----

        /// <summary>
        /// 단일 Non-Query 실행 (성공 시 영향 행수 > 0 이면 true)
        /// </summary>
        public async Task<bool> ExecuteQueryAsync(string query)
        {
            try
            {
                int ret = await WithConnectionAsync(async conn =>
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandType = CommandType.Text;
                    cmd.CommandText = query;
                    cmd.CommandTimeout = 30; // 필요 시 조정
                    return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);

                return ret > 0;
            }
            catch (Exception ex)
            {
                Ob.app._ERROR($"DB_ExecuteQueryAsync <{query}>", ex);
                return false;
            }
        }

        /// <summary>
        /// 여러 Non-Query 순차 실행(트랜잭션 없이), 총 영향 행수 반환
        /// </summary>
        public async Task<int> ExecuteBatchAsync(IEnumerable<string> queries)
        {
            try
            {
                // 빈 쿼리 체크
                var queryList = queries.ToList();
                if (queryList.Count == 0) return 0;

                return await WithConnectionAsync(async conn =>
                {
                    // MySQL/MariaDB는 세미콜론으로 구분된 다중 쿼리 지원
                    // PostgreSQL도 지원
                    string batchQuery = string.Join(";", queryList);

                    await using var cmd = conn.CreateCommand();
                    cmd.CommandType = CommandType.Text;
                    cmd.CommandText = batchQuery;
                    cmd.CommandTimeout = 60;

                    return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("DB_ExecuteBatchAsync", ex);
                return -1;
            }
        }
        public async Task<HedgeGridPositionRow?> HedgeGrid_SelectPositionAsync(long id)
        {
            // 1. SQL 쿼리 작성 (전체 컬럼 조회)
            string sql = $@"select 
            *
            FROM hedge_grid_position_1
            WHERE id = {id};";

            try
            {
                // 2. 쿼리 실행 (기존 SelectQueryAsync 활용)
                var dt = await SelectQueryAsync(sql).ConfigureAwait(false);

                // 결과가 없으면 null 반환
                if (dt == null || dt.Rows.Count == 0) return null;

                // 3. 첫 번째 행 추출 및 매핑
                System.Data.DataRow r = dt.Rows[0];

                return new HedgeGridPositionRow
                {
                    id = Convert.ToInt64(r["id"]),
                    symbol = Convert.ToString(r["symbol"])!,
                    initial_budget = Convert.ToDecimal(r["initial_budget"]),
                    entry_time = (DateTime)r["entry_time"],
                    entry_time_utc = (DateTime)r["entry_time_utc"],

                    long_total_qty = Convert.ToDecimal(r["long_total_qty"]),
                    long_avg_price = r["long_avg_price"] == DBNull.Value ? null : (decimal?)Convert.ToDecimal(r["long_avg_price"]),
                    long_total_cost = Convert.ToDecimal(r["long_total_cost"]),

                    short_total_qty = Convert.ToDecimal(r["short_total_qty"]),
                    short_avg_price = r["short_avg_price"] == DBNull.Value ? null : (decimal?)Convert.ToDecimal(r["short_avg_price"]),
                    short_total_cost = Convert.ToDecimal(r["short_total_cost"]),

                    total_trades = Convert.ToInt32(r["total_trades"]),
                    // ✅ 실시간 동기화의 핵심 필드
                    CfgMaxTrade = Convert.ToInt32(r["cfg_max_trade"]),
                    max_notional_cap = r["max_notional_cap"] == DBNull.Value ? 50m : (decimal?)Convert.ToDecimal(r["max_notional_cap"]),

                    status = Convert.ToString(r["status"])!,
                    max_budget_used = r["max_budget_used"] == DBNull.Value ? null : (decimal?)Convert.ToDecimal(r["max_budget_used"]),
                    max_recorded_pnl = r["max_recorded_pnl"] == DBNull.Value ? null : (decimal?)Convert.ToDecimal(r["max_recorded_pnl"]),
                    recovery_base_price = r["recovery_base_price"] == DBNull.Value ? null : (decimal?)Convert.ToDecimal(r["recovery_base_price"]),
                    recovery_side = Convert.ToString(r["recovery_side"]) ?? "NONE",
                };
            }
            catch (Exception ex)
            {
                // 로그 출력 (UI 메서드나 Console 활용)
                Ob.app._ERROR($"❌ [DB-ERR] HedgeGrid_SelectPositionAsync: {ex.Message}", ex);
                return null;
            }
        }
        private string SqlEnc(string? val)
        {
            if (val == null) return "NULL";
            // 작은따옴표를 두 개('')로 치환하여 SQL 오류 및 인젝션 방어
            return $"'{val.Replace("'", "''")}'";
        }
        public async Task BulkUpdateHedgePositionsAsync(List<HedgeGridPositionRow> items)
        {
            if (items == null || items.Count == 0) return;

            try
            {
                // 1. WithConnectionAsync를 통해 세마포어와 커넥션 풀링 활용
                await WithConnectionAsync<int>(async conn =>
                {
                    // 2. 임시 테이블 생성 (메인 테이블의 구조만 복사, 세션 종료 시 자동 삭제됨)
                    // MEMORY 엔진을 사용하여 속도를 극대화합니다.
                    const string createTempSql = "CREATE TEMPORARY TABLE temp_hedge_grid_1 ENGINE=MEMORY AS SELECT * FROM hedge_grid_position_1 WHERE 1=0;";
                    await using (var createCmd = new MySqlCommand(createTempSql, conn))
                    {
                        await createCmd.ExecuteNonQueryAsync();
                    }

                    // 3. 임시 테이블에 데이터 대량 삽입 (StringBuilder 조립)
                    var sb = new StringBuilder();
                    sb.AppendLine(@"INSERT INTO temp_hedge_grid_1 (
                id, symbol, version, 
                long_total_qty, long_avg_price, long_total_cost, long_position_count,
                short_total_qty, short_avg_price, short_total_cost, short_position_count,
                current_price, total_pnl_usdt, total_pnl_pct, max_budget_used,
                long_pnl_usdt, short_pnl_usdt, total_trades, max_recorded_pnl,
                long_pnl_percent, short_pnl_percent, pnl_gap, recovery_base_price, recovery_side,
                pending_recovery_usd, last_tp_price, pending_target_side, update_dt
            ) VALUES");

                    for (int i = 0; i < items.Count; i++)
                    {
                        var it = items[i];
                        sb.Append($"({it.id}, {SqlEnc(it.symbol)}, {it.version}, ");
                        sb.Append($"{it.long_total_qty}, {it.long_avg_price ?? 0}, {it.long_total_cost}, {it.long_position_count}, ");
                        sb.Append($"{it.short_total_qty}, {it.short_avg_price ?? 0}, {it.short_total_cost}, {it.short_position_count}, ");
                        sb.Append($"{it.current_price ?? 0}, {it.total_pnl_usdt ?? 0}, {it.total_pnl_pct ?? 0}, {it.max_budget_used ?? 0}, ");
                        sb.Append($"{it.long_pnl_usdt}, {it.short_pnl_usdt}, {it.total_trades}, {it.max_recorded_pnl ?? 0}, ");
                        sb.Append($"{it.long_pnl_percent}, {it.short_pnl_percent}, {it.pnl_gap}, ");
                        sb.Append($"{it.recovery_base_price ?? 0}, {SqlEnc(it.recovery_side)}, ");
                        sb.Append($"{it.pending_recovery_usd}, {it.last_tp_price ?? 0}, {SqlEnc(it.pending_target_side)}, NOW())");
                        sb.AppendLine(i == items.Count - 1 ? ";" : ",");
                    }

                    await using (var insertCmd = new MySqlCommand(sb.ToString(), conn))
                    {
                        await insertCmd.ExecuteNonQueryAsync();
                    }

                    // 4. [핵심] 임시 테이블에서 메인 테이블로 머지 (ON DUPLICATE KEY UPDATE 활용)
                    const string mergeSql = @"
                INSERT INTO hedge_grid_position_1 
                SELECT * FROM temp_hedge_grid_1
                ON DUPLICATE KEY UPDATE 
                    version = VALUES(version),
                    long_total_qty = VALUES(long_total_qty),
                    long_avg_price = VALUES(long_avg_price),
                    long_total_cost = VALUES(long_total_cost),
                    long_position_count = VALUES(long_position_count),
                    short_total_qty = VALUES(short_total_qty),
                    short_avg_price = VALUES(short_avg_price),
                    short_total_cost = VALUES(short_total_cost),
                    short_position_count = VALUES(short_position_count),
                    current_price = VALUES(current_price),
                    total_pnl_usdt = VALUES(total_pnl_usdt),
                    total_pnl_pct = VALUES(total_pnl_pct),
                    max_budget_used = VALUES(max_budget_used),
                    long_pnl_usdt = VALUES(long_pnl_usdt),
                    short_pnl_usdt = VALUES(short_pnl_usdt),
                    total_trades = VALUES(total_trades),
                    max_recorded_pnl = VALUES(max_recorded_pnl),
                    long_pnl_percent = VALUES(long_pnl_percent),
                    short_pnl_percent = VALUES(short_pnl_percent),
                    pnl_gap = VALUES(pnl_gap),
                    recovery_base_price = VALUES(recovery_base_price),
                    recovery_side = VALUES(recovery_side),
                    pending_recovery_usd = VALUES(pending_recovery_usd),
                    last_tp_price = VALUES(last_tp_price),
                    pending_target_side = VALUES(pending_target_side),
                    update_dt = NOW();";

                    await using (var mergeCmd = new MySqlCommand(mergeSql, conn))
                    {
                        await mergeCmd.ExecuteNonQueryAsync();
                    }

                    // 5. 임시 테이블 명시적 삭제 (권장)
                    await using (var dropCmd = new MySqlCommand("DROP TEMPORARY TABLE IF EXISTS temp_hedge_grid_1;", conn))
                    {
                        await dropCmd.ExecuteNonQueryAsync();
                    }

                    return 1;
                });
            }
            catch (Exception ex)
            {
                Ob.app._ERROR($"[V2-DB-BULK-ERROR] {ex.Message}", ex);
            }
        }
        public async Task<int> BulkInsertFilterLogsAsync(List<object> logs)
        {
            if (logs == null || logs.Count == 0) return 0;

            try
            {
                // 1. WithConnectionAsync를 호출하여 기존 커넥션 관리 로직 활용
                return await WithConnectionAsync(async conn =>
                {
                    await using var cmd = conn.CreateCommand();
                    var sql = new StringBuilder();
                    sql.Append(@"INSERT INTO hedge_grid_filter_log 
                         (position_id, symbol, side, price, filter_type, blocked_at, reason, rsi, adx) 
                         VALUES ");

                    var valuesList = new List<string>();

                    // 2. 리스트를 돌며 벌크용 파라미터와 SQL 구문 조립
                    for (int i = 0; i < logs.Count; i++)
                    {
                        dynamic p = logs[i];

                        // 행별 파라미터 이름 생성 (@pid0, @pid1...)
                        valuesList.Add($@"(@pid{i}, @sym{i}, @side{i}, @prc{i}, @type{i}, NOW(), @rsn{i}, @rsi{i}, @adx{i})");

                        // 리플렉션 대신 직접 매핑 (성능상 훨씬 유리)
                        cmd.Parameters.AddWithValue($"@pid{i}", p.pid);
                        cmd.Parameters.AddWithValue($"@sym{i}", p.sym);
                        cmd.Parameters.AddWithValue($"@side{i}", p.side);
                        cmd.Parameters.AddWithValue($"@prc{i}", p.prc);
                        cmd.Parameters.AddWithValue($"@type{i}", p.type);
                        cmd.Parameters.AddWithValue($"@rsn{i}", p.rsn);
                        cmd.Parameters.AddWithValue($"@rsi{i}", p.rsi);
                        cmd.Parameters.AddWithValue($"@adx{i}", p.adx);
                    }

                    // 3. 최종 SQL 조립
                    sql.Append(string.Join(", ", valuesList));
                    cmd.CommandText = sql.ToString();
                    cmd.CommandType = CommandType.Text;

                    // 4. 비동기 실행
                    return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Ob.app._ERROR($"DB_BulkInsertFilterLogs <{logs.Count} items>", ex);
                return -1;
            }
        }
        public async Task<List<HedgeGridPositionRow>> HedgeGrid_SelectOpenAsyncV1()
        {
            const string sql = @"select * from abuy2way where status = 0 ORDER BY bDate DESC, bTime DESC;";

            var list = new List<HedgeGridPositionRow>();
            // 이미 세마포어가 적용된 SelectQueryAsync를 사용하므로 안전함
            var dt = await SelectQueryAsync(sql).ConfigureAwait(false);

            if (dt == null) return list;
            long Id = 900000000;
            foreach (DataRow r in dt.Rows)
            {
                list.Add(new HedgeGridPositionRow
                {
                    id = Id,
                    symbol = Convert.ToString(r["BCoin"]) ?? "",
                    status = Convert.ToString(r["Status"]) ?? "",
                    initial_budget = Convert.ToDecimal("100"),
                    entry_time_utc = (DateTime)r["StartDt"],
                    entry_time = (DateTime)r["StartDt"],

                    long_total_qty = Convert.ToDecimal(r["LongQty"]),
                    long_avg_price = r["LongBaseMoney"] == DBNull.Value ? 0m : Convert.ToDecimal(r["LongBaseMoney"]),
                    long_total_cost = Convert.ToDecimal(r["LongInvestMoney"]),
                    long_position_count = Convert.ToInt32("0"),
                    long_pnl_usdt = r["LongPnl"] == DBNull.Value ? 0m : Convert.ToDecimal(r["LongPnl"]),

                    short_total_qty = Convert.ToDecimal(r["ShortQty"]),
                    short_avg_price = r["ShortBaseMoney"] == DBNull.Value ? 0m : Convert.ToDecimal(r["ShortBaseMoney"]),
                    short_total_cost = Convert.ToDecimal(r["ShortInvestMoney"]),
                    short_position_count = Convert.ToInt32("0"),
                    short_pnl_usdt = r["ShortPnl"] == DBNull.Value ? 0m : Convert.ToDecimal(r["ShortPnl"]),

                    current_price = r["CurrentMoney"] == DBNull.Value ? 0m : Convert.ToDecimal(r["CurrentMoney"]),
                    total_pnl_usdt = 0,
                    total_pnl_pct = 0,

                    max_budget_used = 0,
                    recovery_base_price = 0,
                    recovery_side = "NONE",
                    total_trades = 0,
                    CfgMaxTrade = 0,

                    pending_recovery_usd = 0,
                    last_tp_price = 0,
                    pending_target_side = "NONE",
                    max_recorded_pnl = 0,
                    version = 1,
                    max_notional_cap = 0,
                });
                Id++;
            }

            return list;
        }
        public async Task<List<HedgeGridPositionRow>> HedgeGrid_SelectOpenAsync()
        {
            const string sql = @"
SELECT 
    *
FROM hedge_grid_position_1
WHERE status = 'OPEN'
ORDER BY entry_time_utc ASC;";

            var list = new List<HedgeGridPositionRow>();
            // 이미 세마포어가 적용된 SelectQueryAsync를 사용하므로 안전함
            var dt = await SelectQueryAsync(sql).ConfigureAwait(false);

            if (dt == null) return list;

            foreach (DataRow r in dt.Rows)
            {
                list.Add(new HedgeGridPositionRow
                {
                    id = Convert.ToInt64(r["id"]),
                    symbol = Convert.ToString(r["symbol"]) ?? "",
                    status = Convert.ToString(r["status"]) ?? "",
                    initial_budget = Convert.ToDecimal(r["initial_budget"]),
                    entry_time_utc = (DateTime)r["entry_time_utc"],
                    entry_time = (DateTime)r["entry_time"],

                    long_total_qty = Convert.ToDecimal(r["long_total_qty"]),
                    long_avg_price = r["long_avg_price"] == DBNull.Value ? 0m : Convert.ToDecimal(r["long_avg_price"]),
                    long_total_cost = Convert.ToDecimal(r["long_total_cost"]),
                    long_position_count = Convert.ToInt32(r["long_position_count"]),
                    long_pnl_usdt = r["long_pnl_usdt"] == DBNull.Value ? 0m : Convert.ToDecimal(r["long_pnl_usdt"]),

                    short_total_qty = Convert.ToDecimal(r["short_total_qty"]),
                    short_avg_price = r["short_avg_price"] == DBNull.Value ? 0m : Convert.ToDecimal(r["short_avg_price"]),
                    short_total_cost = Convert.ToDecimal(r["short_total_cost"]),
                    short_position_count = Convert.ToInt32(r["short_position_count"]),
                    short_pnl_usdt = r["short_pnl_usdt"] == DBNull.Value ? 0m : Convert.ToDecimal(r["short_pnl_usdt"]),

                    current_price = r["current_price"] == DBNull.Value ? 0m : Convert.ToDecimal(r["current_price"]),
                    total_pnl_usdt = r["total_pnl_usdt"] == DBNull.Value ? 0m : Convert.ToDecimal(r["total_pnl_usdt"]),
                    total_pnl_pct = r["total_pnl_pct"] == DBNull.Value ? 0m : Convert.ToDecimal(r["total_pnl_pct"]),

                    max_budget_used = r["max_budget_used"] == DBNull.Value ? 0m : Convert.ToDecimal(r["max_budget_used"]),
                    recovery_base_price = r["recovery_base_price"] == DBNull.Value ? 0m : Convert.ToDecimal(r["recovery_base_price"]),
                    recovery_side = Convert.ToString(r["recovery_side"]) ?? "NONE",
                    total_trades = Convert.ToInt32(r["total_trades"]),
                    CfgMaxTrade = Convert.ToInt32(r["cfg_max_trade"]),

                    pending_recovery_usd = r["pending_recovery_usd"] == DBNull.Value ? 0m : Convert.ToDecimal(r["pending_recovery_usd"]),
                    last_tp_price = (r["last_tp_price"] == DBNull.Value || Convert.ToDecimal(r["last_tp_price"]) == 0) ? (Convert.ToDecimal(r["long_total_qty"]) >= Convert.ToDecimal(r["short_total_qty"]) ? (r["long_avg_price"] == DBNull.Value ? 0m : Convert.ToDecimal(r["long_avg_price"])) : (r["short_avg_price"] == DBNull.Value ? 0m : Convert.ToDecimal(r["short_avg_price"]))) : Convert.ToDecimal(r["last_tp_price"]),
                    pending_target_side = r["pending_target_side"] == DBNull.Value ? "NONE" : Convert.ToString(r["pending_target_side"]) ?? "NONE",
                    max_recorded_pnl = r.Table.Columns.Contains("max_recorded_pnl") && r["max_recorded_pnl"] != DBNull.Value ? Convert.ToDecimal(r["max_recorded_pnl"]) : 0m,
                    version = r["version"] == DBNull.Value ? 1 : Convert.ToInt32(r["version"]),
                    max_notional_cap = r.Table.Columns.Contains("max_notional_cap") && r["max_notional_cap"] != DBNull.Value ? Convert.ToDecimal(r["max_notional_cap"]) : 50m,
                });
            }

            return list;
        }
        public async Task<List<HedgeGridTradeRow>> HedgeGrid_SelectTradesAsync(long positionId)
        {
            string sql = @"
SELECT id, position_id, trade_time_utc, trade_time, side, qty, price, cost, reason
FROM hedge_grid_trades_1
WHERE position_id = " + positionId + @" 
ORDER BY trade_time_utc ASC;";

            var list = new List<HedgeGridTradeRow>();
            var dt = await SelectQueryAsync(sql).ConfigureAwait(false);

            if (dt == null) return list;

            foreach (DataRow r in dt.Rows)
            {
                list.Add(new HedgeGridTradeRow
                {
                    id = Convert.ToInt64(r["id"]),
                    position_id = Convert.ToInt64(r["position_id"]),
                    trade_time = r["trade_time"] == DBNull.Value ? DateTime.Now : (DateTime)r["trade_time"],
                    trade_time_utc = r["trade_time_utc"] == DBNull.Value ? DateTime.Now : (DateTime)r["trade_time_utc"],
                    side = Convert.ToString(r["side"]) ?? "",
                    qty = r["qty"] == DBNull.Value ? 0m : Convert.ToDecimal(r["qty"]),
                    price = r["price"] == DBNull.Value ? 0m : Convert.ToDecimal(r["price"]),
                    cost = r["cost"] == DBNull.Value ? 0m : Convert.ToDecimal(r["cost"]),
                    reason = Convert.ToString(r["reason"]) ?? ""
                });
            }
            return list;
        }

        /// <summary>
        /// Select 결과를 DataTable로 반환
        /// </summary>
        public async Task<DataTable> SelectQueryAsync(string query)
        {
            try
            {
                return await WithConnectionAsync(async conn =>
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandType = CommandType.Text;
                    cmd.CommandText = query;
                    cmd.CommandTimeout = 30;

                    await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                    var table = new DataTable();
                    table.Load(reader);
                    return table;
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Ob.app._ERROR($"DB_SelectQueryAsync <{query}>", ex);
                return null;
            }
        }
        private async Task<T> WithConnectionAsync<T>(Func<MySqlConnection, Task<T>> work)
        {
            // 🛑 [수정] 작업 시작 전 세마포어 대기 (입장권 확인)
            await _dbSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                const int maxAttempts = 2;
                Exception last = null;

                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    // using을 사용하여 연결이 끝나면 즉시 반환
                    await using var conn = new MySqlConnection(_connectionString);

                    try
                    {
                        await conn.OpenAsync().ConfigureAwait(false);
                        return await work(conn).ConfigureAwait(false);
                    }
                    catch (MySqlException ex) when (IsHostBlocked(ex))
                    {
                        throw; // 호스트 차단은 재시도 금지
                    }
                    catch (MySqlException ex) when (IsTooManyConnections(ex))
                    {
                        // 과부하 완화: 풀 비우고 백오프 후 1회만 재시도
                        MySqlConnection.ClearAllPools();
                        if (attempt < maxAttempts)
                        {
                            Ob.app._ERROR("[DB retry] Too many connections - Retrying...", ex);
                            await Task.Delay(1000 * attempt).ConfigureAwait(false);
                            continue;
                        }
                        throw;
                    }
                    catch (MySqlException ex) when (IsTransient(ex) && attempt < maxAttempts)
                    {
                        // 네트워크/읽기 오류: 풀 비우고 백오프
                        MySqlConnection.ClearAllPools();
                        Ob.app._ERROR($"[DB retry] transient #{ex.Number} state={ex.SqlState}: {ex.Message}", ex);
                        await Task.Delay(700 * attempt).ConfigureAwait(false);
                        continue;
                    }
                    catch (IOException ex) when (attempt < maxAttempts)
                    {
                        MySqlConnection.ClearAllPools();
                        Ob.app._ERROR("[DB retry] IO error", ex);
                        await Task.Delay(700 * attempt).ConfigureAwait(false);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        // 그 외 에러 기록용
                        last = ex;
                        throw;
                    }
                }
                throw last ?? new Exception("DB operation failed without exception.");
            }
            finally
            {
                // 🛑 [수정] 작업 완료 후 반드시 세마포어 반납 (다음 대기자 입장)
                _dbSemaphore.Release();
            }
        }

        /// <summary>
        /// 풀 비우기(옵션). per-call open/close라 실사용에선 보통 불필요.
        /// </summary>
        public Task CloseAsync()
        {
            try
            {
                MySqlConnection.ClearAllPools();
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("DB_CloseAsync", ex);
            }
            return Task.CompletedTask;
        }

        public async Task<long> HedgeGrid_InsertAsync(HedgeGridPositionRow row)
        {
            const string sql = @"
                        INSERT INTO hedge_grid_position_1 
                        (
                            symbol, initial_budget, entry_time_utc, entry_time, status, version,
                            long_total_qty, long_avg_price, long_total_cost, long_position_count, long_pnl_usdt,
                            short_total_qty, short_avg_price, short_total_cost, short_position_count, short_pnl_usdt,
                            current_price, total_pnl_usdt, total_pnl_pct, 
                            max_budget_used, total_trades,
                            cfg_max_trade,
                            recovery_base_price, recovery_side
                        )
                        VALUES 
                        (
                            @symbol, @budget, @time_utc, @time, @status, @version,
                            @long_qty, @long_avg, @long_cost, @long_count, @long_pnl,
                            @short_qty, @short_avg, @short_cost, @short_count, @short_pnl,
                            @price, @pnl, @pnl_pct,
                            @max_budget, @trades,
                            @cfg_max_trade,
                            @recovery_base_price, @recovery_side
                        );
                        SELECT LAST_INSERT_ID();";

            return await WithConnectionAsync(async conn =>
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                cmd.Parameters.AddWithValue("@symbol", row.symbol);
                cmd.Parameters.AddWithValue("@budget", row.initial_budget);
                cmd.Parameters.AddWithValue("@time_utc", row.entry_time);
                cmd.Parameters.AddWithValue("@time", row.entry_time);
                cmd.Parameters.AddWithValue("@status", row.status);

                // [추가] 버전 정보 매핑 (V1: 1, V2: 2)
                cmd.Parameters.AddWithValue("@version", row.version);

                cmd.Parameters.AddWithValue("@long_qty", row.long_total_qty);
                cmd.Parameters.AddWithValue("@long_avg", (object?)row.long_avg_price ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@long_cost", row.long_total_cost);
                cmd.Parameters.AddWithValue("@long_count", row.long_position_count);

                cmd.Parameters.AddWithValue("@short_qty", row.short_total_qty);
                cmd.Parameters.AddWithValue("@short_avg", (object?)row.short_avg_price ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@short_cost", row.short_total_cost);
                cmd.Parameters.AddWithValue("@short_count", row.short_position_count);

                cmd.Parameters.AddWithValue("@long_pnl", (object?)row.long_pnl_usdt ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@short_pnl", (object?)row.short_pnl_usdt ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@price", (object?)row.current_price ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@pnl", (object?)row.total_pnl_usdt ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@pnl_pct", (object?)row.total_pnl_pct ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@max_budget", (object?)row.max_budget_used ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@trades", row.total_trades);

                cmd.Parameters.AddWithValue("@cfg_max_trade", row.CfgMaxTrade);
                cmd.Parameters.AddWithValue("@recovery_base_price", (object?)row.recovery_base_price ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@recovery_side", (object?)row.recovery_side ?? "NONE");

                var id = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                return Convert.ToInt64(id);
            }).ConfigureAwait(false);
        }
        public async Task<int> HedgeGrid_AddTradeAsync(HedgeGridTradeRow row)
        {
            // 🛡️ [수정] trade_type 컬럼 추가
            const string sql = @"
            INSERT INTO hedge_grid_trades_1 (
                position_id, trade_time_utc, trade_time, side, trade_type, qty, price, cost, reason
            ) VALUES (
                @position_id, @trade_time_utc, @trade_time, @side, @trade_type, @qty, @price, @cost, @reason
            )";

            // trade_time이 누락된 경우를 대비한 방어 로직 (선택 사항)
            if (row.trade_time == DateTime.MinValue) row.trade_time = DateTime.Now;
            if (row.trade_time_utc == DateTime.MinValue) row.trade_time_utc = DateTime.Now;
            if (string.IsNullOrEmpty(row.trade_type)) row.trade_type = "NORMAL";

            // 🚀 ExecuteAsync 내부에서 reflection으로 row의 프로퍼티를 @파라미터에 매핑합니다.
            return await ExecuteAsync(sql, row);
        }
        public async Task<int> ExecuteAsync(string sql, object param = null)
        {
            try
            {
                int ret = await WithConnectionAsync(async conn =>
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.CommandType = CommandType.Text;

                    if (param != null)
                    {
                        foreach (var prop in param.GetType().GetProperties())
                        {
                            var name = "@" + prop.Name;
                            var value = prop.GetValue(param);
                            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
                        }
                    }

                    return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);

                return ret;
            }
            catch (Exception ex)
            {
                Ob.app._ERROR($"DB_ExecuteAsync <{sql}>", ex);
                return -1;
            }
        }

        public async Task<bool> HedgeGrid_UpdateAsync(
      long positionId,
      decimal longQty, decimal longAvg, decimal longCost, int longCount,
      decimal shortQty, decimal shortAvg, decimal shortCost, int shortCount,
      decimal currentPrice, decimal pnl, decimal pnlPct, decimal maxBudget,
      decimal longPnl, decimal shortPnl, decimal totalTrades,
      decimal maxRecordedPnl,
      // [NEW] 새로 추가된 파라미터들
      decimal longPnlPercent, decimal shortPnlPercent, decimal pnlGap)
        {
            // [NEW] SQL 쿼리에 컬럼 3개 추가 (long_pnl_percent, short_pnl_percent, pnl_gap)
            const string sql = @"UPDATE hedge_grid_position_1 SET
        long_total_qty = @lqty,
        long_avg_price = @lavg,
        long_total_cost = @lcost,
        long_position_count = @lcount,
        long_pnl_usdt = @lpnl,
        long_pnl_percent = @lpnl_pct,      
        short_total_qty = @sqty,
        short_avg_price = @savg,
        short_total_cost = @scost,
        short_position_count = @scount,
        short_pnl_usdt = @spnl,
        short_pnl_percent = @spnl_pct,     
        current_price = @price,
        total_pnl_usdt = @pnl,
        total_pnl_pct = @pnlpct,
        pnl_gap = @gap_pct,        
        max_budget_used = @maxbudget,
        total_trades = @total_trades,
        update_dt = now(),
        max_recorded_pnl = @max_rec_pnl
    WHERE id = @id";

            try
            {
                int ret = await WithConnectionAsync(async conn =>
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;

                    // 기존 파라미터들
                    cmd.Parameters.AddWithValue("@id", positionId);
                    cmd.Parameters.AddWithValue("@lqty", longQty);
                    cmd.Parameters.AddWithValue("@lavg", longAvg);
                    cmd.Parameters.AddWithValue("@lcost", longCost);
                    cmd.Parameters.AddWithValue("@lcount", longCount);
                    cmd.Parameters.AddWithValue("@sqty", shortQty);
                    cmd.Parameters.AddWithValue("@savg", shortAvg);
                    cmd.Parameters.AddWithValue("@scost", shortCost);
                    cmd.Parameters.AddWithValue("@scount", shortCount);
                    cmd.Parameters.AddWithValue("@lpnl", longPnl);
                    cmd.Parameters.AddWithValue("@spnl", shortPnl);
                    cmd.Parameters.AddWithValue("@price", currentPrice);
                    cmd.Parameters.AddWithValue("@pnl", pnl);
                    cmd.Parameters.AddWithValue("@pnlpct", pnlPct);
                    cmd.Parameters.AddWithValue("@maxbudget", maxBudget);
                    cmd.Parameters.AddWithValue("@total_trades", totalTrades);
                    cmd.Parameters.AddWithValue("@max_rec_pnl", maxRecordedPnl);

                    // [NEW] 신규 파라미터 추가
                    cmd.Parameters.AddWithValue("@lpnl_pct", longPnlPercent);
                    cmd.Parameters.AddWithValue("@spnl_pct", shortPnlPercent);
                    cmd.Parameters.AddWithValue("@gap_pct", pnlGap);

                    return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);

                return ret > 0;
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("HedgeGrid_UpdateAsync", ex);
                return false;
            }
        }

        public async Task<bool> HedgeGrid_CloseAsync(
            long positionId, decimal exitPrice, DateTime exitTime,
            string reason, decimal pnl, decimal pnlPct)
        {
            const string sql = @"
UPDATE hedge_grid_position_1
SET 
    status = 'CLOSED',
    current_price = @price,
    total_pnl_usdt = @pnl,
    total_pnl_pct = @pnl_pct,
    exit_time_utc = @exit_utc,
    exit_time = @exit_time,
    exit_reason = @reason
WHERE id = @id";

            try
            {
                int ret = await WithConnectionAsync(async conn =>
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.Parameters.AddWithValue("@id", positionId);
                    cmd.Parameters.AddWithValue("@exit_utc", exitTime);
                    cmd.Parameters.AddWithValue("@exit_time", DateTime.Now);
                    cmd.Parameters.AddWithValue("@reason", reason);
                    cmd.Parameters.AddWithValue("@price", exitPrice);
                    cmd.Parameters.AddWithValue("@pnl", pnl);
                    cmd.Parameters.AddWithValue("@pnl_pct", pnlPct);

                    return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);

                return ret > 0;
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("HedgeGrid_CloseAsync", ex);
                return false;
            }
        }

        public async Task<bool> ExecPosition_UpdateRealtimeAsync(
            long positionId, decimal? mfePrice, bool? beArmed, decimal? bePrice, decimal? currentStopPrice)
        {
            const string sql = @"
UPDATE exec_position
SET
  mfe_price = COALESCE(@mfe, mfe_price),
  be_armed = COALESCE(@be_armed, be_armed),
  be_price = COALESCE(@be_price, be_price),
  current_stop_price = COALESCE(@csp, current_stop_price)
WHERE id = @id;";
            try
            {
                int ret = await WithConnectionAsync(async conn =>
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.CommandType = CommandType.Text;

                    cmd.Parameters.Add(new MySqlParameter("@id", positionId));
                    cmd.Parameters.Add(new MySqlParameter("@mfe", (object?)mfePrice ?? DBNull.Value));
                    cmd.Parameters.Add(new MySqlParameter("@be_armed",
                        beArmed.HasValue ? (object)(beArmed.Value ? 1 : 0) : DBNull.Value));
                    cmd.Parameters.Add(new MySqlParameter("@be_price", (object?)bePrice ?? DBNull.Value));
                    cmd.Parameters.Add(new MySqlParameter("@csp", (object?)currentStopPrice ?? DBNull.Value));

                    return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);

                return ret > 0;
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("ExecPosition_UpdateRealtimeAsync", ex);
                return false;
            }
        }
    }
}
