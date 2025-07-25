﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dm;
 

namespace SqlSugar
{
    public partial class DmProvider : AdoProvider
    {
        public DmProvider() {
            //this.FormatSql = sql =>
            //{
            //    sql = sql.Replace("+@", "+:");
            //    if (sql.HasValue() && sql.Contains("@"))
            //    {
            //        var exceptionalCaseInfo = Regex.Matches(sql, @"\'[^\=]*?\@.*?\'|[\.,\w]+\@[\.,\w]+ | [\.,\w]+\@[\.,\w]+|[\.,\w]+\@[\.,\w]+ |\d+\@\d|\@\@");
            //        if (exceptionalCaseInfo != null)
            //        {
            //            foreach (var item in exceptionalCaseInfo.Cast<Match>())
            //            {
            //                if (item.Value != null && item.Value.IndexOf(",") == 1 && Regex.IsMatch(item.Value, @"^ \,\@\w+$"))
            //                {
            //                    continue;
            //                }
            //                else if (item.Value != null && Regex.IsMatch(item.Value.Trim(), @"^\w+\,\@\w+\,$"))
            //                {
            //                    continue;
            //                }
            //                else if (item.Value != null && item.Value.ObjToString().Contains("||") && Regex.IsMatch(item.Value.Replace(" ", "").Trim(), @"\|\|@\w+\|\|"))
            //                {
            //                    continue;
            //                }
            //                else if (item.Value != null && Regex.IsMatch(item.Value.Replace(" ", "").Trim(), @"\(\@\w+\,"))
            //                {
            //                    continue;
            //                }
            //                else if (item.Value != null && item.Value.Contains("=") && Regex.IsMatch(item.Value, @"\w+ \@\w+[ ]{0,1}\=[ ]{0,1}\'"))
            //                {
            //                    continue;
            //                }
            //                sql = sql.Replace(item.Value, item.Value.Replace("@", UtilConstants.ReplaceKey));
            //            }
            //        }
            //        sql = sql.Replace("@", ":");
            //        sql = sql.Replace(UtilConstants.ReplaceKey, "@");
            //    }
            //    return sql;
            //};
        }
        public override string SqlParameterKeyWord
        {
            get
            {
                return ":";
            }
        }
        public override IDbConnection Connection
        {
            get
            {
                if (base._DbConnection == null)
                {
                    try
                    {
                        var npgsqlConnectionString = base.Context.CurrentConnectionConfig.ConnectionString;
                        base._DbConnection = new DmConnection(npgsqlConnectionString);
                    }
                    catch (Exception ex)
                    {
                        Check.Exception(true, ErrorMessage.ConnnectionOpen, ex.Message);
                    }
                }
                return base._DbConnection;
            }
            set
            {
                base._DbConnection = value;
            }
        }

        public override void BeginTran(string transactionName)
        {
            base.BeginTran();
        }
        /// <summary>
        /// Only SqlServer
        /// </summary>
        /// <param name="iso"></param>
        /// <param name="transactionName"></param>
        public override void BeginTran(IsolationLevel iso, string transactionName)
        {
            base.BeginTran(iso);
        }
        public override IDataAdapter GetAdapter()
        {
            return new MyDmDataAdapter();
        }
        public override DbCommand GetCommand(string sql, SugarParameter[] parameters)
        {
            sql = ReplaceKeyWordParameterName(sql, parameters);
            DmCommand sqlCommand = new DmCommand(sql, (DmConnection)this.Connection);
            sqlCommand.CommandType = this.CommandType;
            sqlCommand.CommandTimeout = this.CommandTimeOut;
            if (this.Transaction != null)
            {
                sqlCommand.Transaction = (DmTransaction)this.Transaction;
            }
            if (parameters.HasValue())
            {
                IDataParameter[] ipars = ToIDbDataParameter(parameters);
                sqlCommand.Parameters.AddRange((DmParameter[])ipars);
            }
            CheckConnection();
            return sqlCommand;
        }
        public override void SetCommandToAdapter(IDataAdapter dataAdapter, DbCommand command)
        {
            ((MyDmDataAdapter)dataAdapter).SelectCommand = (DmCommand)command;
        }
        /// <summary>
        /// if mysql return MySqlParameter[] pars
        /// if sqlerver return SqlParameter[] pars ...
        /// </summary>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public override IDataParameter[] ToIDbDataParameter(params SugarParameter[] parameters)
        {
            if (parameters == null || parameters.Length == 0) return null;
            DmParameter[] result = new DmParameter[parameters.Length];
            int index = 0;
            var isVarchar = this.Context.IsVarchar();
            foreach (var parameter in parameters)
            {
                if (parameter.Value == null) parameter.Value = DBNull.Value;
                var sqlParameter = new DmParameter();
                sqlParameter.ParameterName = parameter.ParameterName;
                sqlParameter.Size = parameter.Size;
                sqlParameter.Value = parameter.Value;
                sqlParameter.DbType = parameter.DbType;
                if (sqlParameter.ParameterName[0] == '@')
                {
                    sqlParameter.ParameterName = ':' + sqlParameter.ParameterName.Substring(1, sqlParameter.ParameterName.Length - 1);
                }
                if (sqlParameter.DbType == System.Data.DbType.Guid)
                {
                    sqlParameter.DbType = System.Data.DbType.String;
                    if (sqlParameter.Value != DBNull.Value)
                        sqlParameter.Value = sqlParameter.Value.ToString();
                }
                if (parameter.IsClob)
                {
                    sqlParameter.DmSqlType = DmDbType.Clob;
                    sqlParameter.Value = parameter.Value;
                }
                if (parameter.IsNClob)
                {
                    sqlParameter.DmSqlType = DmDbType.Clob;
                    sqlParameter.Value = parameter.Value;
                }
                if (parameter.Direction == 0)
                {
                    parameter.Direction = ParameterDirection.Input;
                }
                sqlParameter.Direction = parameter.Direction;
                result[index] = sqlParameter;
                if (sqlParameter.Direction.IsIn(ParameterDirection.Output, ParameterDirection.InputOutput, ParameterDirection.ReturnValue))
                {
                    if (this.OutputParameters == null) this.OutputParameters = new List<IDataParameter>();
                    this.OutputParameters.RemoveAll(it => it.ParameterName == sqlParameter.ParameterName);
                    this.OutputParameters.Add(sqlParameter);
                }
                if (isVarchar && sqlParameter.DbType == System.Data.DbType.String)
                {
                    sqlParameter.DbType = System.Data.DbType.AnsiString;
                }
                if (parameter.IsRefCursor)
                {
                    sqlParameter.DmSqlType = DmDbType.Cursor;
                }
                if (IsSpOutPutParameter(sqlParameter))
                {
                    sqlParameter.ParameterName = sqlParameter.ParameterName.Replace("@", ":");
                }
                if (parameter.TypeName.HasValue()) 
                {
                    sqlParameter.DmSqlTypeName = parameter.TypeName;
                }
                if (parameter.CustomDbType is DmDbType dmDbType) 
                {
                    sqlParameter.DmSqlType = dmDbType;
                }
                ++index;
            }
            return result;
        }

        private bool IsSpOutPutParameter(DmParameter sqlParameter)
        {
            return sqlParameter.Direction == ParameterDirection.Output && this.CommandType == CommandType.StoredProcedure;
        }

        private static string[] KeyWord =new string []{ ":asc", "@asc", ":desc", "@desc","@month", ":month", ":day","@day","@group", ":group",":index", "@index", "@order", ":order", "@user", "@level", ":user", ":level",":type","@type", ":year", "@year" };
        private static string ReplaceKeyWordParameterName(string sql, SugarParameter[] parameters)
        {
            sql = ReplaceKeyWordWithAd(sql, parameters);
            if (parameters.HasValue() && parameters.Count(it => it.ParameterName.ToLower().IsIn(KeyWord))>0)
            {
                int i = 0;
                foreach (var Parameter in parameters.OrderByDescending(it=>it.ParameterName.Length))
                {
                    if (Parameter.ParameterName != null && Parameter.ParameterName.ToLower().IsContainsIn(KeyWord))
                    {
                        var newName = ":p" + i + 100;
                        sql = Regex.Replace(sql, Parameter.ParameterName, newName, RegexOptions.IgnoreCase);
                        Parameter.ParameterName = newName;
                        i++;
                    }
                }
            } 
            return sql;
        }

        private static string ReplaceKeyWordWithAd(string sql, SugarParameter[] parameters)
        {
            if (parameters != null && sql != null && sql.Contains("@"))
            {
                foreach (var item in parameters.OrderByDescending(it => it.ParameterName.Length))
                {
                    if (item.ParameterName.StartsWith("@"))
                    {
                        item.ParameterName = ":" + item.ParameterName.TrimStart('@');
                    }
                    sql = Regex.Replace(sql, "@" + item.ParameterName.TrimStart(':'), item.ParameterName, RegexOptions.IgnoreCase);
                }
            }

            return sql;
        }
        public override Action<SqlSugarException> ErrorEvent => it =>
        {
            if (base.ErrorEvent != null)
            {
                base.ErrorEvent(it);
            }
            if (it.Message != null && it.Message.Contains("Detail redacted as it may contain sensitive data."))
            {
                Check.ExceptionEasy(it.Message, $"错误：可能是字段太小超出,详细错误：{it.Message} ");
            }
        };
    }
}
