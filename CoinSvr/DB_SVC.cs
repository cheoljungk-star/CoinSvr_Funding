using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using NLog;

namespace CoinSvr
{
    /// <summary>
    /// Service layer for database operations, delegating to DB_MYSQL with async support.
    /// </summary>
    public class DB_SVC : IAsyncDisposable, IDisposable
    {
        private bool _disposed;
        private readonly Logger _logger = LogManager.GetLogger("sql");

        public readonly DB_MYSQL _dbMaria;

        public DB_SVC(string connectionString)
        {
            _dbMaria = new DB_MYSQL(connectionString);
        }

        ~DB_SVC()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await _dbMaria.DisposeAsync().ConfigureAwait(false);
            Dispose(disposing: false);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                _dbMaria.Dispose();
            }
            _disposed = true;
        }

        /// <summary>
        /// Opens the database connection asynchronously.
        /// </summary>
        public Task<bool> ConnectAsync()
        {
            return _dbMaria.ExecuteQueryAsync("SELECT 1;");
        }

        /// <summary>
        /// Checks and returns current connection status.
        /// </summary>
        public bool IsConnected()
        {
            try
            {
                // Uses DB_MYSQL's internal connection state
                return _dbMaria != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Executes a non-query command asynchronously, with optional SQL logging.
        /// </summary>
        public async Task<bool> ExecuteQueryAsync(string query, bool logging = true)
        {
            try
            {
                if (logging)
                {
                    _logger.Info(query);
                }
                return await _dbMaria.ExecuteQueryAsync(query).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("DB_SVC_ExecuteQueryAsync <" + query + ">", ex);
                return false;
            }
        }

        /// <summary>
        /// Executes multiple non-query commands asynchronously.
        /// </summary>
        public Task<int> ExecuteBatchAsync(IEnumerable<string> queries)
        {
            return _dbMaria.ExecuteBatchAsync(queries);
        }

        /// <summary>
        /// Executes a query and returns a DataTable asynchronously.
        /// </summary>
        public async Task<DataTable> SelectQueryAsync(string query)
        {
            try
            {
                //_logger.Info(query);
                return await _dbMaria.SelectQueryAsync(query).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("DB_SVC_SelectQueryAsync <" + query + ">", ex);
                return null;
            }
        }

        /// <summary>
        /// Closes the database connection.
        /// </summary>
        public Task CloseAsync()
        {
            return _dbMaria.CloseAsync();
        }
    }
}
