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


using System.Data.SQLite;

namespace SQLiteWrapper
{
    public class PragmaTableInfo
    {
        [SQLite(Column = "cid")]
        public int CID { set; get; }

        [SQLite(Column = "name")]
        public string Name { set; get; }

        [SQLite(Column = "type")]
        public string DataType { set; get; }

        [SQLite(Column = "dflt value")]
        public object DFLT { set; get; }

        [SQLite(Column = "notnull")]
        public bool IsNotNull { set; get; }

        [SQLite(Column = "pk")]
        public bool IsPrimaryKey { set; get; }

        /// <summary>
        /// Constructs a new <see cref="PragmaTableInfo"/>.
        /// </summary>
        /// <param name="rdr">The <see cref="SQLiteDataReader"/> to pull the current row from.</param>
        public PragmaTableInfo(SQLiteDataReader rdr)
        {
            CID = SQLiteManager.SafeConvert(rdr, "cid", 0);
            Name = SQLiteManager.SafeConvert(rdr, "name", string.Empty);
            DataType = SQLiteManager.SafeConvert(rdr, "type", string.Empty);
            IsNotNull = SQLiteManager.SafeConvert(rdr, "notnull", false);
            DFLT = SQLiteManager.SafeConvert(rdr, "dflt value", new object()); // TODO: change?
            IsPrimaryKey = SQLiteManager.SafeConvert(rdr, "pk", false);
        }
    }
}
