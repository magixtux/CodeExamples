﻿using Core;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;

namespace DataAccess
{
    public class DbUserWithTransactions
    {
        private string CONNECTION_STRING = ConfigurationManager.ConnectionStrings["MyConnectionString4"].ConnectionString;
        /// <summary>
        /// This method implements retrying of failed transactions
        /// </summary>
        /// <param name="p"></param>
        /// <param name="amount"></param>
        /// <param name="callback"></param>
        public void RetryingWithdraw(Account p, decimal amount, Action<string> callback)
        {
            TransactionOptions options = new TransactionOptions();
            //Which isolation level?
            options.IsolationLevel = IsolationLevel.RepeatableRead;
            //options.IsolationLevel = IsolationLevel.Serializable;
            int currentRetries = 3;
            bool success = false;
            while (currentRetries >= 0 && !success)
            {
                    using (TransactionScope scope = new TransactionScope(TransactionScopeOption.RequiresNew, options))
                    {
                    try
                    {
                        using (SqlConnection conn = new SqlConnection(CONNECTION_STRING))
                        {   //Dispose will call Close()
                            conn.Open();
                            decimal currentBalance = decimal.MinValue;

                            using (SqlCommand cmd = conn.CreateCommand())
                            {

                                cmd.CommandText = "SELECT Balance FROM Account WHERE Id=@id";
                                cmd.Parameters.AddWithValue("id", p.Id);
                                currentBalance = (decimal)cmd.ExecuteScalar();
                                callback(Transaction.Current.TransactionInformation.LocalIdentifier + " Retrieved current balance as " + currentBalance);
                            }

                            if (currentBalance >= amount)
                            {
                                using (SqlCommand cmd = conn.CreateCommand())
                                {
                                    Thread.Sleep(2000);
                                    cmd.CommandText = "UPDATE [Account] SET Balance=Balance-@balance WHERE Id=@id";
                                    cmd.Parameters.AddWithValue("id", p.Id);
                                    cmd.Parameters.AddWithValue("balance", amount);
                                    cmd.ExecuteNonQuery();
                                    callback(Transaction.Current.TransactionInformation.LocalIdentifier + " Updated balance to " + (p.Balance - amount).ToString());
                                }
                                scope.Complete();
                                success = true;
                            }
                            else
                            {
                                callback(Transaction.Current.TransactionInformation.LocalIdentifier + " Returned : Insufficient funds...");
                                return;
                            }
                        }
                    }
                    catch (SqlException sqle)
                    {
                        if (sqle.Number != 1205)
                        {
                            throw;
                        }
                        currentRetries--;
                        if (currentRetries == 0)
                            throw;

                        callback("Deadlock detected in \""+ Transaction.Current.TransactionInformation.LocalIdentifier + "\" Retrying transaction");

                    }
                }
               


            }
        }
        public void Withdraw(Account p, decimal amount, Action<string> callback)
        {
            TransactionOptions options = new TransactionOptions();
            //Which isolation level?
            options.IsolationLevel = IsolationLevel.RepeatableRead;
            //Alternative to TransactionScope is connection.BeginTransaction()
            using (TransactionScope scope = new TransactionScope(TransactionScopeOption.RequiresNew, options))
            {
                using (SqlConnection conn = new SqlConnection(CONNECTION_STRING))
                {   //Dispose will call Close()
                    decimal currentBalance = decimal.MinValue;
                    using (SqlCommand cmd = conn.CreateCommand())
                    {
                        
                        conn.Open();
                        cmd.CommandText = "SELECT Balance FROM Account WHERE Id=@id";
                        cmd.Parameters.AddWithValue("id", p.Id);
                        currentBalance = (decimal)cmd.ExecuteScalar();
                        callback("Retrieved current balance as " + currentBalance);
                        Thread.Sleep(2000);
                    }

                    if (currentBalance >= amount)
                    {
                        using (SqlCommand cmd = conn.CreateCommand())
                        {
                            
                            cmd.CommandText = "UPDATE [Account] SET Balance=Balance-@balance WHERE Id=@id";
                            cmd.Parameters.AddWithValue("id", p.Id);
                            cmd.Parameters.AddWithValue("balance", amount);
                            cmd.ExecuteNonQuery();
                            callback("Updated balance to " + (p.Balance - amount).ToString());
                        }
                    }
                    else
                    {
                        callback("Insufficient funds...");
                        return;
                    }

                }
                scope.Complete();
            }
        }
        //Alternative way of ADOing https://docs.microsoft.com/en-us/dotnet/framework/data/adonet/sql/linq/walkthrough-simple-object-model-and-query-csharp
        //Will return n..* rows per account per user! --> each row represents an acount, but carries the user information
        //If no account exists on the found user, the account values are NULL (LEFT JOIN)
        //https://www.w3schools.com/sql/sql_join_left.asp
        public IEnumerable<User> GetAllUsersWithAccounts()
        {
            List<User> foundUsers = new List<User>();
            using (SqlConnection conn = new SqlConnection(CONNECTION_STRING))
            using (SqlCommand cmd = conn.CreateCommand())
            {
                conn.Open();
                cmd.CommandText = "SELECT * FROM [User] u LEFT JOIN Account a ON u.Id=a.UserId";
                var rdr = cmd.ExecuteReader();
                User currentUser = null;
                while (rdr.Read())
                {
                    if (currentUser != null && currentUser.Id == (int)rdr["Id"])
                    {
                        //This is a currently created and added user --> next information must be account info
                        BuildAccount(currentUser, rdr);
                    }
                    else
                    {

                        //This is a user not yet processed, create the user
                        User newUser = new User { Id = (int)rdr["Id"], Name = (string)rdr["Name"], RowVersion=(byte[])rdr["RowVersion"] };
                        //Build the first rows associated account if it exists
                        BuildAccount(newUser, rdr);
                        foundUsers.Add(newUser);
                        //set currentUser
                        currentUser = newUser;
                    }


                }
            }
            return foundUsers;
        }
        private void BuildAccount(User newUser, SqlDataReader rdr)
        {
            if (rdr.IsDBNull(rdr.GetOrdinal("UserId")))//If this column is null, the row contains no account info
                return;
            newUser.Accounts.Add(new Account { Id = (int)rdr["Id"], Balance = Convert.ToDecimal(rdr["Balance"]), UserId = newUser.Id, User = newUser });

        }
        public IEnumerable<Account> GetAllAccountsFromUser(int userId)
        {
            List<Account> foundAccounts = new List<Account>();
            using (SqlConnection conn = new SqlConnection(CONNECTION_STRING))
            using (SqlCommand cmd = conn.CreateCommand())
            {
                conn.Open();
                cmd.CommandText = "SELECT * FROM Account WHERE UserId=@uid";
                cmd.Parameters.AddWithValue("uid", userId);
                var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    foundAccounts.Add(new Account { Id = (int)rdr["Id"], Balance = Convert.ToDecimal(rdr["Balance"]), UserId = userId });
                }
            }
            return foundAccounts;
        }
        /// <summary>
        /// Returns false if there has been an optimistic concurrency error
        /// </summary>
        /// <param name="u"></param>
        /// <returns></returns>
        public bool UpdateUser(User u, bool concurrencyCheck)
        {

            using (SqlConnection conn = new SqlConnection(CONNECTION_STRING))
            using (SqlCommand cmd = conn.CreateCommand())
            {
                conn.Open();
                //In this example i have used MSSQL Servers built in timestamp datatype. I called my column RowVersion (and also added this to my Model "User")
                //You could also check the actual values of your data in columns you find interesting
                //Another approach is to 1. Query data from the database, 2. let the user modify the data. Query the database again and check if values have changed. Then commit
                if (concurrencyCheck)
                {

                    cmd.CommandText = "UPDATE [User] SET Name=@name WHERE Id=@id AND RowVersion=@version";
                    cmd.Parameters.AddWithValue("version", u.RowVersion);
                }
                else
                {
                    cmd.CommandText = "UPDATE [User] SET Name=@name WHERE Id=@id";
                }
                   
                cmd.Parameters.AddWithValue("id", u.Id);
                cmd.Parameters.AddWithValue("name", u.Name);
               
                int rowsChanged = cmd.ExecuteNonQuery();
                return rowsChanged == 1;
            }
        }

    }
}
