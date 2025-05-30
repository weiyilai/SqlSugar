﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace SqlSugar
{
    public class KdbndpInserttable<T> : InsertableProvider<T> where T : class, new()
    {
        public override int ExecuteReturnIdentity()
        {
            InsertBuilder.IsReturnIdentity = true;
            PreToSql();
            string sql = InsertBuilder.ToSqlString().Replace("$PrimaryKey", this.SqlBuilder.GetTranslationColumnName(GetIdentityKeys().FirstOrDefault() ?? ""));
            RestoreMapping();
            sql = GetSql(sql);
            AutoRemoveDataCache();
            var result = Ado.GetScalar(sql, InsertBuilder.Parameters == null ? null : InsertBuilder.Parameters.ToArray()).ObjToInt();
            return result;
        }


        public override async Task<int> ExecuteReturnIdentityAsync()
        {
            InsertBuilder.IsReturnIdentity = true;
            PreToSql();
            string sql = InsertBuilder.ToSqlString().Replace("$PrimaryKey", this.SqlBuilder.GetTranslationColumnName(GetIdentityKeys().FirstOrDefault()??""));
            RestoreMapping();
            sql = GetSql(sql);
            AutoRemoveDataCache();
            var obj = await Ado.GetScalarAsync(sql, InsertBuilder.Parameters == null ? null : InsertBuilder.Parameters.ToArray());
            var result = obj.ObjToInt();
            return result;
        }
        public override KeyValuePair<string, List<SugarParameter>> ToSql()
        {
            var result= base.ToSql();
            if (GetPrimaryKeys()?.Any() == true)
            {
                return new KeyValuePair<string, List<SugarParameter>>(result.Key.Replace("$PrimaryKey", GetPrimaryKeys().FirstOrDefault()), result.Value);
            }
            else 
            {
                return new KeyValuePair<string, List<SugarParameter>>(result.Key.Replace(" returning $PrimaryKey", ""), result.Value);
            }
        }

        public override long ExecuteReturnBigIdentity()
        {
            InsertBuilder.IsReturnIdentity = true;
            PreToSql();
            string sql = InsertBuilder.ToSqlString().Replace("$PrimaryKey", this.SqlBuilder.GetTranslationColumnName(GetIdentityKeys().FirstOrDefault()??""));
            RestoreMapping();
            sql = GetSql(sql);
            AutoRemoveDataCache();
            var result = Convert.ToInt64(Ado.GetScalar(sql, InsertBuilder.Parameters == null ? null : InsertBuilder.Parameters.ToArray()) ?? "0");
            return result;
        }
        public override async Task<long> ExecuteReturnBigIdentityAsync()
        {
            InsertBuilder.IsReturnIdentity = true;
            PreToSql();
            string sql = InsertBuilder.ToSqlString().Replace("$PrimaryKey", this.SqlBuilder.GetTranslationColumnName(GetIdentityKeys().FirstOrDefault() ?? ""));
            RestoreMapping();
            sql = GetSql(sql);
            AutoRemoveDataCache();
            var result = Convert.ToInt64(await Ado.GetScalarAsync(sql, InsertBuilder.Parameters == null ? null : InsertBuilder.Parameters.ToArray()) ?? "0");
            return result;
        }

        public override bool ExecuteCommandIdentityIntoEntity()
        {
            var result = InsertObjs.First();
            var identityKeys = GetIdentityKeys();
            if (identityKeys.Count == 0) { return this.ExecuteCommand() > 0; }
            var idValue = ExecuteReturnBigIdentity();
            Check.Exception(identityKeys.Count > 1, "ExecuteCommandIdentityIntoEntity does not support multiple identity keys");
            var identityKey = identityKeys.First();
            object setValue = 0;
            if (idValue > int.MaxValue)
                setValue = idValue;
            else
                setValue = Convert.ToInt32(idValue);
            var propertyName = this.Context.EntityMaintenance.GetPropertyName<T>(identityKey);
            typeof(T).GetProperties().First(t => t.Name.ToUpper() == propertyName.ToUpper()).SetValue(result, setValue, null);
            return idValue > 0;
        }

        private string GetSql(string sql)
        {
            if (GetIdentityKeys().FirstOrDefault() == null)
            { 
                sql = sql.Replace("returning \"\"", "");
                var id = this.Context.DbMaintenance.GetIsIdentities(this.Context.EntityMaintenance.GetTableName(this.InsertBuilder.GetTableNameString)).FirstOrDefault();
                if (id != null)
                {
                    sql = sql.TrimEnd().TrimEnd(';')+ " returning " + this.SqlBuilder.GetTranslationColumnName(id) ;
                }
            }

            return sql;
        }
    }
}
