using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using OfflineCvBioDataGenerator.Data;
using OfflineCvBioDataGenerator.Models;

namespace OfflineCvBioDataGenerator.Repositories
{
    /// <summary>CRUD for the Experience rows belonging to a client.</summary>
    public class ExperienceRepository
    {
        private const string Columns = "Id, ClientId, JobTitle, Company, Duration";

        public List<Experience> GetByClientId(int clientId)
        {
            using IDbConnection conn = DatabaseHelper.GetConnection();
            return conn.Query<Experience>(
                $"SELECT {Columns} FROM Experience WHERE ClientId = @ClientId ORDER BY Id",
                new { ClientId = clientId }).ToList();
        }

        public int Add(Experience experience)
        {
            if (experience == null) throw new ArgumentNullException(nameof(experience));

            using IDbConnection conn = DatabaseHelper.GetConnection();
            const string sql = @"INSERT INTO Experience (ClientId, JobTitle, Company, Duration)
                                 VALUES (@ClientId, @JobTitle, @Company, @Duration);
                                 SELECT last_insert_rowid();";
            return conn.ExecuteScalar<int>(sql, experience);
        }

        /// <summary>Adds several rows in one transaction - avoids opening a connection per row.</summary>
        public void AddRange(IEnumerable<Experience> rows)
        {
            if (rows == null) return;
            var list = rows.ToList();
            if (list.Count == 0) return;

            using IDbConnection conn = DatabaseHelper.GetConnection();
            using IDbTransaction tx = conn.BeginTransaction();

            const string sql = @"INSERT INTO Experience (ClientId, JobTitle, Company, Duration)
                                 VALUES (@ClientId, @JobTitle, @Company, @Duration)";
            foreach (var row in list)
            {
                conn.Execute(sql, row, tx);
            }

            tx.Commit();
        }

        public void DeleteByClientId(int clientId)
        {
            using IDbConnection conn = DatabaseHelper.GetConnection();
            conn.Execute("DELETE FROM Experience WHERE ClientId=@ClientId", new { ClientId = clientId });
        }
    }
}
