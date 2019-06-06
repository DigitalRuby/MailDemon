using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace MailDemon
{
    /// <summary>
    /// Interface for mail demon databases
    /// </summary>
    public interface IMailDemonDatabase : IDisposable
    {
        /// <summary>
        /// Select an object
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="id">Id to find</param>
        /// <returns>Object or null if not found</returns>
        T Select<T>(long id) where T : class;

        /// <summary>
        /// Select all objects
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <returns>All objects</returns>
        IEnumerable<T> Select<T>() where T : class;

        /// <summary>
        /// Select objects
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="predicate">Predicate to query</param>
        /// <param name="updater">Allow update for found objects, null return value</param>
        /// <param name="offset">Skip count</param>
        /// <param name="count">Max select count</param>
        /// <returns>Objects</returns>
        IEnumerable<T> Select<T>(Expression<Func<T, bool>> predicate, Func<T, bool> updater = null, int offset = 0, int count = int.MaxValue) where T : class;

        /// <summary>
        /// Insert or update based on Id field
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="obj">Object to insert or update</param>
        void Upsert<T>(in T obj) where T : class;

        /// <summary>
        /// Insert or update based on Id field
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="obj">Objects to insert or update</param>
        void Upsert<T>(IEnumerable<T> objs) where T : class;

        /// <summary>
        /// Insert
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="obj">Object to insert</param>
        void Insert<T>(in T obj) where T : class;

        /// <summary>
        /// Insert
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="obj">Objects to insert</param>
        void Insert<T>(IEnumerable<T> objs) where T : class;

        /// <summary>
        /// Update by id
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="obj">Object to update</param>
        void Update<T>(in T obj) where T : class;

        /// <summary>
        /// Update by id
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="obj">Objects to update</param>
        void Update<T>(IEnumerable<T> objs) where T : class;

        /// <summary>
        /// Delete by id
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="id">Id of the object to delete</param>
        void Delete<T>(long id) where T : class;

        /// <summary>
        /// Delete by query
        /// </summary>
        /// <typeparam name="T">Type of object</typeparam>
        /// <param name="predicate">Query of objects to delete</param>
        void Delete<T>(Expression<Func<T, bool>> predicate) where T : class;
    }
}
