using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace SqlSugar
{
    ///<summary>
    /// ** description：ActiveX Data Objects
    /// ** author：sunkaixuan
    /// ** date：2017/1/2
    /// ** email:610262374@qq.com
    /// </summary>
    public abstract partial class AdoProvider : AdoAccessory, IAdo
    {
        #region Constructor
        public AdoProvider()
        {
            this.IsEnableLogEvent = false;
            this.CommandType = CommandType.Text;
            this.IsClearParameters = true;
            this.CommandTimeOut = 300;
        }
        #endregion

        #region Properties
        public virtual bool IsNoSql { get; set; }
        internal bool IsOpenAsync { get; set; }
        protected List<IDataParameter> OutputParameters { get; set; }
        public virtual string SqlParameterKeyWord { get { return "@"; } }
        public IDbTransaction Transaction { get; set; }
        public virtual SqlSugarProvider Context { get; set; }
        internal CommandType OldCommandType { get; set; }
        internal bool OldClearParameters { get; set; }
        public IDataParameterCollection DataReaderParameters { get; set; }
        public TimeSpan SqlExecutionTime { get { return AfterTime - BeforeTime; } }
        public TimeSpan ConnectionExecutionTime { get { return CheckConnectionAfterTime - CheckConnectionBeforeTime; } }
        public TimeSpan GetDataExecutionTime { get { return GetDataAfterTime - GetDataBeforeTime; } }
        /// <summary>
        /// Add, delete and modify: the number of affected items;
        /// </summary>
        public int SqlExecuteCount { get; protected set; } = 0;
        public SugarActionType SqlExecuteType { get=> this.Context.SugarActionType;} 
        public StackTraceInfo SqlStackTrace { get { return UtilMethods.GetStackTrace(); } }
        public bool IsDisableMasterSlaveSeparation { get; set; }
        internal DateTime BeforeTime = DateTime.MinValue;
        internal DateTime AfterTime = DateTime.MinValue;
        internal DateTime GetDataBeforeTime = DateTime.MinValue;
        internal DateTime GetDataAfterTime = DateTime.MinValue;
        internal DateTime CheckConnectionBeforeTime = DateTime.MinValue;
        internal DateTime CheckConnectionAfterTime = DateTime.MinValue;
        public virtual IDbBind DbBind
        {
            get
            {
                if (base._DbBind == null)
                {
                    IDbBind bind = InstanceFactory.GetDbBind(this.Context.CurrentConnectionConfig);
                    base._DbBind = bind;
                    bind.Context = this.Context;
                }
                return base._DbBind;
            }
        }
        public virtual int CommandTimeOut { get; set; }
        public virtual CommandType CommandType { get; set; }
        public virtual bool IsEnableLogEvent { get; set; }
        public virtual bool IsClearParameters { get; set; }
        public virtual Action<string, SugarParameter[]> LogEventStarting => this.Context.CurrentConnectionConfig.AopEvents?.OnLogExecuting;
        public virtual Action<string, SugarParameter[]> LogEventCompleted => this.Context.CurrentConnectionConfig.AopEvents?.OnLogExecuted;
        public virtual Action<IDbConnection> CheckConnectionExecuting => this.Context.CurrentConnectionConfig.AopEvents?.CheckConnectionExecuting;
        public virtual Action<IDbConnection, TimeSpan> CheckConnectionExecuted => this.Context.CurrentConnectionConfig.AopEvents?.CheckConnectionExecuted;
        public virtual Action<string, SugarParameter[]> OnGetDataReadering => this.Context.CurrentConnectionConfig.AopEvents?.OnGetDataReadering;
        public virtual Action<string, SugarParameter[], TimeSpan> OnGetDataReadered => this.Context.CurrentConnectionConfig.AopEvents?.OnGetDataReadered;
        public virtual Func<string, SugarParameter[], KeyValuePair<string, SugarParameter[]>> ProcessingEventStartingSQL => this.Context.CurrentConnectionConfig.AopEvents?.OnExecutingChangeSql;
        protected virtual Func<string, string> FormatSql { get; set; }
        public virtual Action<SqlSugarException> ErrorEvent => this.Context.CurrentConnectionConfig.AopEvents?.OnError;
        public virtual Action<DiffLogModel> DiffLogEvent => this.Context.CurrentConnectionConfig.AopEvents?.OnDiffLogEvent;
        public virtual List<IDbConnection> SlaveConnections { get; set; }
        public virtual IDbConnection MasterConnection { get; set; }
        public virtual string MasterConnectionString { get; set; }
        public virtual CancellationToken? CancellationToken { get; set; }
        #endregion

        #region Connection
        public virtual bool IsValidConnection() 
        {
            try
            {
                if (this.IsAnyTran()) 
                {
                    return true;
                }
                using (OpenAlways()) 
                {
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
        public virtual bool IsValidConnectionNoClose()
        {
            try
            {
                this.Open();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        public virtual void Open()
        {
            CheckConnection();
        }
        public virtual async Task OpenAsync() 
        {
            await CheckConnectionAsync();
        }
        public SugarConnection  OpenAlways() 
        {
            SugarConnection result = new SugarConnection();
            result.IsAutoClose = this.Context.CurrentConnectionConfig.IsAutoCloseConnection;
            result.conn = this.Connection;
            result.Context = this.Context;
            this.Context.CurrentConnectionConfig.IsAutoCloseConnection = false;
            this.Open();
            return result;
        } 
        public virtual void Close()
        {
            if (this.Transaction != null)
            {
                this.Transaction = null;
            }
            if (this.Connection != null && this.Connection.State == ConnectionState.Open)
            {
                this.Connection.Close();
            }
            if (this.IsMasterSlaveSeparation && this.SlaveConnections.HasValue())
            {
                foreach (var slaveConnection in this.SlaveConnections)
                {
                    if (slaveConnection != null && slaveConnection.State == ConnectionState.Open)
                    {
                        slaveConnection.Close();
                    }
                }
            }
        }
        public virtual void Dispose()
        {
            if (this.Transaction != null)
            {
                this.Transaction.Rollback();
                this.Transaction = null;
            }
            //if (this.Connection != null && this.Connection.State != ConnectionState.Open)
            //{
            //    this.Connection.Close();
            //}
            if (this.Connection != null)
            {
                this.Connection.Dispose();
            }
            this.Connection = null;

            if (this.IsMasterSlaveSeparation)
            {
                if (this.SlaveConnections != null)
                {
                    foreach (var slaveConnection in this.SlaveConnections)
                    {
                        if (slaveConnection != null && slaveConnection.State == ConnectionState.Open)
                        {
                            slaveConnection.Dispose();
                        }
                    }
                }
            }
        }
        public virtual void CheckConnection()
        {
            this.CheckConnectionBefore(this.Connection);
            if (this.Connection.State != ConnectionState.Open)
            {
                try
                {
                    this.Connection.Open();
                }
                catch (Exception ex)
                {
                    if (this.Context.CurrentConnectionConfig?.DbType==DbType.SqlServer&&ex.Message?.Contains("provider: SSL")==true) 
                    {
                        Check.ExceptionEasy(true,ex.Message, "SSL出错，因为升级了驱动,字符串增加Encrypt=True;TrustServerCertificate=True;即可。详细错误：" + ex.Message);
                    }
                    Check.Exception(true, ErrorMessage.ConnnectionOpen, ex.Message+$"DbType=\"{this.Context.CurrentConnectionConfig.DbType}\";ConfigId=\"{this.Context.CurrentConnectionConfig.ConfigId}\"");
                }
            }
            this.CheckConnectionAfter(this.Connection);
        }

        public virtual async Task CheckConnectionAsync()
        {
            this.CheckConnectionBefore(this.Connection);
            if (this.Connection.State != ConnectionState.Open)
            {
                try
                {
                    await (this.Connection as DbConnection).OpenAsync();
                }
                catch (Exception ex)
                {
                    Check.Exception(true, ErrorMessage.ConnnectionOpen, ex.Message + $"DbType=\"{this.Context.CurrentConnectionConfig.DbType}\";ConfigId=\"{this.Context.CurrentConnectionConfig.ConfigId}\"");
                }
            }
            this.CheckConnectionAfter(this.Connection);
        }
        public virtual void CheckConnectionBefore(IDbConnection Connection)
        {
            this.CheckConnectionBeforeTime = DateTime.Now;
            if (this.IsEnableLogEvent)
            {
                Action<IDbConnection> action = CheckConnectionExecuting;
                if (action != null)
                {
                    action(Connection);
                }
            }
        }
        public virtual void CheckConnectionAfter(IDbConnection Connection)
        {
            this.CheckConnectionAfterTime = DateTime.Now;
            if (this.IsEnableLogEvent)
            {
                Action<IDbConnection, TimeSpan> action = CheckConnectionExecuted;
                if (action != null)
                {
                    action(Connection,this.ConnectionExecutionTime);
                }
            }
        }
        #endregion

        #region Transaction
        public virtual bool IsAnyTran() 
        {
            return this.Transaction != null;
        }
        public virtual bool IsNoTran()
        {
            return this.Transaction == null;
        }
        public virtual void BeginTran()
        {
            CheckConnection();
            if (this.Transaction == null)
                this.Transaction = this.Connection.BeginTransaction();
        }
        public virtual async Task BeginTranAsync()
        {
            await CheckConnectionAsync();
            if (this.Transaction == null)
                this.Transaction =await (this.Connection as DbConnection).BeginTransactionAsync();
        }
        public virtual void BeginTran(IsolationLevel iso)
        {
            CheckConnection();
            if (this.Transaction == null)
                this.Transaction = this.Connection.BeginTransaction(iso);
        }
        public virtual async Task BeginTranAsync(IsolationLevel iso)
        {
            await CheckConnectionAsync();
            if (this.Transaction == null)
                this.Transaction =await (this.Connection as DbConnection).BeginTransactionAsync(iso);
        }
        public virtual void RollbackTran()
        {
            if (this.Transaction != null)
            {
                this.Transaction.Rollback();
                this.Transaction = null;
                if (this.Context.CurrentConnectionConfig.IsAutoCloseConnection) this.Close();
            }
        }

        public virtual async Task RollbackTranAsync()
        {
            if (this.Transaction != null)
            {
                await (this.Transaction as DbTransaction).RollbackAsync();
                this.Transaction = null;
                if (this.Context.CurrentConnectionConfig.IsAutoCloseConnection) await this.CloseAsync();
            }
        }
        public virtual void CommitTran()
        {
            if (this.Transaction != null)
            {
                this.Transaction.Commit();
                this.Transaction = null;
                if (this.Context.CurrentConnectionConfig.IsAutoCloseConnection) this.Close();
            }
        }
        public virtual async Task CommitTranAsync()
        {
            if (this.Transaction != null)
            {
                await (this.Transaction as DbTransaction).CommitAsync();
                this.Transaction = null;
                if (this.Context.CurrentConnectionConfig.IsAutoCloseConnection) await this.CloseAsync();
            }
        }
        #endregion

        #region abstract
        public abstract IDataParameter[] ToIDbDataParameter(params SugarParameter[] pars);
        public abstract void SetCommandToAdapter(IDataAdapter adapter, DbCommand command);
        public abstract IDataAdapter GetAdapter();
        public abstract DbCommand GetCommand(string sql, SugarParameter[] pars);
        public abstract IDbConnection Connection { get; set; }
        public abstract void BeginTran(string transactionName);//Only SqlServer
        public abstract void BeginTran(IsolationLevel iso, string transactionName);//Only SqlServer 
        #endregion

        #region Use
        public SqlSugarTransactionAdo UseTran()
        {
            return new SqlSugarTransactionAdo(this.Context);
        }
        public DbResult<bool> UseTran(Action action, Action<Exception> errorCallBack = null)
        {
            var result = new DbResult<bool>();
            try
            {
                this.BeginTran();
                if (action != null)
                    action();
                this.CommitTran();
                result.Data = result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                result.ErrorException = ex;
                result.ErrorMessage = ex.Message;
                result.IsSuccess = false;
                this.RollbackTran();
                if (errorCallBack != null)
                {
                    errorCallBack(ex);
                }
            }
            return result;
        }

        public async Task<DbResult<bool>> UseTranAsync(Func<Task> action, Action<Exception> errorCallBack = null)
        {
            var result = new DbResult<bool>();
            try
            {
                await this.BeginTranAsync();
                if (action != null)
                    await action();
                await this.CommitTranAsync();
                result.Data = result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                result.ErrorException = ex;
                result.ErrorMessage = ex.Message;
                result.IsSuccess = false;
                await this.RollbackTranAsync();
                if (errorCallBack != null)
                {
                    errorCallBack(ex);
                }
            }
            return result;
        }

        public DbResult<T> UseTran<T>(Func<T> action, Action<Exception> errorCallBack = null)
        {
            var result = new DbResult<T>();
            try
            {
                this.BeginTran();
                if (action != null)
                    result.Data = action();
                this.CommitTran();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                result.ErrorException = ex;
                result.ErrorMessage = ex.Message;
                result.IsSuccess = false;
                this.RollbackTran();
                if (errorCallBack != null)
                {
                    errorCallBack(ex);
                }
            }
            return result;
        }

        public async Task<DbResult<T>> UseTranAsync<T>(Func<Task<T>> action, Action<Exception> errorCallBack = null)
        {
            var result = new DbResult<T>();
            try
            {
                this.BeginTran();
                if (action != null)
                    result.Data = await action();
                this.CommitTran();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                result.ErrorException = ex;
                result.ErrorMessage = ex.Message;
                result.IsSuccess = false;
                this.RollbackTran();
                if (errorCallBack != null)
                {
                    errorCallBack(ex);
                }
            }
            return result;
        }

        public IAdo UseStoredProcedure()
        {
            this.OldCommandType = this.CommandType;
            this.OldClearParameters = this.IsClearParameters;
            this.CommandType = CommandType.StoredProcedure;
            this.IsClearParameters = false;
            return this;
        }
        #endregion

        #region Core
        public virtual int ExecuteCommandWithGo(string sql, params SugarParameter[] parameters)
        {
            if (string.IsNullOrEmpty(sql))
                return 0;
            if (!sql.ToLower().Contains("go"))
            {
                return ExecuteCommand(sql);
            }
            System.Collections.ArrayList al = new System.Collections.ArrayList();
            System.Text.RegularExpressions.Regex reg = new System.Text.RegularExpressions.Regex(@"^(\s*)go(\s*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.ExplicitCapture);
            al.AddRange(reg.Split(sql));
            int count = 0;
            foreach (string item in al)
            {
                if (item.HasValue())
                {
                    count += ExecuteCommand(item, parameters);
                }
            }
            return count;
        }
        public virtual int ExecuteCommand(string sql, params SugarParameter[] parameters)
        {
            try
            {
                InitParameters(ref sql, parameters);
                if (IsFormat(parameters))
                    sql = FormatSql(sql);
                if (this.Context.CurrentConnectionConfig?.SqlMiddle?.IsSqlMiddle == true)
                    return this.Context.CurrentConnectionConfig.SqlMiddle.ExecuteCommand(sql, parameters);
                SetConnectionStart(sql);
                if (this.ProcessingEventStartingSQL != null)
                    ExecuteProcessingSQL(ref sql, ref parameters);
                ExecuteBefore(sql, parameters);
                using IDbCommand sqlCommand = GetCommand(sql, parameters);
                int count = sqlCommand.ExecuteNonQuery();
                if (this.IsClearParameters)
                    sqlCommand.Parameters.Clear();
                // 影响条数
                this.SqlExecuteCount = count;
                ExecuteAfter(sql, parameters);
                //sqlCommand.Dispose();
                return count;
            }
            catch (Exception ex)
            {
                SugarCatch(ex, sql,parameters);
                CommandType = CommandType.Text;
                if (ErrorEvent != null)
                    ExecuteErrorEvent(sql, parameters, ex);
                throw ex;
            }
            finally
            {
                if (this.IsAutoClose()) this.Close();
                SetConnectionEnd(sql);
            }
        }
        public virtual IDataReader GetDataReader(string sql, params SugarParameter[] parameters)
        {
            try
            {
                InitParameters(ref sql, parameters);
                if (IsFormat(parameters))
                    sql = FormatSql(sql);
                if (this.Context.CurrentConnectionConfig?.SqlMiddle?.IsSqlMiddle == true)
                    return this.Context.CurrentConnectionConfig.SqlMiddle.GetDataReader(sql, parameters);
                SetConnectionStart(sql);
                var isSp = this.CommandType == CommandType.StoredProcedure;
                if (this.ProcessingEventStartingSQL != null)
                    ExecuteProcessingSQL(ref sql,ref parameters);
                ExecuteBefore(sql, parameters);
                IDbCommand sqlCommand = GetCommand(sql, parameters);
                IDataReader sqlDataReader = sqlCommand.ExecuteReader(this.IsAutoClose() ? CommandBehavior.CloseConnection : CommandBehavior.Default);
                if (isSp)
                    DataReaderParameters = sqlCommand.Parameters;
                if (this.IsClearParameters)
                    sqlCommand.Parameters.Clear();
                ExecuteAfter(sql, parameters);
                SetConnectionEnd(sql);
                if (SugarCompatible.IsFramework || this.Context.CurrentConnectionConfig.DbType != DbType.Sqlite)
                    sqlCommand.Dispose();
                return sqlDataReader;
            }
            catch (Exception ex)
            {
                SugarCatch(ex, sql, parameters);
                CommandType = CommandType.Text;
                if (ErrorEvent != null)
                    ExecuteErrorEvent(sql, parameters, ex);
                throw;
            }
        }
        public virtual DataSet GetDataSetAll(string sql, params SugarParameter[] parameters)
        {
            try
            {
                InitParameters(ref sql, parameters);
                if (IsFormat(parameters))
                    sql = FormatSql(sql);
                if (this.Context.CurrentConnectionConfig?.SqlMiddle?.IsSqlMiddle == true)
                    return this.Context.CurrentConnectionConfig.SqlMiddle.GetDataSetAll(sql, parameters);
                SetConnectionStart(sql);
                if (this.ProcessingEventStartingSQL != null)
                    ExecuteProcessingSQL(ref sql,ref parameters);
                ExecuteBefore(sql, parameters);
                IDataAdapter dataAdapter = this.GetAdapter();
                DbCommand sqlCommand = GetCommand(sql, parameters);
                this.SetCommandToAdapter(dataAdapter, sqlCommand);
                DataSet ds = new DataSet();
                dataAdapter.Fill(ds);
                if (this.IsClearParameters)
                    sqlCommand.Parameters.Clear();
                ExecuteAfter(sql, parameters);
                sqlCommand.Dispose();
                return ds;
            }
            catch (Exception ex)
            {
                SugarCatch(ex, sql, parameters);
                CommandType = CommandType.Text;
                if (ErrorEvent != null)
                    ExecuteErrorEvent(sql, parameters, ex);
                throw;
            }
            finally
            {
                if (this.IsAutoClose()) this.Close();
                SetConnectionEnd(sql);
            }
        }
        public virtual object GetScalar(string sql, params SugarParameter[] parameters)
        {
            try
            {
                InitParameters(ref sql, parameters);
                if (IsFormat(parameters))
                    sql = FormatSql(sql);
                if (this.Context.CurrentConnectionConfig?.SqlMiddle?.IsSqlMiddle == true)
                    return this.Context.CurrentConnectionConfig.SqlMiddle.GetScalar(sql, parameters);
                SetConnectionStart(sql);
                if (this.ProcessingEventStartingSQL != null)
                    ExecuteProcessingSQL(ref sql,ref parameters);
                ExecuteBefore(sql, parameters);
                IDbCommand sqlCommand = GetCommand(sql, parameters);
                object scalar = sqlCommand.ExecuteScalar();
                //scalar = (scalar == null ? 0 : scalar);
                if (this.IsClearParameters)
                    sqlCommand.Parameters.Clear();
                ExecuteAfter(sql, parameters);
                sqlCommand.Dispose();
                return scalar;
            }
            catch (Exception ex)
            {
                SugarCatch(ex, sql, parameters);
                CommandType = CommandType.Text;
                if (ErrorEvent != null)
                    ExecuteErrorEvent(sql, parameters, ex);
                throw;
            }
            finally
            {
                if (this.IsAutoClose()) this.Close();
                SetConnectionEnd(sql);
            }
        }

        public virtual async Task<int> ExecuteCommandAsync(string sql, params SugarParameter[] parameters)
        {
            try
            {
                Async();
                InitParameters(ref sql, parameters);
                if (IsFormat(parameters))
                    sql = FormatSql(sql);
                if (this.Context.CurrentConnectionConfig?.SqlMiddle?.IsSqlMiddle == true)
                    return await this.Context.CurrentConnectionConfig.SqlMiddle.ExecuteCommandAsync(sql, parameters);
                SetConnectionStart(sql);
                if (this.ProcessingEventStartingSQL != null)
                    ExecuteProcessingSQL(ref sql,ref parameters);
                ExecuteBefore(sql, parameters);
                using var sqlCommand =IsOpenAsync? await GetCommandAsync(sql, parameters) : GetCommand(sql, parameters);
                int count;
                if (this.CancellationToken == null)
                    count=await sqlCommand.ExecuteNonQueryAsync();
                else
                    count=await sqlCommand.ExecuteNonQueryAsync(this.CancellationToken.Value);
                if (this.IsClearParameters)
                    sqlCommand.Parameters.Clear();
                this.SqlExecuteCount = count;
                ExecuteAfter(sql, parameters);
                //sqlCommand.Dispose();
                return count;
            }
            catch (Exception ex)
            {
                SugarCatch(ex, sql, parameters);
                CommandType = CommandType.Text;
                if (ErrorEvent != null)
                    ExecuteErrorEvent(sql, parameters, ex);
                throw;
            }
            finally
            {
                if (this.IsAutoClose()) this.Close();
                SetConnectionEnd(sql);
            }
        }
        public virtual async Task<IDataReader> GetDataReaderAsync(string sql, params SugarParameter[] parameters)
        {
            try
            {
                Async();
                InitParameters(ref sql, parameters);
                if (IsFormat(parameters))
                    sql = FormatSql(sql);
                if (this.Context.CurrentConnectionConfig?.SqlMiddle?.IsSqlMiddle == true)
                    return await this.Context.CurrentConnectionConfig.SqlMiddle.GetDataReaderAsync(sql, parameters);
                SetConnectionStart(sql);
                var isSp = this.CommandType == CommandType.StoredProcedure;
                if (this.ProcessingEventStartingSQL != null)
                    ExecuteProcessingSQL(ref sql,ref parameters);
                ExecuteBefore(sql, parameters);
                var sqlCommand = IsOpenAsync ? await GetCommandAsync(sql, parameters) : GetCommand(sql, parameters);
                DbDataReader sqlDataReader;
                if(this.CancellationToken==null)
                    sqlDataReader=await sqlCommand.ExecuteReaderAsync(this.IsAutoClose() ? CommandBehavior.CloseConnection : CommandBehavior.Default);
                else
                    sqlDataReader=await sqlCommand.ExecuteReaderAsync(this.IsAutoClose() ? CommandBehavior.CloseConnection : CommandBehavior.Default,this.CancellationToken.Value);
                if (isSp)
                    DataReaderParameters = sqlCommand.Parameters;
                if (this.IsClearParameters)
                    sqlCommand.Parameters.Clear();
                ExecuteAfter(sql, parameters);
                SetConnectionEnd(sql);
                if (SugarCompatible.IsFramework || this.Context.CurrentConnectionConfig.DbType != DbType.Sqlite)
                    sqlCommand.Dispose();
                return sqlDataReader;
            }
            catch (Exception ex)
            {
                SugarCatch(ex, sql, parameters);
                CommandType = CommandType.Text;
                if (ErrorEvent != null)
                    ExecuteErrorEvent(sql, parameters, ex);
                throw;
            }
        }
        public virtual async Task<object> GetScalarAsync(string sql, params SugarParameter[] parameters)
        {
            try
            {
                Async();
                InitParameters(ref sql, parameters);
                if (IsFormat(parameters))
                    sql = FormatSql(sql);
                if (this.Context.CurrentConnectionConfig?.SqlMiddle?.IsSqlMiddle == true)
                    return await this.Context.CurrentConnectionConfig.SqlMiddle.GetScalarAsync(sql, parameters);
                SetConnectionStart(sql);
                if (this.ProcessingEventStartingSQL != null)
                    ExecuteProcessingSQL(ref sql,ref parameters);
                ExecuteBefore(sql, parameters);
                var sqlCommand = IsOpenAsync ? await GetCommandAsync(sql, parameters) : GetCommand(sql, parameters);
                object scalar;
                if(CancellationToken==null)
                    scalar=await sqlCommand.ExecuteScalarAsync();
                else
                    scalar = await sqlCommand.ExecuteScalarAsync(this.CancellationToken.Value);
                //scalar = (scalar == null ? 0 : scalar);
                if (this.IsClearParameters)
                    sqlCommand.Parameters.Clear();
                ExecuteAfter(sql, parameters);
                sqlCommand.Dispose();
                return scalar;
            }
            catch (Exception ex)
            {
                SugarCatch(ex, sql, parameters);
                CommandType = CommandType.Text;
                if (ErrorEvent != null)
                    ExecuteErrorEvent(sql, parameters, ex);
                throw;
            }
            finally
            {
                if (this.IsAutoClose()) this.Close();
                SetConnectionEnd(sql);
            }
        }
        public virtual Task<DataSet> GetDataSetAllAsync(string sql, params SugarParameter[] parameters)
        {
            Async();

            //False asynchrony . No Support DataSet
            if (CancellationToken == null)
            {
                return Task.Run(() =>
                {
                    return GetDataSetAll(sql, parameters);
                });
            }
            else 
            {
                return Task.Run(() =>
                {
                    return GetDataSetAll(sql, parameters);
                },this.CancellationToken.Value);
            }
        }
        #endregion

        #region Methods

        public virtual string GetString(string sql, object parameters)
        {
            return GetString(sql, this.GetParameters(parameters));
        }
        public virtual string GetString(string sql, params SugarParameter[] parameters)
        {
            return Convert.ToString(GetScalar(sql, parameters));
        }
        public virtual string GetString(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return GetString(sql);
            }
            else
            {
                return GetString(sql, parameters.ToArray());
            }
        }


        public virtual Task<string> GetStringAsync(string sql, object parameters)
        {
            return GetStringAsync(sql, this.GetParameters(parameters));
        }
        public virtual async Task<string> GetStringAsync(string sql, params SugarParameter[] parameters)
        {
            return Convert.ToString(await GetScalarAsync(sql, parameters));
        }
        public virtual Task<string> GetStringAsync(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return GetStringAsync(sql);
            }
            else
            {
                return GetStringAsync(sql, parameters.ToArray());
            }
        }



        public virtual long GetLong(string sql, object parameters = null)
        {
            return Convert.ToInt64(GetScalar(sql, GetParameters(parameters)));
        }
        public virtual async Task<long> GetLongAsync(string sql, object parameters = null)
        {
            return Convert.ToInt64(await GetScalarAsync(sql, GetParameters(parameters)));
        }


        public virtual int GetInt(string sql, object parameters)
        {
            return GetInt(sql, this.GetParameters(parameters));
        }
        public virtual int GetInt(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return GetInt(sql);
            }
            else
            {
                return GetInt(sql, parameters.ToArray());
            }
        }
        public virtual int GetInt(string sql, params SugarParameter[] parameters)
        {
            return GetScalar(sql, parameters).ObjToInt();
        }

        public virtual Task<int> GetIntAsync(string sql, object parameters)
        {
            return GetIntAsync(sql, this.GetParameters(parameters));
        }
        public virtual Task<int> GetIntAsync(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return GetIntAsync(sql);
            }
            else
            {
                return GetIntAsync(sql, parameters.ToArray());
            }
        }
        public virtual async Task<int> GetIntAsync(string sql, params SugarParameter[] parameters)
        {
            var list = await GetScalarAsync(sql, parameters);
            return list.ObjToInt();
        }

        public virtual Double GetDouble(string sql, object parameters)
        {
            return GetDouble(sql, this.GetParameters(parameters));
        }
        public virtual Double GetDouble(string sql, params SugarParameter[] parameters)
        {
            return GetScalar(sql, parameters).ObjToMoney();
        }
        public virtual Double GetDouble(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return GetDouble(sql);
            }
            else
            {
                return GetDouble(sql, parameters.ToArray());
            }
        }

        public virtual Task<Double> GetDoubleAsync(string sql, object parameters)
        {
            return GetDoubleAsync(sql, this.GetParameters(parameters));
        }
        public virtual async Task<Double> GetDoubleAsync(string sql, params SugarParameter[] parameters)
        {
            var result = await GetScalarAsync(sql, parameters);
            return result.ObjToMoney();
        }
        public virtual Task<Double> GetDoubleAsync(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return GetDoubleAsync(sql);
            }
            else
            {
                return GetDoubleAsync(sql, parameters.ToArray());
            }
        }


        public virtual decimal GetDecimal(string sql, object parameters)
        {
            return GetDecimal(sql, this.GetParameters(parameters));
        }
        public virtual decimal GetDecimal(string sql, params SugarParameter[] parameters)
        {
            return GetScalar(sql, parameters).ObjToDecimal();
        }
        public virtual decimal GetDecimal(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return GetDecimal(sql);
            }
            else
            {
                return GetDecimal(sql, parameters.ToArray());
            }
        }


        public virtual Task<decimal> GetDecimalAsync(string sql, object parameters)
        {
            return GetDecimalAsync(sql, this.GetParameters(parameters));
        }
        public virtual async Task<decimal> GetDecimalAsync(string sql, params SugarParameter[] parameters)
        {
            var result = await GetScalarAsync(sql, parameters);
            return result.ObjToDecimal();
        }
        public virtual Task<decimal> GetDecimalAsync(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return GetDecimalAsync(sql);
            }
            else
            {
                return GetDecimalAsync(sql, parameters.ToArray());
            }
        }



        public virtual DateTime GetDateTime(string sql, object parameters)
        {
            return GetDateTime(sql, this.GetParameters(parameters));
        }
        public virtual DateTime GetDateTime(string sql, params SugarParameter[] parameters)
        {
            return GetScalar(sql, parameters).ObjToDate();
        }
        public virtual DateTime GetDateTime(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return GetDateTime(sql);
            }
            else
            {
                return GetDateTime(sql, parameters.ToArray());
            }
        }




        public virtual Task<DateTime> GetDateTimeAsync(string sql, object parameters)
        {
            return GetDateTimeAsync(sql, this.GetParameters(parameters));
        }
        public virtual async Task<DateTime> GetDateTimeAsync(string sql, params SugarParameter[] parameters)
        {
            var list = await GetScalarAsync(sql, parameters);
            return list.ObjToDate();
        }
        public virtual Task<DateTime> GetDateTimeAsync(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return GetDateTimeAsync(sql);
            }
            else
            {
                return GetDateTimeAsync(sql, parameters.ToArray());
            }
        }


        public virtual List<T> SqlQuery<T>(string sql, object parameters = null)
        {
            var sugarParameters = this.GetParameters(parameters);
            return SqlQuery<T>(sql, sugarParameters);
        }
        public virtual List<T> SqlQuery<T>(string sql, params SugarParameter[] parameters)
        {
            var result = SqlQuery<T, object, object, object, object, object, object>(sql, parameters);
            return result.Item1;
        }
        public List<T> MasterSqlQuery<T>(string sql, object parameters = null) 
        {
            var oldValue = this.Context.Ado.IsDisableMasterSlaveSeparation;
            this.Context.Ado.IsDisableMasterSlaveSeparation = true;
            var result = this.Context.Ado.SqlQuery<T>(sql, parameters);
            this.Context.Ado.IsDisableMasterSlaveSeparation = oldValue;
            return result;
        }
        public async Task<List<T>> MasterSqlQueryAasync<T>(string sql, object parameters = null)
        {
            var oldValue = this.Context.Ado.IsDisableMasterSlaveSeparation;
            this.Context.Ado.IsDisableMasterSlaveSeparation = true;
            var result = await this.Context.Ado.SqlQueryAsync<T>(sql, parameters);
            this.Context.Ado.IsDisableMasterSlaveSeparation = oldValue;
            return result;
        }
        public virtual List<T> SqlQuery<T>(string sql, List<SugarParameter> parameters)
        {
            if (parameters != null)
            {
                return SqlQuery<T>(sql, parameters.ToArray());
            }
            else
            {
                return SqlQuery<T>(sql);
            }
        }
        public Tuple<List<T>, List<T2>> SqlQuery<T, T2>(string sql, object parameters = null)
        {
            var result = SqlQuery<T, T2, object, object, object, object, object>(sql, parameters);
            return new Tuple<List<T>, List<T2>>(result.Item1, result.Item2);
        }
        public Tuple<List<T>, List<T2>, List<T3>> SqlQuery<T, T2, T3>(string sql, object parameters = null)
        {
            var result = SqlQuery<T, T2, T3, object, object, object, object>(sql, parameters);
            return new Tuple<List<T>, List<T2>, List<T3>>(result.Item1, result.Item2, result.Item3);
        }
        public Tuple<List<T>, List<T2>, List<T3>, List<T4>> SqlQuery<T, T2, T3, T4>(string sql, object parameters = null)
        {
            var result = SqlQuery<T, T2, T3, T4, object, object, object>(sql, parameters);
            return new Tuple<List<T>, List<T2>, List<T3>, List<T4>>(result.Item1, result.Item2, result.Item3, result.Item4);
        }
        public Tuple<List<T>, List<T2>, List<T3>, List<T4>, List<T5>> SqlQuery<T, T2, T3, T4, T5>(string sql, object parameters = null)
        {
            var result = SqlQuery<T, T2, T3, T4, T5, object, object>(sql, parameters);
            return new Tuple<List<T>, List<T2>, List<T3>, List<T4>, List<T5>>(result.Item1, result.Item2, result.Item3, result.Item4, result.Item5);
        }
        public Tuple<List<T>, List<T2>, List<T3>, List<T4>, List<T5>, List<T6>> SqlQuery<T, T2, T3, T4, T5, T6>(string sql, object parameters = null)
        {
            var result = SqlQuery<T, T2, T3, T4, T5, T6, object>(sql, parameters);
            return new Tuple<List<T>, List<T2>, List<T3>, List<T4>, List<T5>, List<T6>>(result.Item1, result.Item2, result.Item3, result.Item4, result.Item5, result.Item6);
        }
        public virtual Tuple<List<T>, List<T2>, List<T3>, List<T4>, List<T5>, List<T6>, List<T7>> SqlQuery<T, T2, T3, T4, T5, T6, T7>(string sql, object parameters = null)
        {
            var parsmeterArray = this.GetParameters(parameters);
            this.Context.InitMappingInfo<T>();
            var builder = InstanceFactory.GetSqlbuilder(this.Context.CurrentConnectionConfig);
            builder.SqlQueryBuilder.sql.Append(sql);
            if (parsmeterArray != null && parsmeterArray.Any())
                builder.SqlQueryBuilder.Parameters.AddRange(parsmeterArray);
            string sqlString = builder.SqlQueryBuilder.ToSqlString();
            SugarParameter[] Parameters = builder.SqlQueryBuilder.Parameters.ToArray();
            this.GetDataBefore(sqlString, Parameters);
            using (var dataReader = this.GetDataReader(sqlString, Parameters))
            {
                DbDataReader DbReader = (DbDataReader)dataReader;
                List<T> result = new List<T>();
                if (DbReader.HasRows)
                {
                    result = GetData<T>(typeof(T), dataReader);
                }
                else 
                {
                    dataReader.Read();
                }
                List<T2> result2 = null;
                if (NextResult(dataReader))
                {
                    this.Context.InitMappingInfo<T2>();
                    result2 = GetData<T2>(typeof(T2), dataReader);
                }
                List<T3> result3 = null;
                if (NextResult(dataReader))
                {
                    this.Context.InitMappingInfo<T3>();
                    result3 = GetData<T3>(typeof(T3), dataReader);
                }
                List<T4> result4 = null;
                if (NextResult(dataReader))
                {
                    this.Context.InitMappingInfo<T4>();
                    result4 = GetData<T4>(typeof(T4), dataReader);
                }
                List<T5> result5 = null;
                if (NextResult(dataReader))
                {
                    this.Context.InitMappingInfo<T5>();
                    result5 = GetData<T5>(typeof(T5), dataReader);
                }
                List<T6> result6 = null;
                if (NextResult(dataReader))
                {
                    this.Context.InitMappingInfo<T6>();
                    result6 = GetData<T6>(typeof(T6), dataReader);
                }
                List<T7> result7 = null;
                if (NextResult(dataReader))
                {
                    this.Context.InitMappingInfo<T7>();
                    result7 = GetData<T7>(typeof(T7), dataReader);
                }
                builder.SqlQueryBuilder.Clear();
                if (this.Context.Ado.DataReaderParameters != null)
                {
                    foreach (IDataParameter item in this.Context.Ado.DataReaderParameters)
                    {
                        var parameter = parsmeterArray.FirstOrDefault(it => item.ParameterName.Substring(1) == it.ParameterName.Substring(1));
                        if (parameter != null)
                        {
                            parameter.Value = item.Value;
                        }
                    }
                    this.Context.Ado.DataReaderParameters = null;
                }
                this.GetDataAfter(sqlString, Parameters);
                return Tuple.Create<List<T>, List<T2>, List<T3>, List<T4>, List<T5>, List<T6>, List<T7>>(result, result2, result3, result4, result5, result6, result7);
            }
        }

        public Task<List<T>> SqlQueryAsync<T>(string sql, object parameters, CancellationToken token) 
        {
            this.CancellationToken = token;
            return SqlQueryAsync<T>(sql, parameters);
        }

        public virtual Task<List<T>> SqlQueryAsync<T>(string sql, object parameters = null)
        {
            var sugarParameters = this.GetParameters(parameters);
            return SqlQueryAsync<T>(sql, sugarParameters);
        }
        public virtual async Task<List<T>> SqlQueryAsync<T>(string sql, params SugarParameter[] parameters)
        {
            var result = await SqlQueryAsync<T, object, object, object, object, object, object>(sql, parameters);
            return result.Item1;
        }
        public virtual Task<List<T>> SqlQueryAsync<T>(string sql, List<SugarParameter> parameters)
        {
            if (parameters != null)
            {
                return SqlQueryAsync<T>(sql, parameters.ToArray());
            }
            else
            {
                return SqlQueryAsync<T>(sql);
            }
        }
        public async Task<Tuple<List<T>, List<T2>>> SqlQueryAsync<T, T2>(string sql, object parameters = null)
        {
            var result = await SqlQueryAsync<T, T2, object, object, object, object, object>(sql, parameters);
            return new Tuple<List<T>, List<T2>>(result.Item1, result.Item2);
        }
        public async Task<Tuple<List<T>, List<T2>, List<T3>>> SqlQueryAsync<T, T2, T3>(string sql, object parameters = null)
        {
            var result = await SqlQueryAsync<T, T2, T3, object, object, object, object>(sql, parameters);
            return new Tuple<List<T>, List<T2>, List<T3>>(result.Item1, result.Item2, result.Item3);
        }
        public async Task<Tuple<List<T>, List<T2>, List<T3>, List<T4>>> SqlQueryAsync<T, T2, T3, T4>(string sql, object parameters = null)
        {
            var result = await SqlQueryAsync<T, T2, T3, T4, object, object, object>(sql, parameters);
            return new Tuple<List<T>, List<T2>, List<T3>, List<T4>>(result.Item1, result.Item2, result.Item3, result.Item4);
        }
        public async Task<Tuple<List<T>, List<T2>, List<T3>, List<T4>, List<T5>>> SqlQueryAsync<T, T2, T3, T4, T5>(string sql, object parameters = null)
        {
            var result = await SqlQueryAsync<T, T2, T3, T4, T5, object, object>(sql, parameters);
            return new Tuple<List<T>, List<T2>, List<T3>, List<T4>, List<T5>>(result.Item1, result.Item2, result.Item3, result.Item4, result.Item5);
        }
        public async Task<Tuple<List<T>, List<T2>, List<T3>, List<T4>, List<T5>, List<T6>>> SqlQueryAsync<T, T2, T3, T4, T5, T6>(string sql, object parameters = null)
        {
            var result =await SqlQueryAsync<T, T2, T3, T4, T5, T6, object>(sql, parameters);
            return new Tuple<List<T>, List<T2>, List<T3>, List<T4>, List<T5>, List<T6>>(result.Item1, result.Item2, result.Item3, result.Item4, result.Item5, result.Item6);
        }
        public async Task<Tuple<List<T>, List<T2>, List<T3>, List<T4>, List<T5>, List<T6>, List<T7>>> SqlQueryAsync<T, T2, T3, T4, T5, T6, T7>(string sql, object parameters = null)
        {
            var parsmeterArray = this.GetParameters(parameters);
            this.Context.InitMappingInfo<T>();
            var builder = InstanceFactory.GetSqlbuilder(this.Context.CurrentConnectionConfig);
            builder.SqlQueryBuilder.sql.Append(sql);
            if (parsmeterArray != null && parsmeterArray.Any())
                builder.SqlQueryBuilder.Parameters.AddRange(parsmeterArray);
            string sqlString = builder.SqlQueryBuilder.ToSqlString();
            SugarParameter[] Parameters = builder.SqlQueryBuilder.Parameters.ToArray();
            this.GetDataBefore(sqlString, Parameters);
            using (var dataReader = await this.GetDataReaderAsync(sqlString, Parameters))
            {
                DbDataReader DbReader = (DbDataReader)dataReader;
                List<T> result = new List<T>();
                if (DbReader.HasRows)
                {
                    result =await GetDataAsync<T>(typeof(T), dataReader);
                }
                List<T2> result2 = null;
                if (NextResult(dataReader))
                {
                    this.Context.InitMappingInfo<T2>();
                    result2 = await GetDataAsync<T2>(typeof(T2), dataReader);
                }
                List<T3> result3 = null;
                if (NextResult(dataReader))
                {
                    this.Context.InitMappingInfo<T3>();
                    result3 = await GetDataAsync<T3>(typeof(T3), dataReader);
                }
                List<T4> result4 = null;
                if (NextResult(dataReader))
                {
                    this.Context.InitMappingInfo<T4>();
                    result4 = await GetDataAsync<T4>(typeof(T4), dataReader);
                }
                List<T5> result5 = null;
                if (NextResult(dataReader))
                {
                    this.Context.InitMappingInfo<T5>();
                    result5 = await GetDataAsync<T5>(typeof(T5), dataReader);
                }
                List<T6> result6 = null;
                if (NextResult(dataReader))
                {
                    this.Context.InitMappingInfo<T6>();
                    result6 = await GetDataAsync<T6>(typeof(T6), dataReader);
                }
                List<T7> result7 = null;
                if (NextResult(dataReader))
                {
                    this.Context.InitMappingInfo<T7>();
                    result7 = await GetDataAsync<T7>(typeof(T7), dataReader);
                }
                builder.SqlQueryBuilder.Clear();
                if (this.Context.Ado.DataReaderParameters != null)
                {
                    foreach (IDataParameter item in this.Context.Ado.DataReaderParameters)
                    {
                        var parameter = parsmeterArray.FirstOrDefault(it => item.ParameterName.Substring(1) == it.ParameterName.Substring(1));
                        if (parameter != null)
                        {
                            parameter.Value = item.Value;
                        }
                    }
                    this.Context.Ado.DataReaderParameters = null;
                }
                this.GetDataAfter(sqlString, Parameters);
                return Tuple.Create<List<T>, List<T2>, List<T3>, List<T4>, List<T5>, List<T6>, List<T7>>(result, result2, result3, result4, result5, result6, result7);
            }
        }

        public virtual T SqlQuerySingle<T>(string sql, object parameters = null)
        {
            var result = SqlQuery<T>(sql, parameters);
            return result == null ? default(T) : result.FirstOrDefault();
        }
        public virtual T SqlQuerySingle<T>(string sql, params SugarParameter[] parameters)
        {
            var result = SqlQuery<T>(sql, parameters);
            return result == null ? default(T) : result.FirstOrDefault();
        }
        public virtual T SqlQuerySingle<T>(string sql, List<SugarParameter> parameters)
        {
            var result = SqlQuery<T>(sql, parameters);
            return result == null ? default(T) : result.FirstOrDefault();
        }


        public virtual async Task<T> SqlQuerySingleAsync<T>(string sql, object parameters = null)
        {
            var result = await SqlQueryAsync<T>(sql, parameters);
            return result == null ? default(T) : result.FirstOrDefault();
        }
        public virtual async Task<T> SqlQuerySingleAsync<T>(string sql, params SugarParameter[] parameters)
        {
            var result = await SqlQueryAsync<T>(sql, parameters);
            return result == null ? default(T) : result.FirstOrDefault();
        }
        public virtual async Task<T> SqlQuerySingleAsync<T>(string sql, List<SugarParameter> parameters)
        {
            var result = await SqlQueryAsync<T>(sql, parameters);
            return result == null ? default(T) : result.FirstOrDefault();
        }



        public virtual DataTable GetDataTable(string sql, params SugarParameter[] parameters)
        {
            var ds = GetDataSetAll(sql, parameters);
            if (ds.Tables.Count != 0 && ds.Tables.Count > 0) return ds.Tables[0];
            return new DataTable();
        }
        public virtual DataTable GetDataTable(string sql, object parameters)
        {
            return GetDataTable(sql, this.GetParameters(parameters));
        }
        public virtual DataTable GetDataTable(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return GetDataTable(sql);
            }
            else
            {
                return GetDataTable(sql, parameters.ToArray());
            }
        }


        public virtual async Task<DataTable> GetDataTableAsync(string sql, params SugarParameter[] parameters)
        {
            var ds = await GetDataSetAllAsync(sql, parameters);
            if (ds.Tables.Count != 0 && ds.Tables.Count > 0) return ds.Tables[0];
            return new DataTable();
        }
        public virtual Task<DataTable> GetDataTableAsync(string sql, object parameters)
        {
            return GetDataTableAsync(sql, this.GetParameters(parameters));
        }
        public virtual Task<DataTable> GetDataTableAsync(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return GetDataTableAsync(sql);
            }
            else
            {
                return GetDataTableAsync(sql, parameters.ToArray());
            }
        }


        public virtual DataSet GetDataSetAll(string sql, object parameters)
        {
            return GetDataSetAll(sql, this.GetParameters(parameters));
        }
        public virtual DataSet GetDataSetAll(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return GetDataSetAll(sql);
            }
            else
            {
                return GetDataSetAll(sql, parameters.ToArray());
            }
        }

        public virtual Task<DataSet> GetDataSetAllAsync(string sql, object parameters)
        {
            return GetDataSetAllAsync(sql, this.GetParameters(parameters));
        }
        public virtual Task<DataSet> GetDataSetAllAsync(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return GetDataSetAllAsync(sql);
            }
            else
            {
                return GetDataSetAllAsync(sql, parameters.ToArray());
            }
        }




        public virtual IDataReader GetDataReader(string sql, object parameters)
        {
            return GetDataReader(sql, this.GetParameters(parameters));
        }
        public virtual IDataReader GetDataReader(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return GetDataReader(sql);
            }
            else
            {
                return GetDataReader(sql, parameters.ToArray());
            }
        }
        public virtual Task<IDataReader> GetDataReaderAsync(string sql, object parameters)
        {
            return GetDataReaderAsync(sql, this.GetParameters(parameters));
        }
        public virtual Task<IDataReader> GetDataReaderAsync(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return GetDataReaderAsync(sql);
            }
            else
            {
                return GetDataReaderAsync(sql, parameters.ToArray());
            }
        }
        public virtual object GetScalar(string sql, object parameters)
        {
            return GetScalar(sql, this.GetParameters(parameters));
        }
        public virtual object GetScalar(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return GetScalar(sql);
            }
            else
            {
                return GetScalar(sql, parameters.ToArray());
            }
        }
        public virtual Task<object> GetScalarAsync(string sql, object parameters)
        {
            return GetScalarAsync(sql, this.GetParameters(parameters));
        }
        public virtual Task<object> GetScalarAsync(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return GetScalarAsync(sql);
            }
            else
            {
                return GetScalarAsync(sql, parameters.ToArray());
            }
        }
        public virtual int ExecuteCommand(string sql, object parameters)
        {
            return ExecuteCommand(sql, GetParameters(parameters));
        }
        public virtual int ExecuteCommand(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return ExecuteCommand(sql);
            }
            else
            {
                return ExecuteCommand(sql, parameters.ToArray());
            }
        }
        public Task<int> ExecuteCommandAsync(string sql, object parameters, CancellationToken cancellationToken) 
        {
            this.CancellationToken = CancellationToken;
            return ExecuteCommandAsync(sql,parameters);
        }
        public virtual Task<int> ExecuteCommandAsync(string sql, object parameters)
        {
            return ExecuteCommandAsync(sql, GetParameters(parameters));
        }
        public virtual Task<int> ExecuteCommandAsync(string sql, List<SugarParameter> parameters)
        {
            if (parameters == null)
            {
                return ExecuteCommandAsync(sql);
            }
            else
            {
                return ExecuteCommandAsync(sql, parameters.ToArray());
            }
        }
        #endregion

        #region  Helper
        public  virtual async Task<DbCommand> GetCommandAsync(string sql, SugarParameter[] parameters)
        {
            await Task.FromResult(0);
            throw new NotImplementedException();
        }
        public async Task CloseAsync()
        {
            if (this.Transaction != null)
            {
                this.Transaction = null;
            }
            if (this.Connection != null && this.Connection.State == ConnectionState.Open)
            {
                await (this.Connection as DbConnection).CloseAsync();
            }
            if (this.IsMasterSlaveSeparation && this.SlaveConnections.HasValue())
            {
                foreach (var slaveConnection in this.SlaveConnections)
                {
                    if (slaveConnection != null && slaveConnection.State == ConnectionState.Open)
                    {
                        await (slaveConnection as DbConnection).CloseAsync();
                    }
                }
            }
        }

        protected virtual void SugarCatch(Exception ex, string sql, SugarParameter[] parameters)
        {
            if (sql != null && sql.Contains("{year}{month}{day}")) 
            {
                Check.ExceptionEasy("need .SplitTable() message:" + ex.Message, "当前代码是否缺少 .SplitTable() ,可以看文档 [分表]  , 详细错误:" + ex.Message);
            }
        }
        public virtual void RemoveCancellationToken()
        {
            this.CancellationToken = null;
        }
        protected void Async()
        {
            if (this.Context.Root != null && this.Context.Root.AsyncId == null)
            {
                this.Context.Root.AsyncId = Guid.NewGuid(); ;
            }
        }
        protected bool NextResult(IDataReader dataReader)
        {
            try
            {
                return dataReader.NextResult();
            }
            catch  
            {
                return false;
            }
        }

        protected void ExecuteProcessingSQL(ref string sql,ref SugarParameter[] parameters)
        {
            var result = this.ProcessingEventStartingSQL(sql, parameters);
            sql = result.Key;
            parameters = result.Value;
        }
        public virtual void ExecuteBefore(string sql, SugarParameter[] parameters)
        {
            //if (this.Context.CurrentConnectionConfig.Debugger != null && this.Context.CurrentConnectionConfig.Debugger.EnableThreadSecurityValidation == true)
            //{

            //    var contextId = this.Context.ContextID.ToString();
            //    var processId = Thread.CurrentThread.ManagedThreadId.ToString();
            //    var cache = new ReflectionInoCacheService();
            //    if (!cache.ContainsKey<string>(contextId))
            //    {
            //        cache.Add(contextId, processId);
            //    }
            //    else
            //    {
            //        var cacheValue = cache.Get<string>(contextId);
            //        if (processId != cacheValue)
            //        {
            //            throw new SqlSugarException(this.Context, ErrorMessage.GetThrowMessage("Detection of SqlSugarClient cross-threading usage,a thread needs a new one", "检测到声名的SqlSugarClient跨线程使用，请检查是否静态、是否单例、或者IOC配置错误引起的，保证一个线程new出一个对象 ，具本Sql:") + sql, parameters);
            //        }
            //    }
            //}
            this.BeforeTime = DateTime.Now;
            if (this.IsEnableLogEvent)
            {
                Action<string, SugarParameter[]> action = LogEventStarting;
                if (action != null)
                {
                    if (parameters == null || parameters.Length == 0)
                    {
                        action(sql, new SugarParameter[] { });
                    }
                    else
                    {
                        action(sql, parameters);
                    }
                }
            }
        }
        public virtual void ExecuteAfter(string sql, SugarParameter[] parameters)
        {
            this.AfterTime = DateTime.Now;
            var hasParameter = parameters.HasValue();
            if (hasParameter)
            {
                foreach (var outputParameter in parameters.Where(it => it.Direction.IsIn(ParameterDirection.Output, ParameterDirection.InputOutput, ParameterDirection.ReturnValue)))
                {
                    var gobalOutputParamter = this.OutputParameters.FirstOrDefault(it => it.ParameterName == outputParameter.ParameterName);
                    if (gobalOutputParamter == null)
                    {//Oracle bug
                        gobalOutputParamter = this.OutputParameters.FirstOrDefault(it => it.ParameterName == outputParameter.ParameterName.TrimStart(outputParameter.ParameterName.First()));
                    }
                    outputParameter.Value = gobalOutputParamter.Value;
                    this.OutputParameters.Remove(gobalOutputParamter);
                }
            }
            if (this.IsEnableLogEvent)
            {
                Action<string, SugarParameter[]> action = LogEventCompleted;
                if (action != null)
                {
                    if (parameters == null || parameters.Length == 0)
                    {
                        action(sql, new SugarParameter[] { });
                    }
                    else
                    {
                        action(sql, parameters);
                    }
                }
            }
            if (this.OldCommandType != 0)
            {
                this.CommandType = this.OldCommandType;
                this.IsClearParameters = this.OldClearParameters;
                this.OldCommandType = 0;
                this.OldClearParameters = false;
            }
        }
        public virtual void GetDataBefore(string sql, SugarParameter[] parameters)
        {
            this.GetDataBeforeTime = DateTime.Now;
            if (this.IsEnableLogEvent)
            {
                Action<string, SugarParameter[]> action = OnGetDataReadering;
                if (action != null)
                {
                    if (parameters == null || parameters.Length == 0)
                    {
                        action(sql, new SugarParameter[] { });
                    }
                    else
                    {
                        action(sql, parameters);
                    }
                }
            }
        }
        public virtual void GetDataAfter(string sql, SugarParameter[] parameters)
        {
            this.GetDataAfterTime = DateTime.Now;
            if (this.IsEnableLogEvent)
            {
                Action<string, SugarParameter[], TimeSpan> action = OnGetDataReadered;
                if (action != null)
                {
                    if (parameters == null || parameters.Length == 0)
                    {
                        action(sql, new SugarParameter[] { }, GetDataExecutionTime);
                    }
                    else
                    {
                        action(sql, parameters, GetDataExecutionTime);
                    }
                }
            }
        }
        public virtual SugarParameter[] GetParameters(object parameters, PropertyInfo[] propertyInfo = null)
        {
            if (parameters == null) return null;
            return base.GetParameters(parameters, propertyInfo, this.SqlParameterKeyWord);
        }
        protected bool IsAutoClose()
        {
            return this.Context.CurrentConnectionConfig.IsAutoCloseConnection && this.Transaction == null;
        }
        protected bool IsMasterSlaveSeparation
        {
            get
            {
                return this.Context.CurrentConnectionConfig.SlaveConnectionConfigs.HasValue() && this.IsDisableMasterSlaveSeparation == false;
            }
        }
        protected void SetConnectionStart(string sql)
        {
            if (this.Transaction == null && this.IsMasterSlaveSeparation && IsRead(sql))
            {
                if (this.MasterConnection == null)
                {
                    this.MasterConnection = this.Connection;
                    this.MasterConnectionString = this.MasterConnection.ConnectionString;
                }
                var saves = this.Context.CurrentConnectionConfig.SlaveConnectionConfigs.Where(it => it.HitRate > 0).ToList();
                var currentIndex = UtilRandom.GetRandomIndex(saves.ToDictionary(it => saves.ToList().IndexOf(it), it => it.HitRate));
                var currentSaveConnection = saves[currentIndex];
                this.Connection = null;
                this.Context.CurrentConnectionConfig.ConnectionString = currentSaveConnection.ConnectionString;
                this.Connection = this.Connection;
                if (this.SlaveConnections.IsNullOrEmpty() || !this.SlaveConnections.Any(it => EqualsConnectionString(it.ConnectionString, this.Connection.ConnectionString)))
                {
                    if (this.SlaveConnections == null) this.SlaveConnections = new List<IDbConnection>();
                    this.SlaveConnections.Add(this.Connection);
                }
            }
            else if (this.Transaction == null && this.IsMasterSlaveSeparation&& this.MasterConnection == null) 
            {
                this.MasterConnection = this.Connection;
                this.MasterConnectionString = this.MasterConnection.ConnectionString;
            }
        }

        private bool EqualsConnectionString(string connectionString1, string connectionString2)
        {
            var connectionString1Array = connectionString1.Split(';');
            var connectionString2Array = connectionString2.Split(';');
            var result = connectionString1Array.Except(connectionString2Array);
            return result.Count() == 0;
        }
        private bool IsFormat(SugarParameter[] parameters)
        {
            return FormatSql != null && parameters != null && parameters.Length > 0;
        }

        protected void SetConnectionEnd(string sql)
        {
            if (this.IsMasterSlaveSeparation && IsRead(sql) && this.Transaction == null)
            {
                this.Connection = this.MasterConnection;
                this.Context.CurrentConnectionConfig.ConnectionString = this.MasterConnectionString;
            }
            this.Context.SugarActionType = SugarActionType.UnKnown;
        }

        private bool IsRead(string sql)
        {
            var sqlLower = sql.ToLower();
            var result = Regex.IsMatch(sqlLower, "[ ]*select[ ]") && !Regex.IsMatch(sqlLower, "[ ]*insert[ ]|[ ]*update[ ]|[ ]*delete[ ]");
            return result;
        }

        protected void ExecuteErrorEvent(string sql, SugarParameter[] parameters, Exception ex)
        {
            this.AfterTime = DateTime.Now;
            ErrorEvent(new SqlSugarException(this.Context, ex, sql, parameters));
        }
        protected void InitParameters(ref string sql, SugarParameter[] parameters)
        {
            this.SqlExecuteCount = 0;
            this.BeforeTime = DateTime.MinValue;
            this.AfterTime = DateTime.MinValue;
            if (parameters.HasValue())
            {
                foreach (var item in parameters)
                {
                    if (item.Value != null)
                    {
                        var type = item.Value.GetType();
                        if ((type != UtilConstants.ByteArrayType && type.IsArray&&item.IsArray==false) || type.FullName.IsCollectionsList()||type.IsIterator())
                        {
                            var newValues = new List<string>();
                            foreach (var inValute in item.Value as IEnumerable)
                            {
                                newValues.Add(inValute.ObjToString());
                            }
                            if (newValues.IsNullOrEmpty())
                            {
                                newValues.Add("-1");
                            }
                            if (item.ParameterName.Substring(0, 1) == ":")
                            {
                                sql = sql.Replace("@" + item.ParameterName.Substring(1), newValues.ToArray().ToJoinSqlInVals());
                            }
                            if (item.ParameterName.Substring(0, 1) != this.SqlParameterKeyWord && sql.ObjToString().Contains(this.SqlParameterKeyWord + item.ParameterName))
                            {
                                sql = sql.Replace(this.SqlParameterKeyWord + item.ParameterName, newValues.ToArray().ToJoinSqlInVals());
                            }
                            else if (item.Value!=null&&UtilMethods.IsNumberArray(item.Value.GetType()))
                            {
                                if (newValues.Any(it => it == "")) 
                                {
                                    newValues.RemoveAll(r => r == "");
                                    newValues.Add("null");
                                }
                                sql = sql.Replace(item.ParameterName, string.Join(",", newValues));
                            }
                            else
                            {
                                sql = sql.Replace(item.ParameterName, newValues.ToArray().ToJoinSqlInVals());
                            }
                            item.Value = DBNull.Value;
                        }
                    }
                    if (item.ParameterName != null && item.ParameterName.Contains(" ")) 
                    {
                        var oldName = item.ParameterName;
                        item.ParameterName = item.ParameterName.Replace(" ", "");
                        sql = sql.Replace(oldName, item.ParameterName);
                    }
                }
            }
        }

 

        protected List<TResult> GetData<TResult>(Type entityType, IDataReader dataReader)
        {
            List<TResult> result;
            if (entityType == UtilConstants.DynamicType)
            {
                result = this.Context.Utilities.DataReaderToExpandoObjectListNoUsing(dataReader) as List<TResult>;
            }
            else if (entityType == UtilConstants.ObjType)
            {
                result = this.Context.Utilities.DataReaderToExpandoObjectListNoUsing(dataReader).Select(it => ((TResult)(object)it)).ToList();
            }
            else if (entityType.IsAnonymousType()||StaticConfig.EnableAot)
            {
                if (StaticConfig.EnableAot&& entityType==UtilConstants.StringType)
                {
                    result = this.Context.Ado.DbBind.DataReaderToListNoUsing<TResult>(entityType, dataReader);
                }
                else
                {
                    result = this.Context.Utilities.DataReaderToListNoUsing<TResult>(dataReader);
                }
            }
            else
            {
                result = this.Context.Ado.DbBind.DataReaderToListNoUsing<TResult>(entityType, dataReader);
            }
            return result;
        }
        private async Task<List<TResult>> GetDataAsync<TResult>(Type entityType, IDataReader dataReader)
        {
            List<TResult> result;
            if (entityType == UtilConstants.DynamicType)
            {
                result =await this.Context.Utilities.DataReaderToExpandoObjectListAsyncNoUsing(dataReader) as List<TResult>;
            }
            else if (entityType == UtilConstants.ObjType)
            {
                var list = await this.Context.Utilities.DataReaderToExpandoObjectListAsyncNoUsing(dataReader);
                result = list.Select(it => ((TResult)(object)it)).ToList();
            }
            else if (entityType.IsAnonymousType() || StaticConfig.EnableAot)
            {
                if (StaticConfig.EnableAot && entityType == UtilConstants.StringType)
                {
                    result =  await this.Context.Ado.DbBind.DataReaderToListNoUsingAsync<TResult>(entityType, dataReader);
                }
                else
                {
                    result =await this.Context.Utilities.DataReaderToListAsyncNoUsing<TResult>(dataReader);
                }
            }
            else
            {
                result =await this.Context.Ado.DbBind.DataReaderToListNoUsingAsync<TResult>(entityType, dataReader);
            }
            return result;
        }
        #endregion
    }
}
