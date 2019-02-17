using DBLib;
using Microsoft.Extensions.Logging;
using Protein_Interaction.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Protein_Interaction.Operations
{
    public class HomeOperations
    {
        private readonly ILogger<HomeOperations> logger;

        private static CountModel count = null;
        private static Task loadTsk = null;
        private static object loadLock = new object();

#if DEBUG
        const string dbServer = DBHandler.Servers.test;
#else
        const string dbServer = DBHandler.Servers.azureProtein;
#endif

        public HomeOperations(ILoggerFactory factory)
        {
            logger = factory.CreateLogger<HomeOperations>();
        }

        public void loadCount()
        {
            if ((count == null || count.id == null) && (loadTsk == null || loadTsk.IsCompleted))
                lock (loadLock)
                    if ((count == null || count.id == null) && (loadTsk == null || loadTsk.IsCompleted))
                        loadTsk = _loadCount();
        }

        public CountModel getCount()
        {
            loadCount();
            if (loadTsk != null && !loadTsk.IsCompleted)
                loadTsk.Wait();
            return count;
        }

        private async Task _loadCount()
        {
            #region query
            const string geneIDQuery = @"select count (1) from GeneID;";
            const string synonQuery = @"select count(1) from Synon;";
            const string refQuery = @"select count(1) from Ref;";
            #endregion

            var geneIDTsk = DBHandler.getInt32Async(geneIDQuery, dbServer);
            var synonTsk = DBHandler.getInt32Async(synonQuery, dbServer);
            var refTsk = DBHandler.getInt32Async(refQuery, dbServer);

            CountModel newCount = new CountModel();
            newCount.id = (await geneIDTsk.ConfigureAwait(false) ?? 0).ToString("N0");
            newCount.syn = (await synonTsk.ConfigureAwait(false) ?? 0).ToString("N0");
            newCount.nref = (await refTsk.ConfigureAwait(false) ?? 0).ToString("N0");
            Interlocked.Exchange(ref count, newCount);
        }
    }
}
