/*
 * Attributes for SQLite Manager V2
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

namespace SQLiteWrapper
{
    /// <summary>
    /// This <see cref="SQLiteAttribute"/> is used to instruct the <see cref="SQLiteManager"/> of desired properties.
    /// </summary>
    public class SQLiteAttribute : Attribute
    {
        /// <summary>
        /// If the Property is a Primary Key in the table. It <b>must be UNIQUE and NON-NULL</b>. REQUIRED for 
        /// updating and deleting other entries in the <see cref="object"/>.
        /// </summary>
        public bool PrimaryKey { set; get; } = false;

        /// <summary>
        /// If the Property is not allowed to be NULL.
        /// </summary>
        public bool NotNull { set; get; } = false;

        /// <summary>
        /// If the Property is supposed to be ignored and not processed by the <see cref="SQLiteManager"/>.
        /// </summary>
        public bool Ignore { set; get; } = false;

        /// <summary>
        /// The <b>optional</b> default value for the Property.
        /// </summary>
        public object? Default { set; get; } = null;

        /// <summary>
        /// The <b>optional</b> SQLite column name for the Property.
        /// </summary>
        public string? Column { set; get; }

        /// <summary>
        /// Initializes a new <see cref="SQLiteAttribute"/>.
        /// </summary>
        public SQLiteAttribute()
        { }
    }
}
