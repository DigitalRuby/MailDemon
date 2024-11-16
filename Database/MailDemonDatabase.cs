using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
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

        private void HandlePlatformSpecificDatabaseOptions()
        {
            if (Database.ProviderName.IndexOf("sqlite", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Database.ExecuteSqlRaw("PRAGMA auto_vacuum = INCREMENTAL;");
                Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
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
                if (Database.ProviderName.IndexOf("sqlite", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    SQLitePCL.Batteries.Init();
                }
                Database.Migrate();
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
        /// <param name="config">Configuration</param>
        public MailDemonDatabase(IConfiguration config = null) : this(MailDemonDatabaseSetup.ConfigureDB(config))
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options">Options</param>
        public MailDemonDatabase(DbContextOptions<MailDemonDatabase> options) : base(options)
        {
            this.options = options;
            HandlePlatformSpecificDatabaseOptions();
        }

        /// <summary>
        /// Delete database, use with caution
        /// </summary>
        /// <param name="confirm">True to delete</param>
        public static void DeleteDatabase(bool confirm)
        {
            if (confirm)
            {
                SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                string path = Path.Combine(Directory.GetCurrentDirectory(), "MailDemon.sqlite");
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
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
            string dbProvider = conf?["DatabaseProvider"];
            string connectionString = (dbProvider == null ? null : conf?.GetSection("ConnectionStrings")?[dbProvider]);
            if (dbProvider == null || connectionString == null)
            {
                dbProvider = "sqlite";
                connectionString = null;
            }
            DbContextOptionsBuilder<MailDemonDatabase> options = new DbContextOptionsBuilder<MailDemonDatabase>();
            switch (dbProvider?.ToLowerInvariant())
            {
                case "sqlite":
                default:
                    if (string.IsNullOrWhiteSpace(connectionString))
                    {
                        connectionString = "Data Source=" + Path.Combine(AppContext.BaseDirectory, "MailDemon.sqlite");
                    }
                    options.UseSqlite(connectionString);
                    break;

                case "sqlserver":
                    options.UseSqlServer(connectionString);
                    break;

                case "inmemory":
                    options.UseInMemoryDatabase("InMemoryDB");
                    break;
            }
            return options.Options;
        }

        MailDemonDatabase IDesignTimeDbContextFactory<MailDemonDatabase>.CreateDbContext(string[] args)
        {
            string jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            if (!File.Exists(jsonPath))
            {
                jsonPath = Path.Combine(Path.GetTempPath(), "appsettings.json");
                File.WriteAllText(jsonPath, "{ }");
            }
            IConfiguration config = new ConfigurationBuilder().AddJsonFile(jsonPath).Build();
            return new MailDemonDatabase(ConfigureDB(config));
        }
    }

    /// <summary>
    /// Provides mail demon database
    /// </summary>
    public interface IMailDemonDatabaseProvider
    {
        /// <summary>
        /// Get a database instance. Must dispose when done with.
        /// </summary>
        /// <param name="config">Configuration</param>
        /// <returns>Database</returns>
        MailDemonDatabase GetDatabase(IConfiguration config = null);
    }
}
