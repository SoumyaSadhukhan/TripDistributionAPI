using System;
using System.Data;
using Microsoft.Data.SqlClient;
using Dapper;

namespace TripDistribution.LoadTesting;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== TripDistribution Database Manager & Verification Engine ===");
        var connStr = "Data Source=localhost;Initial Catalog=SPLIT_BILL_DB;Integrated Security=True;Persist Security Info=False;Pooling=True;Encrypt=False;TrustServerCertificate=True;Connect Timeout=30;";
        
        try
        {
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            Console.WriteLine("[DB] Successfully connected to SPLIT_BILL_DB on localhost.");

            var action = args.Length > 0 ? args[0].ToLowerInvariant() : "inspect";

            if (action == "reset")
            {
                // 1. Attempt Backup
                try
                {
                    var backupPath = @"C:\My_Project\SPLIT_BILL_DB_dev_backup.bak";
                    Console.WriteLine($"[BACKUP] Attempting database backup to '{backupPath}'...");
                    await conn.ExecuteAsync($"BACKUP DATABASE [SPLIT_BILL_DB] TO DISK = '{backupPath}' WITH FORMAT, INIT;");
                    Console.WriteLine("[BACKUP] Database backup completed successfully.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BACKUP NOTE] Native backup skipped: {ex.Message}");
                }

                // 2. Clear records in strict dependency order
                Console.WriteLine("[RESET] Deleting application data respecting dependencies...");
                var tablesToClear = new[]
                {
                    "ExpenseSplits",
                    "Expenses",
                    "SettlementTransactions",
                    "TripCollaborators",
                    "Persons",
                    "Groups",
                    "Trips",
                    "FriendConnections",
                    "Notifications",
                    "ActivityLogs",
                    "OtpVerification",
                    "UserTokens",
                    "Users"
                };

                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        foreach (var tbl in tablesToClear)
                        {
                            var count = await conn.ExecuteAsync($"DELETE FROM [{tbl}]", transaction: trans);
                            Console.WriteLine($"  - Deleted {count} rows from [{tbl}]");
                        }
                        trans.Commit();
                        Console.WriteLine("[RESET] Reset transaction committed successfully.");
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        Console.WriteLine($"[RESET ERROR] Deletion failed: {ex.Message}");
                        return 1;
                    }
                }
            }

            // 3. Status inspection
            Console.WriteLine("[DB] Inspecting table row counts:");
            var tables = await conn.QueryAsync<string>("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME");
            int totalRows = 0;
            foreach (var t in tables)
            {
                var count = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM [{t}]");
                Console.WriteLine($"  - {t}: {count} rows");
                totalRows += count;
            }

            Console.WriteLine($"[DB] Total rows across all tables: {totalRows}");

            Console.WriteLine("\n=== DETAILED RECORD INSPECTION ===");
            Console.WriteLine("\n[TABLE: Users]");
            var users = await conn.QueryAsync("SELECT user_id, full_name, email, phone_number, password_hash, created_at FROM Users");
            foreach (var u in users)
            {
                Console.WriteLine($"  - User: {u.full_name} | Email: {u.email} | Phone: {u.phone_number} | HashPrefix: {((string)u.password_hash).Substring(0, 25)}... | ID: {u.user_id}");
            }

            Console.WriteLine("\n[TABLE: Trips]");
            var trips = await conn.QueryAsync("SELECT trip_id, name, description, user_id, created_at FROM Trips");
            foreach (var tr in trips)
            {
                Console.WriteLine($"  - Trip: '{tr.name}' | Desc: '{tr.description}' | Owner ID: {tr.user_id} | Trip ID: {tr.trip_id}");
            }

            Console.WriteLine("\n[TABLE: Expenses]");
            var exps = await conn.QueryAsync("SELECT expense_id, trip_id, description, amount, paid_by_person_id, split_type FROM Expenses");
            foreach (var ep in exps)
            {
                Console.WriteLine($"  - Expense: '{ep.description}' | Amount: INR {ep.amount} | SplitType: {ep.split_type} | Payer: {ep.paid_by_person_id} | ID: {ep.expense_id}");
            }

            Console.WriteLine("\n[TABLE: ExpenseSplits]");
            var splits = await conn.QueryAsync("SELECT split_id, expense_id, person_id, amount FROM ExpenseSplits");
            foreach (var sp in splits)
            {
                Console.WriteLine($"  - Split ID: {sp.split_id} | Expense ID: {sp.expense_id} | Person ID: {sp.person_id} | Amount: INR {sp.amount}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB ERROR] {ex.Message}");
            return 1;
        }
    }
}
