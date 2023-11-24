/*
 * SQLite Manager V2
 * Copyright (C) 2023 Eric "ericg2" Gold
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.Data.SQLite;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace SQLiteWrapper
{
    /// <summary>
    /// This <see cref="SQLiteManager"/> class is designed to provide a wrapper for managing
    /// SQLite databases. It supports many features which makes the process easier to use.
    /// </summary>
    public class SQLiteManager : IDisposable
    {
        /// <summary>
        /// This <see cref="Generation"/> class is responsible for generating CREATE, INSERT, UPDATE, and DELETE 
        /// <see cref="SQLiteCommand"/>s based on the input information.
        /// </summary>
        private static class Generation
        {
            /// <summary>
            /// Generates an inserting <see cref="string"/> based on the Type and table name.
            /// </summary>
            /// <param name="obj">The <see cref="Type"/> of the object to use.</param>
            /// <param name="tblName">The table name to use.</param>
            /// <param name="createStr">The output creation <see cref="string"/>; null if failed.</param>
            /// <returns>If the procedure was successful.</returns>
            public static bool GenerateCreateCommand(Type obj, string tblName, out SQLiteCommand? cmd)
            {
                cmd = null;
                string createStr = "";

                StringBuilder createSb = new StringBuilder();

                createSb.Append("CREATE TABLE " + tblName + "(");

                if (obj.GetProperties().Length == 0)
                    return false;

                int primaryCount = 0;
                foreach (PropertyInfo prop in obj.GetProperties())
                {

                    var objType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                    string sqlType = "TEXT";
                    object sqlKey = prop.Name;

                    switch (Type.GetTypeCode(objType))
                    {
                        case TypeCode.Boolean:
                        case TypeCode.Int32:
                        case TypeCode.Int64:
                        case TypeCode.Byte:
                            sqlType = "INTEGER";
                            break;

                        case TypeCode.Double:
                            sqlType = "REAL";
                            break;

                        case TypeCode.String:
                        case TypeCode.DateTime:
                        case TypeCode.Object when objType.GetInterface(nameof(IEnumerable)) != null:
                        case TypeCode.Object when objType.IsArray:
                            sqlType = "TEXT";
                            break;
                    }

                    // Look for any attributes.
                    try
                    {
                        SQLiteAttribute? attr = prop.GetCustomAttribute(typeof(SQLiteAttribute)) as SQLiteAttribute;
                        if (attr != null && !attr.Ignore)
                        {
                            if (attr.PrimaryKey)
                            {
                                primaryCount++;

                                if (primaryCount > 1)
                                    return false;

                                sqlType += " PRIMARY KEY";
                            }
                            if (attr.Default != null)
                            {
                                object defaultVal;
                                string defaultSqlType;

                                if (!GetSQLType(attr.Default, out defaultSqlType, out defaultVal))
                                    return false;

                                // Removes the primary key from the type.
                                string actualType;
                                try
                                {
                                    if (sqlType.Contains(" "))
                                        actualType = sqlType.Split(' ')[0].Trim();
                                    else
                                        actualType = sqlType;
                                } catch (Exception)
                                {
                                    actualType = sqlType;
                                }

                                if (!defaultSqlType.Equals(actualType))
                                    return false; // Type does not match.

                                sqlType += " DEFAULT " + defaultVal;
                            }
                            if (attr.NotNull)
                                sqlType += " NOT NULL";
                        }           
                    } catch (Exception) { }

                    createSb.Append($"{sqlKey} {sqlType}, ");
                }
                createStr = createSb.ToString();

                if (createStr.EndsWith(", "))
                    createStr = createStr.Substring(0, createStr.Length - 2);

                createStr += ");";
                cmd = new SQLiteCommand(createStr);

                return true;
            }

            /// <summary>
            /// Generates an inserting <see cref="SQLiteCommand"/> based on the <see cref="object"/>.
            /// </summary>
            /// <param name="obj">The <see cref="object"/> to use.</param>
            /// <param name="tblName">The table name to use.</param>
            /// <param name="cmd">The output <see cref="SQLiteCommand"/>; null if failed.</param>
            /// <returns>If the procedure was successful.</returns>
            public static bool GenerateInsertCommand<T>(T obj, string tblName, out SQLiteCommand? cmd) where T : class
            {
                string insertStr;
                cmd = null;

                if (obj == null || obj.GetType().GetProperties().Length == 0)
                    return false;

                StringBuilder insertHeaderSb = new StringBuilder();
                StringBuilder insertValueSb = new StringBuilder();

                insertHeaderSb.Append("INSERT INTO " + tblName + "(");
                insertValueSb.Append(" VALUES (");

                Dictionary<string, object> sqlParams = new Dictionary<string, object>();

                foreach (PropertyInfo prop in obj.GetType().GetProperties())
                {
                    if (TestAttributeInfo(obj, prop, out object sqlValue, out SQLiteAttribute? attr))
                    {
                        string sqlKey = prop.Name;
                        insertHeaderSb.Append($"{sqlKey}, ");
                        insertValueSb.Append($"@{sqlKey}, ");

                        sqlParams.Add($"@{sqlKey}", sqlValue);
                    } else
                    {
                        return false;
                    }
                }

                string insertHeaderStr = insertHeaderSb.ToString();
                string insertValueStr = insertValueSb.ToString();

                if (insertHeaderStr.EndsWith(", "))
                    insertHeaderStr = insertHeaderStr.Substring(0, insertHeaderStr.Length - 2);
                if (insertValueStr.EndsWith(", "))
                    insertValueStr = insertValueStr.Substring(0, insertValueStr.Length - 2);

                insertHeaderStr += ")";
                insertValueStr += ");";

                insertStr = insertHeaderStr + insertValueStr;

                cmd = new SQLiteCommand(insertStr);
                foreach (KeyValuePair<string, object> pair in sqlParams)
                    cmd.Parameters.AddWithValue(pair.Key, pair.Value);
                return true;
            }

            /// <summary>
            /// Generates an updating <see cref="SQLiteCommand"/> based on the <b>primary key</b> of the <see cref="object"/>.
            /// </summary>
            /// <param name="obj">The <see cref="object"/> to use. <b>Must contain a primary key!</b></param>
            /// <param name="tblName">The table name to use.</param>
            /// <param name="cmd">The output <see cref="SQLiteCommand"/>; null if failed.</param>
            /// <returns>If the procedure was successful.</returns>
            public static bool GenerateUpdateCommand(object obj, string tblName, out SQLiteCommand? cmd)
            {
                cmd = null;
                string updateStr = "";
                bool primaryFound = false;

                if (obj == null || obj.GetType().GetProperties().Length == 0)
                    return false;

                StringBuilder updateHeaderSb = new StringBuilder();
                StringBuilder updateWhereSb = new StringBuilder();

                updateHeaderSb.Append("UPDATE " + tblName + " SET ");
                updateWhereSb.Append("WHERE ");

                Dictionary<string, object> sqlParams = new Dictionary<string, object>();

                foreach (PropertyInfo prop in obj.GetType().GetProperties())
                {
                    if (TestAttributeInfo(obj, prop, out object sqlValue, out SQLiteAttribute? attr))
                    {
                        string sqlKey = prop.Name;

                        if (attr != null && attr.PrimaryKey)
                        {
                            primaryFound = true;
                            updateWhereSb.Append($"{sqlKey} = @{sqlKey} AND ");
                        } else
                        {
                            updateHeaderSb.Append($"{sqlKey} = @{sqlKey}, ");
                        }

                        sqlParams.Add($"@{sqlKey}", sqlValue);
                    } else
                    {
                        return false; // bad conditions; return false.
                    }
                }

                if (!primaryFound)
                    return false;

                string updateHeaderStr = updateHeaderSb.ToString();
                string updateWhereStr = updateWhereSb.ToString();

                if (updateHeaderStr.EndsWith(", "))
                    updateHeaderStr = updateHeaderStr.Substring(0, updateHeaderStr.Length - 2);

                if (updateWhereStr.EndsWith(" AND "))
                    updateWhereStr = updateWhereStr.Substring(0, updateWhereStr.Length - 5);

                updateStr = updateHeaderStr + updateWhereStr;

                cmd = new SQLiteCommand(updateStr);
                foreach (KeyValuePair<string, object> pair in sqlParams)
                    cmd.Parameters.AddWithValue(pair.Key, pair.Value);
                return true;
            }

            /// <summary>
            /// Generates a deleting <see cref="SQLiteCommand"/> based on <b>all Properties</b> of the <see cref="object"/>.
            /// </summary>
            /// <param name="obj">The <see cref="object"/> to use.</param>
            /// <param name="tblName">The table name to use.</param>
            /// <param name="cmd">The output <see cref="SQLiteCommand"/>; null if failed.</param>
            /// <returns>If the procedure was successful.</returns>
            public static bool GenerateDeleteCommand(object obj, string tblName, out SQLiteCommand? cmd)
            {
                cmd = null;

                if (obj == null || obj.GetType().GetProperties().Length == 0)
                    return false;

                StringBuilder deleteCommandSb = new StringBuilder($"DELETE FROM {tblName} WHERE ");

                Dictionary<string, object> sqlParams = new Dictionary<string, object>();

                foreach (PropertyInfo prop in obj.GetType().GetProperties())
                {
                    if (TestAttributeInfo(obj, prop, out object sqlValue, out SQLiteAttribute? attr))
                    {
                        string sqlKey = prop.Name;
                        deleteCommandSb.Append($"{sqlKey} = @{sqlKey} AND ");
                        sqlParams.Add($"@{sqlKey}", sqlValue);
                    } else
                    {
                        return false; // bad conditions; return false.
                    }
                }

                string deleteCommandStr = deleteCommandSb.ToString().TrimEnd(' ', 'A', 'N', 'D');

                cmd = new SQLiteCommand(deleteCommandStr);

                foreach (KeyValuePair<string, object> pair in sqlParams)
                    cmd.Parameters.AddWithValue(pair.Key, pair.Value);

                return true;
            }
        }

        private SQLiteConnection? _Connection;
        private string _Database;

        /// <summary>
        /// The <see cref="SQLiteConnection"/> instance used by the Manager; may be null
        /// if a method has not been previously called.
        /// </summary>
        public SQLiteConnection? Connection
        {
            get
            {
                return _Connection;
            }
        }

        /// <summary>
        /// The <see cref="Path"/> of the used Database File.
        /// </summary>
        public string Database
        {
            get
            {
                return _Database;
            }
        }

        /// <summary>
        /// Safely converts two <b>NON-NULL</b> (struct) values in a single method. This ignores
        /// all conversion exceptions, returning the "defaultValue" output upon failure.
        /// </summary>
        /// <typeparam name="O">A non-nullable input struct.</typeparam>
        /// <typeparam name="N">A non-nullable output struct.</typeparam>
        /// <param name="rdr">The <see cref="SQLiteDataReader"/> to use for conversion.</param>
        /// <param name="col">The SQLite-column name.</param>
        /// <param name="defaultValue">The default value to use upon failure.</param>
        /// <returns>The converted value, or "defaultValue" if an Exception occurred.</returns>
        public static N SafeConvert<N>(SQLiteDataReader rdr, string col, N defaultValue)
            where N : notnull
        {
            try
            {
                object? obj = Convert.ChangeType(rdr[col], typeof(N));
                return obj == null ? defaultValue : (N)obj;
            }
            catch (Exception)
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Safely converts two <b>NON-NULL</b> (struct) values in a single method. This ignores
        /// all conversion exceptions, returning the "defaultValue" output upon failure.
        /// </summary>
        /// <typeparam name="O">A non-nullable input struct.</typeparam>
        /// <typeparam name="N">A non-nullable output struct.</typeparam>
        /// <param name="currentType">The current value to convert</param>
        /// <returns>The converted value, or "defaultValue" if an Exception occurred.</returns>
        public static N SafeConvert<O, N>(object currentVal, N defaultValue) where N : struct
        {
            return SafeConvert<object, N>(currentVal, defaultValue);
        }

        /// <summary>
        /// Constructs a new <see cref="SQLiteManager"/> instance with a File Path.
        /// </summary>
        /// <param name="filePath">The <see cref="string"/> File Path of the Database.</param>
        public SQLiteManager(string filePath)
        {
            _Database = filePath;
        }

        /// <summary>
        /// Opens the <see cref="SQLiteConnection"/>. Not intended for public-access.
        /// </summary>
        /// <param name="ex">Returns an <see cref="Exception"/> if applicable; null otherwise.</param>
        /// <returns></returns>
        private bool OpenConnection(out Exception? ex)
        {
            ex = null;
            try
            {
                if (_Connection != null && _Connection.State != System.Data.ConnectionState.Closed)
                    return true;
            } catch (Exception) { }

            _Connection = new SQLiteConnection($"Data Source={Database};Version=3;New={(!File.Exists(Database) ? "True" : "False")};Compress=True");
            try
            {
                _Connection.Open();
                return true;
            }
            catch (Exception sqlEx)
            {
                ex = sqlEx;
                return false;
            }
        }

        /// <summary>
        /// Disposes the <see cref="SQLiteConnection"/>. The connection will automatically re-open upon calling another method.
        /// </summary>
        public void Dispose()
        {
            try
            {
                if (_Connection != null && _Connection.State != System.Data.ConnectionState.Closed)
                    _Connection.Dispose();
            } catch (Exception) { }
        }

        /// <summary>
        /// Executes an <see cref="SQLiteCommand"/> using the specified Database.
        /// </summary>
        /// <param name="cmd">The <see cref="SQLiteCommand"/> to execute.</param>
        /// <returns>The number of affected rows.</returns>
        public int ExecuteCommand(SQLiteCommand cmd)
        {
            if (!OpenConnection(out _))
                return 0;
            try
            {
                cmd.Connection = Connection;
                int ret = cmd.ExecuteNonQuery();
                //CloseConnection();
                return ret;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Executes an <see cref="SQLiteCommand"/> using the specified Database.
        /// </summary>
        /// <param name="cmd">The <see cref="SQLiteCommand"/> to execute.</param>
        /// <returns>A <see cref="SQLiteDataReader"/> with the output.</returns>
        public SQLiteDataReader? ExecuteReader(SQLiteCommand cmd)
        {
            if (!OpenConnection(out _))
                return null;

            try
            {
                cmd.Connection = Connection;
                SQLiteDataReader rdr = cmd.ExecuteReader();

                return rdr;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Executes an <see cref="SQLiteCommand"/> using the specified Database.
        /// </summary>
        /// <param name="cmd">The <see cref="SQLiteCommand"/> to execute.</param>
        /// <returns>An <see cref="object"/> representing the output.</returns>
        public object? ExecuteScalar(SQLiteCommand cmd)
        {
            if (!OpenConnection(out _))
                return null;
            
            try
            {
                cmd.Connection = Connection;
                object output = cmd.ExecuteScalar();

                //CloseConnection();
                return output;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <returns>All the SQLite tables associated with the Connected Database.</returns>
        public List<string> GetTables()
        {
            List<string> output = new List<string>();
            SQLiteCommand cmd = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table'");

            using (SQLiteDataReader? rdr = ExecuteReader(cmd))
            {
                if (rdr == null)
                    return output;
                while (rdr.Read())
                    output.Add(SafeConvert(rdr, "name", string.Empty));
            }

            return output;
        }

        /// <param name="tblName">The SQLite Table Name to check.</param>
        /// <returns>All columns for a specific SQLite table in the Connected Database.</returns>
        public List<PragmaTableInfo> GetColumnsForTable(string tblName)
        {
            List<PragmaTableInfo> output = new List<PragmaTableInfo>();
            SQLiteCommand cmd = new SQLiteCommand("SELECT * FROM pragma_table_info(@Table)");
            cmd.Parameters.AddWithValue("@Table", tblName);

            using (SQLiteDataReader? rdr = ExecuteReader(cmd))
            {
                if (rdr == null)
                    return output;
                while (rdr.Read())
                    output.Add(new PragmaTableInfo(rdr));
            }

            return output;
        }

        /// <summary>
        /// Tests and obtains all SQLite Attribute Information for an Object and a specific
        /// <see cref="PropertyInfo"/>. Not intended for public-use.
        /// </summary>
        /// <param name="obj">The <see cref="object"/> to input.</param>
        /// <param name="prop">The <see cref="PropertyInfo"/> to input.</param>
        /// <param name="sqlValue">An output containing the SQL value of the inputs.</param>
        /// <param name="attr">The <see cref="SQLiteAttribute"/> if applicable.</param>
        /// <returns>True if the information is acceptable, false otherwise.</returns>
        private static bool TestAttributeInfo<T>(T? obj, PropertyInfo prop, out object sqlValue, out SQLiteAttribute? attr) where T : class
        {
            sqlValue = "null";
            attr = null;

            object? sqlObject = prop.GetValue(obj, null);
            if (sqlObject == null)
                return false; // must contain a value; no null-objects allowed.

            try
            {
                attr = prop.GetCustomAttribute(typeof(SQLiteAttribute)) as SQLiteAttribute;
                if (attr != null && !attr.Ignore)
                {
                    if (attr.Default != null)
                        sqlObject = attr.Default;

                    if ((attr.NotNull || attr.PrimaryKey) && sqlObject == null)
                        return false;
                }        
            } catch (Exception) { }

            if (sqlObject == null)
            {
                sqlValue = "null";
            } else
            {
                if (!GetSQLType(sqlObject, out _, out sqlValue))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Attempts to fill the accessible Properties of an <see cref="object"/> using the 
        /// values from SQLiteDataReader.
        /// </summary>
        /// <param name="obj">The <see cref="object"/> to update.</param>
        /// <param name="rdr">The <see cref="SQLiteDataReader"/> to use.</param>
        /// <param name="tblName">The table name to use.</param>
        /// <returns>If the procedure was successful.</returns>
        public bool FillProperties(ref object obj, SQLiteDataReader rdr, string tblName = "")
        {
            if (!rdr.HasRows || rdr.IsClosed)
                return false;

            PropertyInfo[] props = obj.GetType().GetProperties();
            List<string> allCols = GetColumnsForTable(tblName)
                .Select(info => info.Name)
                .ToList();

            foreach (PropertyInfo prop in props)
            {
                if (TestAttributeInfo(obj, prop, out _, out SQLiteAttribute? attr))
                {
                    string col = prop.Name;

                    if (attr != null && !string.IsNullOrEmpty(attr.Column))
                        col = attr.Column;

                    if (!allCols.Contains(col))
                    {
                        return false;
                    }

                    object? outObj = ConvertColumnValue(rdr[col], prop.PropertyType) ?? (attr != null ? attr.Default : null);
                    if (outObj != null) { 
                        prop.SetValue(obj, outObj, null);
                    }
                }
            }

            return true;
        }

        private object? ConvertColumnValue(object columnValue, Type targetType)
        {
            try
            {
                if (columnValue == DBNull.Value || columnValue == null)
                {
                    return null;
                }

                string columnString = columnValue.ToString() ?? string.Empty;
                columnString = columnString.Replace("\"", "").Trim();

                // Check if the targetType is an array or IEnumerable
                if (targetType.IsArray || (targetType.GetInterface(nameof(IEnumerable)) != null && targetType != typeof(string)))
                {
                    Type? elementType = targetType.IsArray ? targetType.GetElementType() : targetType.GetGenericArguments()[0];
                    if (elementType == null)
                        return null;

                    if (ToArray(elementType, columnString, out IEnumerable<object> values))
                    {
                        // If it's an array, create an array of the elementType
                        // If it's IEnumerable, create an instance of List<T> and add the values
                        object? collection;

                        if (targetType.IsArray)
                        {
                            // Create an array using Array.CreateInstance
                            Array array = Array.CreateInstance(elementType, values.Count());
                            int index = 0;
                            foreach (var value in values)
                            {
                                array.SetValue(Convert.ChangeType(value, elementType), index++);
                            }
                            collection = array;
                        } else
                        {
                            // Create an instance of List<T>
                            var listType = typeof(List<>).MakeGenericType(elementType);
                            var list = Activator.CreateInstance(listType);

                            if (list != null)
                            {
                                foreach (var value in values)
                                {
                                    listType.GetMethod("Add")?.Invoke(list, new[] { Convert.ChangeType(value, elementType) });
                                }
                            }
                            collection = list;
                        }
                        return collection;
                    }
                }
                // Check if the targetType is a boolean; it has a 1 or 0 value as stored in SQL.
                if (targetType == typeof(bool))
                {
                    columnString = columnString.Equals("1") ? "true" : "false";
                }

                return Convert.ChangeType(columnString, targetType);
            } catch (Exception ex)
            {
                return null;
            }
        }

        /// <summary>
        /// Attempts to fill the accessible Properties of an <see cref="object"/> using <b>each row</b> of
        /// values from SQLiteDataReader. Binds directly to a class.
        /// </summary>
        /// <typeparam name="T">A class which can be initialized with the "new" keyword.</typeparam>
        /// <param name="rdr">The <see cref="SQLiteDataReader"/> to use.</param>
        /// <param name="output">The output of the operation.</param>
        /// <param name="tblName">The table name to use.</param>
        /// <returns>If the procedure was successful.</returns>
        public bool FillAllProperties<T>(SQLiteDataReader? rdr, out List<T> output, string tblName="") where T : new()
        {
            output = new List<T>();
            if (rdr == null)
                return false;
            if (rdr.IsClosed)
                return false;

            while (rdr.Read())
            {
                try
                {
                    object obj = new T();
                    if (!FillProperties(ref obj, rdr, tblName))
                        continue;
                    output.Add((T)obj);
                }
                catch (Exception)
                {
                    continue;
                }
            }
            return true;
        }

        /// <summary>
        /// Attempts to fill the accessible Properties of an <see cref="object"/> using <b>each row</b> of
        /// <b>all possible values</b>. Binds directly to a class.
        /// </summary>
        /// <typeparam name="T">A class which can be initialized with the "new" keyword.</typeparam>
        /// <param name="output">The output of the operation.</param>
        /// <param name="tblName">The table name to use.</param>
        /// <returns>If the procedure was successful.</returns>
        public bool FillAllProperties<T>(out List<T> output, string tblName = "") where T : new()
        {
            if (string.IsNullOrEmpty(tblName))
                tblName = typeof(T).Name;

            return FillAllProperties<T>(ExecuteReader(new SQLiteCommand("SELECT * FROM " + tblName)), out output, tblName);
        }   

        
        /// <summary>
        /// Converts a SQLite <see cref="string"/> input to the desired array.
        /// </summary>
        /// <param name="targetType">The <see cref="Type"/> to output.</param>
        /// <param name="input">The input <see cref="string"/> from SQLite.</param>
        /// <param name="output">The output array.</param>
        /// <returns>If the procedure was successful.</returns>
        public static bool ToArray(Type targetType, string input, out IEnumerable<object> output)
        {
            List<object> allObjects = new List<object>();

            output = new object[0];
            if (input == null)
                return false;

            try
            {
                string[] parts = input.Split('|');

                if (parts.Length != 2 || !parts[0].Equals("ARR", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                JToken arrayToken = JToken.Parse(parts[1].Trim());

                if (arrayToken is JArray array)
                {
                    foreach (JToken jToken in array)
                    {
                        string temp = jToken.ToString(Formatting.None);
                        object? o = JsonConvert.DeserializeObject(temp, targetType);
                        if (o != null)
                            allObjects.Add(o);
                    }
                } 
                else
                {
                    // Handle non-array case
                    string temp = arrayToken.ToString(Formatting.None);
                    object? o = JsonConvert.DeserializeObject(temp, targetType);
                    if (o != null)
                        allObjects.Add(o);
                }

                // Convert the List<object> to IEnumerable<object>
                output = allObjects;


                return true;

            } catch (Exception ex)
            {
                return false;
            }
        }

        /// <summary>
        /// Converts a SQLite <see cref="string"/> input to the desired array.
        /// </summary>
        /// <typeparam name="T">The type of the array to output.</typeparam>
        /// <param name="input">The input <see cref="string"/> from SQLite.</param>
        /// <param name="output">The output array.</param>
        /// <param name="ignoreType">If the type-check should be ignored.</param>
        /// <returns>If the procedure was successful.</returns>
        public static bool ToArray<T>(string input, out IEnumerable<T> output, bool ignoreType=false) where T : notnull
        {
            output = new T[0];
            if (input == null)
                return false;

            try
            {
                string inputStr = Convert.ToString(input);

                List<T> outputList = new List<T>();

                string[] pairs = inputStr.Replace("\"", "").Trim().Split('|');

                if (!pairs[0].Equals("ARR") || string.IsNullOrEmpty(pairs[1]) || (!ignoreType && !typeof(T).Name.Contains(pairs[1])))
                    return false; // invalid setup

                string[] entries = pairs[2]
                    .Substring(1, pairs[2].Length - 2) // remove ()
                    .Trim() // trim
                    .Split(','); // split

                foreach (string entry in entries)
                {
                    if (string.IsNullOrEmpty(entry) || entry.Equals(",") || entry.Equals(")"))
                        continue;
                    outputList.Add((T)Convert.ChangeType(entry, typeof(T)));
                }

                output = outputList;
                return true;

            } catch (Exception) { return false; }
        }

        /// <summary>
        /// Converts an Array input to a SQLite-compatible <see cref="string"/>.
        /// </summary>
        /// <typeparam name="T">The type of the array to output.</typeparam>
        /// <param name="input">The input <see cref="string"/> from SQLite.</param>
        /// <param name="output">The output array.</param>
        /// <returns>If the procedure was successful.</returns>
        public static bool FromArray<T>(IEnumerable<T> input, out object output) where T : notnull
        {
            #region Not working perfectly
            /*
            output = new object();
            try
            {
                // Try to convert it.
                if (input == null)
                    return false;

                Type arrType = typeof(T);
                if (arrType.IsArray)
                {
                    Type? t = arrType.GetElementType();
                    if (t == null)
                        return false;
                    arrType = t;
                } else if (typeof(IEnumerable).IsAssignableFrom(arrType))
                {
                    // If T implements IEnumerable, get the actual element type from the generic argument.
                    Type[] genericArgs = arrType.GetGenericArguments();
                    if (genericArgs.Length == 1)
                    {
                        arrType = genericArgs[0];
                    } else
                    {
                        throw new InvalidOperationException("Invalid IEnumerable type.");
                    }
                }

                string arrName = arrType.Name;
                if (arrName.Trim().EndsWith("[]"))
                    arrName = arrName.Substring(0, arrName.Length - 2);

                StringBuilder sb = new StringBuilder("\"ARR|" + arrName + "|(") ;
                
                foreach (T element in input)
                    sb.Append(Convert.ToString(element) + ",");

                string arrStr = sb.ToString();

                if (arrStr.EndsWith(","))
                    arrStr = arrStr.Substring(0, arrStr.Length - 1);

                arrStr += ")";

                output = arrStr;
            } catch (Exception)
            {
                return false;
            }
            return true;
            */
            #endregion

            output = new object();
            try
            {
                // Try to convert it.
                if (input == null)
                    return false;

                Type arrType = typeof(T);
                if (arrType.IsArray)
                {
                    Type? t = arrType.GetElementType();
                    if (t == null)
                        return false;
                    arrType = t;
                } 
                else if (typeof(IEnumerable).IsAssignableFrom(arrType))
                {
                    // If T implements IEnumerable, get the actual element type from the generic argument.
                    Type[] genericArgs = arrType.GetGenericArguments();
                    if (genericArgs.Length == 1)
                        arrType = genericArgs[0];
                    else
                        return false;
                }

                string arrName = arrType.Name;
                if (arrName.Trim().EndsWith("[]"))
                    arrName = arrName.Substring(0, arrName.Length - 2);

                // Serialize the input array using Newtonsoft.Json
                StringBuilder arrSb = new StringBuilder("[");
                foreach (T item in input)
                {
                    string str = JsonConvert.SerializeObject(item);
                    if (str.StartsWith("["))
                        str = str.Substring(1);
                    if (str.EndsWith("]"))
                        str = str.Substring(0, str.Length - 1);

                    arrSb.Append(str + ",");
                }

                string arrStr = arrSb.ToString().Substring(0, arrSb.Length - 1) + "]";

                // Replace the array name with the correct name
                output = $"ARR|{arrStr}";
            } catch (Exception)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Converts an Array input to a SQLite-compatible <see cref="string"/>.
        /// </summary>
        /// <typeparam name="T">The type of the array to output.</typeparam>
        /// <param name="input">The input <see cref="string"/> from SQLite.</param>
        /// <param name="output">The output array.</param>
        /// <returns>If the procedure was successful.</returns>
        public static object? FromArray<T>(IEnumerable<T> input) where T : notnull
        {
            object? output = null;
            FromArray(input, out output);
            return output;
        }

        /// <summary>
        /// Converts an Array input to a SQLite-compatible <see cref="string"/>.
        /// </summary>
        /// <typeparam name="T">The type of the array to output.</typeparam>
        /// <param name="input">The input <see cref="string"/> from SQLite.</param>
        /// <param name="output">The output array.</param>
        /// <returns>If the procedure was successful.</returns>
        public static IEnumerable<T> ToArray<T>(string input) where T : notnull
        {
            IEnumerable<T> output;
            ToArray(input, out output);
            return output;
        }

        /// <summary>
        /// Returns the <b>estimated</b> SQLite-type and adjusted value for an <see cref="object"/> 
        /// input. Not intended for public-use
        /// </summary>
        /// <param name="obj">The <see cref="object"/> to input.</param>
        /// <param name="sqlType">The output of the SQLite-type.</param>
        /// <param name="sqlValue">The output of the SQLite-value.</param>
        /// <returns>If the procedure was successful.</returns>
        private static bool GetSQLType(object obj, out string sqlType, out object sqlValue)
        {
            sqlType = "TEXT";
            sqlValue = obj;

            if (obj == null)
                return false;

            Type objType = obj.GetType();

            if (objType.IsArray || (objType.GetInterface(nameof(IEnumerable)) != null && objType != typeof(string)))
            {
                try
                {
                    IEnumerable<object> enumerable = ((IEnumerable)obj).Cast<object>();
                    return FromArray(enumerable, out sqlValue);
                } catch (Exception)
                {
                    return false;
                }
            }

            switch (Type.GetTypeCode(objType))
            {
                case TypeCode.Double:
                    sqlValue = Convert.ToDouble(obj);
                    sqlType = "REAL";
                    break;

                case TypeCode.Int32:
                    sqlValue = Convert.ToInt32(obj);
                    sqlType = "INTEGER";
                    break;

                case TypeCode.Int64:
                    sqlValue = Convert.ToInt64(obj);
                    sqlType = "INTEGER";
                    break;

                case TypeCode.Byte:
                    sqlValue = Convert.ToByte(obj);
                    sqlType = "INTEGER";
                    break;

                case TypeCode.String:
                    sqlType = "TEXT";
                    sqlValue = Convert.ToString(obj) ?? obj;
                    break;

                case TypeCode.Boolean:
                    bool val = Convert.ToBoolean(obj);
                    sqlType = "INTEGER";
                    sqlValue = val ? 1 : 0;
                    break;

                case TypeCode.DateTime:
                    DateTime time = Convert.ToDateTime(obj);
                    sqlType = "TEXT";
                    sqlValue = time.ToString();
                    break;
            }

            return true;
        }

        /// <param name="obj">The <see cref="object"/> to test for.</param>
        /// <param name="tblName">The SQLite table name.</param>
        /// <returns>If the "tblName" table contains a specific property.</returns>
        public bool ContainsObject<T>(T obj, string tblName="") where T : class
        {
            if (string.IsNullOrEmpty(tblName))
                tblName = obj.GetType().Name;

            List<PragmaTableInfo> tableInfo = GetColumnsForTable(tblName);

            foreach (PropertyInfo prop in obj.GetType().GetProperties())
            {
                bool hasProperty = false;
                foreach (PragmaTableInfo info in tableInfo)
                {
                    if (info.Name.Equals(prop.Name))
                    {
                        hasProperty = true;
                        break;
                    }
                }
                if (!hasProperty)
                {
                    try
                    {
                        SQLiteAttribute? attr = prop.GetCustomAttribute(typeof(SQLiteAttribute)) as SQLiteAttribute;
                        if (attr == null)
                            continue;
                        if (!attr.Ignore)
                            return false;
                    } catch (Exception) { }
                    return false;
                }
            }      
            return true;
        }

        /// <summary>
        /// Runs SQLite-prechecks; used for Adding, Updating, and Deleting objects.
        /// </summary>
        /// <typeparam name="T">A non-nullable <see cref="class"/></typeparam>
        /// <param name="obj">The <see cref="object"/> to use for name generation and validating.</param>
        /// <param name="tblName">The input table name; may be modified.</param>
        /// <returns>If the procedure was successful.</returns>
        private bool RunSQLPrechecks<T>(T obj, ref string tblName) where T : class
        {
            if (string.IsNullOrEmpty(tblName))
                tblName = obj.GetType().Name;

            if (!OpenConnection(out _))
                return false;
            if (!CreateSQLTable(obj.GetType(), tblName))
                return false;
            if (!ContainsObject(obj, tblName))
                return false;
            return true;
        }


        /// <summary>
        /// Creates a SQLite table with a <see cref="Type"/> and optional Name.
        /// </summary>
        /// <param name="objType">The <see cref="Type"/> of the <see cref="object"/></param>
        /// <param name="tblName">The optional SQLite table name.</param>
        /// <returns>If the procedure was successful.</returns>
        public bool CreateSQLTable(Type objType, string tblName="")
        {
            if (string.IsNullOrEmpty(tblName))
                tblName = objType.Name;

            if (!OpenConnection(out _))
                return false;

            if (string.IsNullOrEmpty(tblName))
                tblName = objType.Name;

            if (!GetTables().Contains(tblName))
            {
                SQLiteCommand? cmd;
                if (!Generation.GenerateCreateCommand(objType, tblName, out cmd) || cmd == null)
                    return false;
                // Run the create command first.
                ExecuteCommand(cmd);
                if (!GetTables().Contains(tblName))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Adds an <see cref="object"/> to the specified SQL table; creates if doesn't exist.
        /// </summary>
        /// <param name="obj">The <see cref="object"/> to add.</param>
        /// <param name="tblName">The optional SQLite table name.</param>
        /// <returns>If the procedure was successful.</returns>
        public bool AddSQLObject<T>(T obj, string tblName="") where T : class
        {
            if (!RunSQLPrechecks(obj, ref tblName))
                return false;

            SQLiteCommand? insert;
            if (!Generation.GenerateInsertCommand(obj, tblName, out insert) || insert == null)
                return false;
            return ExecuteCommand(insert) > 0;
        }

        /// <summary>
        /// Updates the <see cref="object"/> on the specified SQL table; based off <b>primary key</b>.
        /// </summary>
        /// <param name="obj">The <see cref="object"/> to update.</param>
        /// <param name="tblName">The optional SQLite table name.</param>
        /// <returns>If the procedure was successful.</returns>
        public bool UpdateSQLObject<T>(T obj, string tblName="") where T : class
        {
            if (!RunSQLPrechecks(obj, ref tblName))
                return false;

            SQLiteCommand? update;
            if (!Generation.GenerateUpdateCommand(obj, tblName, out update) || update == null)
                return false;
            return ExecuteCommand(update) > 0;
        }

        public bool DeleteSQLObject<T>(T obj, string tblName="") where T : class
        {
            if (!RunSQLPrechecks(obj, ref tblName))
                return false;

            SQLiteCommand? delete;
            if (!Generation.GenerateDeleteCommand(obj, tblName, out delete) || delete == null)
                return false;
            return ExecuteCommand(delete) > 0;
        }

    }
}