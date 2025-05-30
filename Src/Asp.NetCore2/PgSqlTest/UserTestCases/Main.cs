﻿using PgSqlTest.UserTestCases;
using SqlSugar;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OrmTest
{
    public partial class NewUnitTest
    {
       public static  SqlSugarClient Db=> new SqlSugarClient(new ConnectionConfig()
        {
            DbType = DbType.PostgreSQL,
            ConnectionString = Config.ConnectionString,
            InitKeyType = InitKeyType.Attribute,
            IsAutoCloseConnection = true,
            AopEvents = new AopEvents
            {
                OnLogExecuting = (sql, p) =>
                {
                    Console.WriteLine(sql);
                    Console.WriteLine(string.Join(",", p?.Select(it => it.ParameterName + ":" + it.Value)));
                }
            }
        });

        public static void RestData()
        {
            Db.DbMaintenance.TruncateTable<Order>();
            Db.DbMaintenance.TruncateTable<OrderItem>();
        }
        public static void Init()
        {
            Unitsdfasyss.Init();
            Unitdfaysss.Init();
            Unitadfafasfa1.Init();
            Unita1ddys.Init();
            Unit1sdgsaaysdfa.Init();
            UnitBulkMergeaa.Init();
            Unitadsfayasdfaaay.Init();
            Unitafdafas.Init();
            Unitadfaf2s.Init();
            UnitWeek.Init();
            UnitTestOneToOne.Init();
            Unitadfafafasd.Init();
            UnitSubToList.Init();
            Unit001.Init();
            Bulk();
            CodeFirst();
            Updateable();
            Json();
            Ado();
            Queryable();
            QueryableAsync();
            Thread();
            Thread2();
            Thread3();
        }
    }
}
