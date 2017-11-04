using Microsoft.Isam.Esent.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace HelloWorld.Universal
{

    public sealed partial class MainPage : Page
    {
        private Instance _instance;
        private string _instancePath;
        private string _databasePath;
        private const string DatabaseName = "Database";

        public MainPage()
        {
            this.InitializeComponent();

            CreateInstance();
   
        }


        private async void butStart_Click(object sender, RoutedEventArgs e)
        {
            await TryCreateDatabase();
            AddEvent(new Event() { Id = Guid.NewGuid(), Description = "Test", Price = 100.00, StartTime = DateTime.UtcNow });
            UpdateCount();
        }
        private async Task TryCreateDatabase()
        {
            if (await DoesDatabaseNotExist())
            {
                CreateDatabase();
            }
        }
        private void UpdateCount() {
            var foundEvents = GetAllEvents();
            lblCount.Text = foundEvents.Count().ToString();
        }

        public void CreateInstance()
        {
            Windows.Storage.StorageFolder localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
            
            _instancePath = Path.Combine(localFolder.Path, DatabaseName);
            _databasePath = Path.Combine(_instancePath, "database.edb");
            _instance = new Instance(_databasePath);

            // configure instance
            _instance.Parameters.CreatePathIfNotExist = true;
            _instance.Parameters.TempDirectory = Path.Combine(_instancePath, "temp");
            _instance.Parameters.SystemDirectory = Path.Combine(_instancePath, "system");
            _instance.Parameters.LogFileDirectory = Path.Combine(_instancePath, "logs");
            _instance.Parameters.Recovery = true;
            _instance.Parameters.CircularLog = true;

            _instance.Init();
        }

        public async Task<bool> DoesDatabaseNotExist() {
            Windows.Storage.StorageFolder localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
            var foundDBFolder = await localFolder.GetFolderAsync(DatabaseName);
            var foundDBFile = await foundDBFolder.TryGetItemAsync("database.edb");
            if (foundDBFile != null) return false;
            return true;
        }

        public void CreateDatabase()
        {
            using (var session = new Session(_instance))
            {
                // create database file
                JET_DBID database;
                Api.JetCreateDatabase(session, _databasePath, null, out database, CreateDatabaseGrbit.None);

                // create database schema
                using (var transaction = new Transaction(session))
                {
                    JET_TABLEID tableid;
                    Api.JetCreateTable(session, database, "Events", 1, 100, out tableid);

                    // ID
                    JET_COLUMNID columnid;
                    Api.JetAddColumn(session, tableid, "Id",
                           new JET_COLUMNDEF
                           {
                               cbMax = 16,
                               coltyp = JET_coltyp.Binary,
                               grbit = ColumndefGrbit.ColumnFixed | ColumndefGrbit.ColumnNotNULL
                           }, null, 0, out columnid);
                    // Description
                    Api.JetAddColumn(session, tableid, "Description",
                           new JET_COLUMNDEF
                           {
                               coltyp = JET_coltyp.LongText,
                               cp = JET_CP.Unicode,
                               grbit = ColumndefGrbit.None
                           }, null, 0, out columnid);
                    // Price
                    Api.JetAddColumn(session, tableid, "Price",
                           new JET_COLUMNDEF
                           {
                               coltyp = JET_coltyp.IEEEDouble,
                               grbit = ColumndefGrbit.None
                           }, null, 0, out columnid);
                    // StartTime
                    Api.JetAddColumn(session, tableid, "StartTime",
                           new JET_COLUMNDEF
                           {
                               coltyp = JET_coltyp.Currency,
                               grbit = ColumndefGrbit.None
                           }, null, 0, out columnid);

                    // Define table indices
                    var indexDef = "+Id\0\0";
                    Api.JetCreateIndex(session, tableid, "id_index",
                                       CreateIndexGrbit.IndexPrimary, indexDef, indexDef.Length, 100);

                    indexDef = "+Price\0\0";
                    Api.JetCreateIndex(session, tableid, "price_index",
                                       CreateIndexGrbit.IndexDisallowNull, indexDef, indexDef.Length, 100);

                    transaction.Commit(CommitTransactionGrbit.None);
                }

                Api.JetCloseDatabase(session, database, CloseDatabaseGrbit.None);
                Api.JetDetachDatabase(session, _databasePath);
            }
        }


        private IList<Event> ExecuteInTransaction(Func<Session, Table, IList<Event>> dataFunc)
        {
            IList<Event> results;
            using (var session = new Session(_instance))
            {
                JET_DBID dbid;
                Api.JetAttachDatabase(session, _databasePath, AttachDatabaseGrbit.None);
                Api.JetOpenDatabase(session, _databasePath, String.Empty, out dbid, OpenDatabaseGrbit.None);
                using (var transaction = new Transaction(session))
                {
                    using (var table = new Table(session, dbid, "Events", OpenTableGrbit.None))
                    {
                        results = dataFunc(session, table);
                    }

                    transaction.Commit(CommitTransactionGrbit.None);
                }
            }

            return results;
        }


        public void AddEvent(Event ev)
        {
            ExecuteInTransaction((session, table) =>
            {
                using (var updater = new Update(session, table, JET_prep.Insert))
                {
                    var columnId = Api.GetTableColumnid(session, table, "Id");
                    Api.SetColumn(session, table, columnId, ev.Id);

                    var columnDesc = Api.GetTableColumnid(session, table, "Description");
                    Api.SetColumn(session, table, columnDesc, ev.Description, Encoding.Unicode);

                    var columnPrice = Api.GetTableColumnid(session, table, "Price");
                    Api.SetColumn(session, table, columnPrice, ev.Price);

                    var columnStartTime = Api.GetTableColumnid(session, table, "StartTime");
                    Api.SetColumn(session, table, columnStartTime, DateTime.Now.Ticks);

                    updater.Save();
                }
                return null;
            });
        }

        public void Delete(Guid id)
        {
            ExecuteInTransaction((session, table) =>
            {
                Api.JetSetCurrentIndex(session, table, null);
                Api.MakeKey(session, table, id, MakeKeyGrbit.NewKey);
                if (Api.TrySeek(session, table, SeekGrbit.SeekEQ))
                {
                    Api.JetDelete(session, table);
                }
                return null;
            });
        }


        public IList<Event> GetAllEvents()
        {
            return ExecuteInTransaction((session, table) =>
            {
                var results = new List<Event>();
                if (Api.TryMoveFirst(session, table))
                {
                    do
                    {
                        results.Add(GetEvent(session, table));
                    }
                    while (Api.TryMoveNext(session, table));
                }
                return results;
            });
        }

        private Event GetEvent(Session session, Table table)
        {
            var ev = new Event();

            var columnId = Api.GetTableColumnid(session, table, "Id");
            ev.Id = Api.RetrieveColumnAsGuid(session, table, columnId) ?? Guid.Empty;

            var columnDesc = Api.GetTableColumnid(session, table, "Description");
            ev.Description = Api.RetrieveColumnAsString(session, table, columnDesc, Encoding.Unicode);

            var columnPrice = Api.GetTableColumnid(session, table, "Price");
            ev.Price = Api.RetrieveColumnAsDouble(session, table, columnPrice) ?? 0;

            var columnStartTime = Api.GetTableColumnid(session, table, "StartTime");
            var ticks = Api.RetrieveColumnAsInt64(session, table, columnStartTime);
            if (ticks.HasValue)
                ev.StartTime = new DateTime(ticks.Value);

            return ev;
        }


        public IList<Event> GetEventsById(Guid id)
        {
            return ExecuteInTransaction((session, table) =>
            {
                var results = new List<Event>();
                Api.JetSetCurrentIndex(session, table, null);
                Api.MakeKey(session, table, id, MakeKeyGrbit.NewKey);
                if (Api.TrySeek(session, table, SeekGrbit.SeekEQ))
                {
                    results.Add(GetEvent(session, table));
                }
                return results;
            });
        }


        public IList<Event> GetEventsForPriceRange(double minPrice, double maxPrice)
        {
            return ExecuteInTransaction((session, table) =>
            {
                var results = new List<Event>();

                Api.JetSetCurrentIndex(session, table, "price_index");
                Api.MakeKey(session, table, minPrice, MakeKeyGrbit.NewKey);

                if (Api.TrySeek(session, table, SeekGrbit.SeekGE))
                {
                    Api.MakeKey(session, table, maxPrice, MakeKeyGrbit.NewKey);
                    Api.JetSetIndexRange(session, table,
                          SetIndexRangeGrbit.RangeUpperLimit | SetIndexRangeGrbit.RangeInclusive);

                    do
                    {
                        results.Add(GetEvent(session, table));
                    }
                    while (Api.TryMoveNext(session, table));
                }
                return results;
            });
        }

    }



    public class Event {
        public Guid Id { get; set; }
        public string Description { get; set; }
        public DateTime StartTime { get; set; }
        public double Price { get; set; }
    }
}
