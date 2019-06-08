using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

// migration create: dotnet ef migrations add InitialCreate
// migration drop: dotnet ef migrations remove

namespace MailDemon
{
    public class MailDemonDatabase : DbContext
    {
        private readonly DbContextOptions<MailDemonDatabase> options;

        private DbConnection conn;

        private void HandlePlatformSpecificDatabaseOptions()
        {
            if (Database.ProviderName.IndexOf("sqlite", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Database.ExecuteSqlCommand("PRAGMA auto_vacuum = INCREMENTAL;");
                Database.ExecuteSqlCommand("PRAGMA journal_mode = WAL;");
            }
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            //builder.Entity<Machine>().HasIndex(m => new { m.MachineGuid }).IsUnique();
            builder.Entity<MailListSubscription>().HasIndex(m => m.EmailAddress);
            builder.Entity<MailListSubscription>().HasIndex(m => m.EmailAddressDomain);
            builder.Entity<MailListSubscription>().HasIndex(m => m.ListName);
            builder.Entity<MailListSubscription>().HasIndex(m => m.SubscribeToken);
            builder.Entity<MailListSubscription>().HasIndex(m => m.UnsubscribeToken);
            builder.Entity<MailListSubscription>().HasIndex(m => m.Result);
            builder.Entity<MailTemplate>().HasIndex(m => m.Name);
            builder.Entity<MailList>().HasIndex(m => m.Name);
        }

        /// <summary>
        /// Initialize the db
        /// </summary>
        public void Initialize()
        {
            try
            {
                Database.Migrate();
                if (Database.ProviderName.IndexOf("sqlite", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    SQLitePCL.Batteries.Init();
                }
            }
            catch (Exception ex)
            {
                // OK, if in memory migration will fail
                MailDemonLog.Error("Error creating database: {0}", ex);
            }
        }

        ~MailDemonDatabase()
        {
            Dispose();
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public MailDemonDatabase() : this(MailDemonDatabaseSetup.ConfigureDB(null))
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options">Options</param>
        public MailDemonDatabase(DbContextOptions<MailDemonDatabase> options) : base(options)
        {
            this.options = options;
            try
            {
                Database.OpenConnection();
                conn = Database.GetDbConnection();
            }
            catch
            {
                // ok, probably in memory
            }
            HandlePlatformSpecificDatabaseOptions();
        }

        /// <summary>
        /// Dispose of the connection to the database
        /// </summary>
        public override void Dispose()
        {
            if (conn != null)
            {
                conn.Dispose();
                conn = null;
            }
            base.Dispose();
        }

        /// <summary>
        /// Delete database, use with caution
        /// </summary>
        /// <param name="confirm">True to delete</param>
        public static void DeleteDatabase(bool confirm)
        {
            if (confirm)
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MailDemon.sqlite");
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        public IEnumerable<KeyValuePair<string, IEnumerable<MailListSubscription>>> BeginBulkEmail(MailList list, string unsubscribeUrl, bool all)
        {
            if (all)
            {
                Database.ExecuteSqlCommand("UPDATE Subscriptions SET Result = 'Pending', ResultTimestamp = {0} WHERE ListName = {1}", DateTime.UtcNow, list.Name);
            }
            else
            {
                Database.ExecuteSqlCommand("UPDATE Subscriptions SET Result = 'Pending', ResultTimestamp = {0} WHERE ListName = {1} AND Result IN ('', 'Pending')", DateTime.UtcNow);
            }
            List<MailListSubscription> subs = new List<MailListSubscription>();
            string domain = null;
            foreach (MailListSubscription sub in Subscriptions.Where(s => s.ListName == list.Name && s.Result == "Pending").OrderBy(s => s.EmailAddressDomain))
            {
                if (sub.EmailAddressDomain != domain)
                {
                    if (subs.Count != 0)
                    {
                        yield return new KeyValuePair<string, IEnumerable<MailListSubscription>>(domain, subs);
                        subs.Clear();
                    }
                    domain = sub.EmailAddressDomain;
                }
                sub.MailList = list;
                sub.UnsubscribeUrl = string.Format(unsubscribeUrl, sub.UnsubscribeToken);
                subs.Add(sub);
            }
            if (subs.Count != 0)
            {
                yield return new KeyValuePair<string, IEnumerable<MailListSubscription>>(domain, subs);
            }
        }

        public DbSet<MailListSubscription> Subscriptions { get; set; }
        public DbSet<MailList> Lists { get; set; }
        public DbSet<MailTemplate> Templates { get; set; }
    }

    public class MailDemonDatabaseSetup : IDesignTimeDbContextFactory<MailDemonDatabase>
    {
        public MailDemonDatabaseSetup() { }

        public static DbContextOptions<MailDemonDatabase> ConfigureDB(IConfiguration conf)
        {
            string dbProvider;
            string connectionString;
            if (conf == null)
            {
                dbProvider = "sqlite";
                connectionString = null;
            }
            else
            {
                dbProvider = conf["DatabaseProvider"];
                connectionString = conf.GetConnectionString(dbProvider);
            }
            DbContextOptionsBuilder<MailDemonDatabase> options = new DbContextOptionsBuilder<MailDemonDatabase>();
            switch (dbProvider?.ToLowerInvariant())
            {
                case "sqlite":
                default:
                    if (string.IsNullOrWhiteSpace(connectionString))
                    {
                        connectionString = "Data Source=" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MailDemon.sqlite");
                    }
                    options.UseSqlite(connectionString);
                    break;

                case "inmemory":
                    options.UseInMemoryDatabase("InMemoryDB");
                    break;
            }
            return options.Options;
        }

        MailDemonDatabase IDesignTimeDbContextFactory<MailDemonDatabase>.CreateDbContext(string[] args)
        {
            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (!File.Exists(jsonPath))
            {
                jsonPath = Path.Combine(Path.GetTempPath(), "appsettings.json");
                File.WriteAllText(jsonPath, "{ }");
            }
            IConfiguration config = new ConfigurationBuilder().AddJsonFile(jsonPath).Build();
            return new MailDemonDatabase(ConfigureDB(config));
        }
    }
}
