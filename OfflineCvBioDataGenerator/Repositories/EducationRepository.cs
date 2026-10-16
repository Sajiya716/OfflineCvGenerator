using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using OfflineCvBioDataGenerator.Data;
using OfflineCvBioDataGenerator.Models;

namespace OfflineCvBioDataGenerator.Repositories
{
    /// <summary>CRUD for the Education rows belonging to a client.</summary>
    public class EducationRepository
    {
        private const string Columns = "Id, ClientId, Qualification, Institute, Year, Results";

        public List<Education> GetByClientId(int clientId)
        {
            using IDbConnection conn = DatabaseHelper.GetConnection();
            return conn.Query<Education>(
                $"SELECT {Columns} FROM Education WHERE ClientId = @ClientId ORDER BY Id",
                new { ClientId = clientId }).ToList();
        }

        public int Add(Education education)
        {
            if (education == null) throw new ArgumentNullException(nameof(education));

            using IDbConnection conn = DatabaseHelper.GetConnection();
            const string sql = @"INSERT INTO Education (ClientId, Qualification, Institute, Year, Results)
                                 VALUES (@ClientId, @Qualification, @Institute, @Year, @Results);
                                 SELECT last_insert_rowid();";
            return conn.ExecuteScalar<int>(sql, education);
        }

        /// <summary>Adds several rows in one transaction - avoids opening a connection per row.</summary>
        public void AddRange(IEnumerable<Education> rows)
        {
            if (rows == null) return;
            var list = rows.ToList();
            if (list.Count == 0) return;

            using IDbConnection conn = DatabaseHelper.GetConnection();
            using IDbTransaction tx = conn.BeginTransaction();

            const string sql = @"INSERT INTO Education (ClientId, Qualification, Institute, Year, Results)
                                 VALUES (@ClientId, @Qualification, @Institute, @Year, @Results)";
            foreach (var row in list)
            {
                conn.Execute(sql, row, tx);
            }

            tx.Commit();
        }

        public void DeleteByClientId(int clientId)
        {
            using IDbConnection conn = DatabaseHelper.GetConnection();
            conn.Execute("DELETE FROM Education WHERE ClientId=@ClientId", new { ClientId = clientId });
        }
    }
}
