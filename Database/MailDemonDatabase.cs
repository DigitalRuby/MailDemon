using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

using LiteDB;

namespace MailDemon
{
    /// <summary>
    /// Mail demon database. All objects in the db should have a long Id property.
    /// This class is thread safe.
    /// </summary>
    public class MailDemonDatabase : IDisposable
    {
        /// <summary>
        /// Path to the database
        /// </summary>
        public static string DatabasePath { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MailDemon.db");

        /// <summary>
        /// Database options
        /// </summary>
        public static string DatabaseOptions { get; set; } = string.Empty;

        private static readonly BsonMapper mapper = new BsonMapper { EmptyStringToNull = false };

        /// <summary>
        /// Initialize db
        /// </summary>
        static MailDemonDatabase()
        {
            using (var db = GetDB())
            {
                var coll = db.GetCollection<MailListSubscription>();
                coll.EnsureIndex("Fields.EmailAddress");
                coll.EnsureIndex(x => x.ListName);
                coll.EnsureIndex(x => x.SubscribeToken);
                coll.EnsureIndex(x => x.UnsubscribeToken);
                var coll2 = db.GetCollection<MailTemplate>();
                coll2.EnsureIndex(x => x.Name, true);
                var coll3 = db.GetCollection<MailList>();
                coll3.EnsureIndex(x => x.Name, true);
            }
        }

        /// <summary>
        /// Delete database file - use with caution!
        /// </summary>
        public static void DeleteDatabase()
        {
            if (File.Exists(MailDemonDatabase.DatabasePath))
            {
                while (true)
                {
                    try
                    {
                        // WTF litedb releases files in the finalizer...???
                        System.GC.Collect();
                        File.Delete(MailDemonDatabase.DatabasePath);
                        break;
                    }
                    catch
                    {
                        System.Threading.Thread.Sleep(100);
                    }
                }
            }
        }

        /// <summary>
        /// Access the underlying db structure - be careful here, if the underlying structure changes, code will break
        /// </summary>
        /// <returns>Database</returns>
        public static LiteDatabase GetDB()
        {
            return new LiteDatabase($"Filename={DatabasePath}; {DatabaseOptions}", mapper);
            //return new LiteDatabase(File.Open(DatabasePath, System.IO.FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite), mapper, null, true);
        }

        /// <summary>
        /// Dispose of all resources
        /// </summary>
        public void Dispose()
        {

        }

        /// <summary>
        /// Ensure index for a type of object
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <typeparam name="T2">Type of field</typeparam>
        /// <param name="predicate">Predicate</param>
        /// <param name="unique">Whether the index should be unique</param>
        public void EnsureIndex<T, T2>(Expression<Func<T, T2>> predicate, bool unique = false)
        {
            using (var db = GetDB())
            {
                var coll = db.GetCollection<T>();
                coll.EnsureIndex(predicate, unique);
            }
        }

        /// <summary>
        /// Drop an index for a type of object
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <typeparam name="T2">Type of field</typeparam>
        /// <param name="predicate">Predicate</param>
        /// <returns>True if dropped, false if not</returns>
        public bool DropIndex<T, T2>(Expression<Func<T, T2>> predicate)
        {
            using (var db = GetDB())
            {
                var coll = db.GetCollection<T>();
                return coll.DropIndex((predicate.Body as MemberExpression).Member.Name);
            }
        }

        /// <summary>
        /// Optimize database, shrink, reorganize pages, etc.
        /// </summary>
        public void Optimize()
        {
            using (var db = GetDB())
            {
                db.Shrink();
            }
        }

        /// <summary>
        /// Select an object
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="id">Id to find</param>
        /// <returns>Object or null if not found</returns>
        public T Select<T>(long id)
        {
            using (var db = GetDB())
            {
                var coll = db.GetCollection<T>();
                return coll.FindById(id);
            }
        }

        /// <summary>
        /// Select all objects
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <returns>All objects</returns>
        public IEnumerable<T> Select<T>()
        {
            using (var db = GetDB())
            {
                var coll = db.GetCollection<T>();
                return coll.FindAll();
            }
        }

        /// <summary>
        /// Select objects
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="predicate">Predicate to query</param>
        /// <param name="updater">Allow update for found objects, null return value</param>
        /// <param name="offset">Skip count</param>
        /// <param name="count">Max select count</param>
        /// <returns>Objects</returns>
        public IEnumerable<T> Select<T>(Expression<Func<T, bool>> predicate, Func<T, bool> updater = null, int offset = 0, int count = int.MaxValue)
        {
            using (var db = GetDB())
            {
                var coll = db.GetCollection<T>();
                IEnumerable<T> results = coll.Find(predicate, offset, count);
                if (updater == null)
                {
                    return results;
                }
                else
                {
                    foreach (T obj in results)
                    {
                        if (updater.Invoke(obj))
                        {
                            coll.Update(obj);
                        }
                    }
                    return null;
                }
            }
        }

        /// <summary>
        /// Insert or update based on Id field
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="obj">Object to insert or update</param>
        public void Upsert<T>(in T obj)
        {
            using (var db = GetDB())
            {
                var coll = db.GetCollection<T>();
                coll.Upsert(obj);
            }
        }

        /// <summary>
        /// Insert or update based on Id field
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="obj">Objects to insert or update</param>
        public void Upsert<T>(IEnumerable<T> objs)
        {
            using (var db = GetDB())
            {
                var coll = db.GetCollection<T>();
                foreach (T obj in objs)
                {
                    coll.Upsert(obj);
                }
            }
        }

        /// <summary>
        /// Insert
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="obj">Object to insert</param>
        public void Insert<T>(in T obj)
        {
            using (var db = GetDB())
            {
                var coll = db.GetCollection<T>();
                coll.Insert(obj);
            }
        }

        /// <summary>
        /// Insert
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="obj">Objects to insert</param>
        public void Insert<T>(IEnumerable<T> objs)
        {
            using (var db = GetDB())
            {
                var coll = db.GetCollection<T>();
                coll.InsertBulk(objs);
            }
        }

        /// <summary>
        /// Update by id
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="obj">Object to update</param>
        public void Update<T>(in T obj)
        {
            using (var db = GetDB())
            {
                var coll = db.GetCollection<T>();
                coll.Update(obj);
            }
        }

        /// <summary>
        /// Update by id
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="obj">Objects to update</param>
        public void Update<T>(IEnumerable<T> objs)
        {
            using (var db = GetDB())
            {
                var coll = db.GetCollection<T>();
                coll.Update(objs);
            }
        }

        /// <summary>
        /// Delete by id
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="id">Id of the object to delete</param>
        public void Delete<T>(long id)
        {
            using (var db = GetDB())
            {
                var coll = db.GetCollection<T>();
                coll.Delete(id);
            }
        }

        /// <summary>
        /// Delete by query
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="predicate">Query of objects to delete</param>
        public void Delete<T>(Expression<Func<T, bool>> predicate)
        {
            using (var db = GetDB())
            {
                var coll = db.GetCollection<T>();
                coll.Delete(predicate);
            }
        }
    }
}
