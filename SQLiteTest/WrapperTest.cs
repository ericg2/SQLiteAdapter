using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using SQLiteWrapper;

namespace SQLiteTest
{
    /// <summary>
    /// This <see cref="WrapperTest"/> class is designed to test various features of the <see cref="SQLiteManager"/>.
    /// </summary>
    [TestFixture]
    public class WrapperTest
    {
        public class TestObject
        {
            public class SerializeItem
            {
                public int One { set; get; } = 1;
                public int Two { set; get; } = 2;
            }

            private static Random random = new Random();

            public static string RandomString(int length)
            {
                const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
                return new string(Enumerable.Repeat(chars, length)
                    .Select(s => s[random.Next(s.Length)]).ToArray());
            }

            public List<SerializeItem> Items { set; get; }
            public string StringTest { set; get; }
            public int IntTest { set; get; }
            public byte ByteTest { set; get; }
            public double DoubleTest { set; get; }
            public bool BooleanTest { set; get; }

            public DateTime DateTest { set; get; }

            public int[] OtherArr { set; get; }

            public TestObject()
            {
                StringTest = RandomString(8);
                IntTest = random.Next(0, int.MaxValue);
                ByteTest = (byte)random.Next(0, byte.MaxValue);
                DoubleTest = random.NextDouble();
                BooleanTest = Convert.ToBoolean(random.Next(0, 1));
                DateTest = DateTime.Today;
                Items = new List<SerializeItem>();

                int c = random.Next(1, 10);
                OtherArr = new int[c];
                for (int i=0; i<c; i++)
                {
                    Items.Add(new SerializeItem()
                    {
                        One = random.Next(1, 10),
                        Two = random.Next(1, 10)
                    });
                    OtherArr[i] = c;
                }
            }
        }

        private SQLiteManager? manager;
        private readonly string DB_NAME = "UnitTest.sqlite";
        private readonly string TABLE_NAME = "objects";

        /// <summary>
        /// The setup method for the test; called before any Test is called.
        /// </summary>
        [OneTimeSetUp]
        public void Initialize()
        {
            // Make sure we start with a clean database.
            try
            { 
                if (Directory.Exists(DB_NAME))
                    Assert.Fail("Database name is a directory!");

                if (File.Exists(DB_NAME))
                {
                    File.Delete(DB_NAME);
                }
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.Message);
            }

            // Initialize the manager.
            manager = new SQLiteManager(DB_NAME);
        }

        [Test, Order(1)]
        public void Test_ValidateIntegrity()
        {
            Assert.IsNotNull(manager, "SQLiteManager is null!");

            Assert.IsTrue(manager.CreateSQLTable(typeof(TestObject), TABLE_NAME), "SQL table creation failed!");

            List<TestObject> rawList = new List<TestObject>();
            for (int i=0; i<50; i++)
            {
                TestObject obj = new TestObject();
                rawList.Add(obj);
                Assert.IsTrue(manager.AddSQLObject(obj, TABLE_NAME), "Failed to insert #" + i + " item!");
            }

            // Attempt to validate the added items.
            List<TestObject> pulledList;
            Assert.IsTrue(manager.FillAllProperties(out pulledList, TABLE_NAME), "Failed to load all Properties from SQL!");
            Assert.That(rawList.Count, Is.EqualTo(pulledList.Count), "Data integrity failure!");

            for (int i=0; i<pulledList.Count; i++)
            {
                bool good = true;
                TestObject o1 = rawList[i];
                TestObject o2 = pulledList[i];

                if (!o1.StringTest.Equals(o2.StringTest))
                    good = false;
                if (o1.IntTest != o2.IntTest)
                    good = false;
                if (o1.ByteTest != o2.ByteTest)
                    good = false;
                if (o1.DoubleTest != o2.DoubleTest)
                    good = false;
                if (o1.BooleanTest != o2.BooleanTest)
                    good = false;
                if (!o1.DateTest.Equals(o2.DateTest))
                    good = false;
                if (o1.Items.Count != o2.Items.Count)
                    good = false;
                Assert.IsTrue(good, "Data integrity failure!");
            }

            manager.Dispose();
            manager = null;
        }

        /// <summary>
        /// The ending method for the test; called after all tests are ran.
        /// </summary>
        [OneTimeTearDown]
        public void End()
        {
            // Delete the database file if it exists; set manager to NULL.
            manager?.Dispose();

            try
            {
                if (File.Exists(DB_NAME))
                    File.Delete(DB_NAME);
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.Message);
            }
        }
    }
}
