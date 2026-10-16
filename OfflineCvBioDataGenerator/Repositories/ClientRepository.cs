using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using OfflineCvBioDataGenerator.Data;
using OfflineCvBioDataGenerator.Models;

namespace OfflineCvBioDataGenerator.Repositories
{
    /// <summary>
    /// CRUD for Clients (and the child Education/Experience rows, which are always
    /// written together with the client so the data never ends up half saved).
    ///
    /// Every method opens its own connection and disposes it via 'using', so no SQLite
    /// file handle is ever left open after a call returns.
    /// </summary>
    public class ClientRepository
    {
        public List<Client> GetAllClients()
        {
            using IDbConnection conn = DatabaseHelper.GetConnection();
            return conn.Query<Client>(
                "SELECT Id, FullName, Address, Phone, Email, DOB, NIC, PhotoPath " +
                "FROM Clients ORDER BY FullName COLLATE NOCASE").ToList();
        }

        public Client? GetClientById(int id)
        {
            using IDbConnection conn = DatabaseHelper.GetConnection();
            return conn.QueryFirstOrDefault<Client>(
                "SELECT Id, FullName, Address, Phone, Email, DOB, NIC, PhotoPath " +
                "FROM Clients WHERE Id = @Id",
                new { Id = id });
        }

        /// <summary>Returns true when another client already uses this NIC.</summary>
        public bool NicExists(string nic, int excludeClientId = 0)
        {
            if (string.IsNullOrWhiteSpace(nic)) return false;

            using IDbConnection conn = DatabaseHelper.GetConnection();
            return NicExists(conn, null, nic, excludeClientId);
        }

        /// <summary>
        /// Inserts a brand new client. Throws <see cref="InvalidOperationException"/>
        /// when the NIC is already taken, so duplicates are impossible.
        /// </summary>
        public int AddClient(Client client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            using IDbConnection conn = DatabaseHelper.GetConnection();
            using IDbTransaction tx = conn.BeginTransaction();

            if (NicExists(conn, tx, client.NIC, 0))
                throw new InvalidOperationException($"A client with NIC '{client.NIC.Trim()}' already exists.");

            int newId = conn.ExecuteScalar<int>(InsertSql, client, tx);
            tx.Commit();
            return newId;
        }

        /// <summary>Updates an existing client. Rejects a NIC that belongs to someone else.</summary>
        public void UpdateClient(Client client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (client.Id <= 0)
                throw new ArgumentOutOfRangeException(nameof(client), "Client.Id must be set before updating.");

            using IDbConnection conn = DatabaseHelper.GetConnection();
            using IDbTransaction tx = conn.BeginTransaction();

            if (NicExists(conn, tx, client.NIC, client.Id))
                throw new InvalidOperationException($"A client with NIC '{client.NIC.Trim()}' already exists.");

            int affected = conn.Execute(UpdateSql, client, tx);
            tx.Commit();

            if (affected == 0)
                throw new InvalidOperationException("The client no longer exists (it may have been deleted already).");
        }

        /// <summary>Deletes a client and every child row in a single transaction.</summary>
        public void DeleteClient(int id)
        {
            using IDbConnection conn = DatabaseHelper.GetConnection();
            using IDbTransaction tx = conn.BeginTransaction();

            conn.Execute("DELETE FROM Education WHERE ClientId=@Id", new { Id = id }, tx);
            conn.Execute("DELETE FROM Experience WHERE ClientId=@Id", new { Id = id }, tx);
            int affected = conn.Execute("DELETE FROM Clients WHERE Id=@Id", new { Id = id }, tx);

            tx.Commit();

            if (affected == 0)
                throw new InvalidOperationException("The client no longer exists (it may have been deleted already).");
        }

        /// <summary>
        /// Saves a client together with its full education and experience lists.
        /// Existing child rows are replaced, all inside one transaction, so a failure
        /// leaves the previous data untouched instead of half overwritten.
        /// </summary>
        public Client Save(Client client, IEnumerable<Education>? education, IEnumerable<Experience>? experience)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            using IDbConnection conn = DatabaseHelper.GetConnection();
            using IDbTransaction tx = conn.BeginTransaction();

            bool isNew = client.Id <= 0;

            if (isNew)
            {
                if (NicExists(conn, tx, client.NIC, 0))
                    throw new InvalidOperationException($"A client with NIC '{client.NIC.Trim()}' already exists.");

                client.Id = conn.ExecuteScalar<int>(InsertSql, client, tx);
            }
            else
            {
                if (NicExists(conn, tx, client.NIC, client.Id))
                    throw new InvalidOperationException($"A client with NIC '{client.NIC.Trim()}' already exists.");

                int affected = conn.Execute(UpdateSql, client, tx);
                if (affected == 0)
                    throw new InvalidOperationException("The client no longer exists (it may have been deleted already).");
            }

            ReplaceEducation(conn, tx, client.Id, education);
            ReplaceExperience(conn, tx, client.Id, experience);

            tx.Commit();
            return client;
        }

        /// <summary>Searches by name or NIC. LIKE wildcards in the input are escaped.</summary>
        public List<Client> SearchClients(string searchTerm)
        {
            using IDbConnection conn = DatabaseHelper.GetConnection();

            const string sql = @"SELECT Id, FullName, Address, Phone, Email, DOB, NIC, PhotoPath
                                 FROM Clients
                                 WHERE FullName LIKE @Search ESCAPE '\'
                                    OR NIC     LIKE @Search ESCAPE '\'
                                 ORDER BY FullName COLLATE NOCASE";

            return conn.Query<Client>(sql, new { Search = SearchTerm.Contains(searchTerm) }).ToList();
        }

        // ------------------------------------------------------------------ constants

        private const string InsertSql =
            @"INSERT INTO Clients (FullName, Address, Phone, Email, DOB, NIC, PhotoPath)
              VALUES (@FullName, @Address, @Phone, @Email, @DOB, @NIC, @PhotoPath);
              SELECT last_insert_rowid();";

        private const string UpdateSql =
            @"UPDATE Clients
              SET FullName=@FullName, Address=@Address, Phone=@Phone,
                  Email=@Email, DOB=@DOB, NIC=@NIC, PhotoPath=@PhotoPath
              WHERE Id=@Id";

        // ------------------------------------------------------------------ helpers

        private static bool NicExists(IDbConnection conn, IDbTransaction? tx, string nic, int excludeClientId)
        {
            if (string.IsNullOrWhiteSpace(nic)) return false;

            int count = conn.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM Clients WHERE TRIM(NIC) = @Nic AND Id <> @ExcludeId",
                new { Nic = nic.Trim(), ExcludeId = excludeClientId },
                tx);
            return count > 0;
        }

        private static void ReplaceEducation(IDbConnection conn, IDbTransaction tx, int clientId,
            IEnumerable<Education>? rows)
        {
            // A null list means "leave the existing rows alone"; an empty list means
            // "the operator removed them all".
            if (rows == null) return;

            conn.Execute("DELETE FROM Education WHERE ClientId=@ClientId", new { ClientId = clientId }, tx);

            const string sql = @"INSERT INTO Education (ClientId, Qualification, Institute, Year, Results)
                                 VALUES (@ClientId, @Qualification, @Institute, @Year, @Results)";

            foreach (var row in rows)
            {
                row.ClientId = clientId;
                conn.Execute(sql, row, tx);
            }
        }

        private static void ReplaceExperience(IDbConnection conn, IDbTransaction tx, int clientId,
            IEnumerable<Experience>? rows)
        {
            if (rows == null) return;

            conn.Execute("DELETE FROM Experience WHERE ClientId=@ClientId", new { ClientId = clientId }, tx);

            const string sql = @"INSERT INTO Experience (ClientId, JobTitle, Company, Duration)
                                 VALUES (@ClientId, @JobTitle, @Company, @Duration)";

            foreach (var row in rows)
            {
                row.ClientId = clientId;
                conn.Execute(sql, row, tx);
            }
        }
    }
}
