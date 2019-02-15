using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Protein_Interaction.Data;
using Protein_Interaction.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Protein_Interaction.Controllers
{
    public class SearchController : Controller
    {
        //DLLEXP void buildGraph(int32_t const data[], int32_t nData)
        [DllImport("gene.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void buildGraph(int[] data, int nData);

        //DLLEXP void enrichGenes(uint32_t tid, int32_t** vertex, float** coordinates, int32_t* vcount,
        //  int32_t** edge, int32_t* ecount, int32_t const genelist[], int32_t nQuery)
        [DllImport("gene.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void enrichGenes(int tid, out IntPtr vertex, out IntPtr coordinates, out int vcount,
            out IntPtr edge, out int ecount, int[] genelist, int nQuery);

        //DLLEXP void freeIntArr(int* arr)
        [DllImport("gene.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void freeIntArr(IntPtr arr);

        //DLLEXP void freeFloatArr(float* arr)
        [DllImport("gene.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void freeFloatArr(IntPtr arr);

        //DLLEXP void terminateProc(int32_t tid)
        [DllImport("gene.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void terminnateProc(int tid);

        private static ConcurrentDictionary<int, CancellationTokenSource> tasks = new ConcurrentDictionary<int, CancellationTokenSource>();
        private readonly DataContext _context;
        private IMemoryCache _cache;
        
        private static Task tskDB = null;

        static int taskID = 0;
        static ConcurrentDictionary<int, JsonResult> failQueries = new ConcurrentDictionary<int, JsonResult>();

        public IActionResult Multi()
        {
            ViewBag.taskID = Interlocked.Increment(ref taskID);
            return View();
        }
        public IActionResult Single()
        {
            ViewBag.taskID = Interlocked.Increment(ref taskID);
            return View();
        }

        public SearchController(DataContext context, IMemoryCache memoryCache)
        {
            _context = context;
            _cache = memoryCache;
            if (tskDB == null)
                tskDB = LoadMemCache();
        }

        //Check if the Memcache is available. If not, build it by pre-loading the database.
        private async Task LoadMemCache()
        {
            if (GeneRelation == null || geneid == null || idgene == null || CacheUp == null || CacheDown == null || GeneRelation.Count < 200000)
            {
                if (!_cache.TryGetValue("GeneRelation", out GeneRelation) ||
                    !_cache.TryGetValue("geneid", out geneid) ||
                    !_cache.TryGetValue("idgene", out idgene) ||
                    !_cache.TryGetValue("CacheUp", out CacheUp) ||
                    !_cache.TryGetValue("CacheDown", out CacheDown)
                    || GeneRelation == null || geneid == null || idgene == null || CacheUp == null || CacheDown == null || GeneRelation.Count < 200000
                  )
                    await CollectDB();
            }
        }

        //Read from the database to build the dictionaries and populate the memcache.
        private async Task CollectDB()
        {
            Task confTsk = null;
            List<Task> tasks = new List<Task>();
            if (idgene == null || idgene.Count < 10000)
                tasks.Add(collectIdGene());
            if (geneid == null || geneid.Count < 500000)
                tasks.Add(collectGeneID());
            if (GeneRelation == null || CacheUp == null || CacheDown == null || GeneRelation.Count < 200000)
            {
                confTsk = collectConf();
                tasks.Add(confTsk);
            }

            if (confTsk != null)
            {
                await confTsk.ConfigureAwait(false);
                int dataCount = 0;
                int[] dataset = new int[GeneRelation.Count * 3];
                foreach (var key in GeneRelation)
                {
                    dataset[dataCount * 3] = key.Key.Item1;
                    dataset[dataCount * 3 + 1] = key.Key.Item2;
                    dataset[dataCount * 3 + 2] = key.Value;
                    dataCount++;
                }
                buildGraph(dataset, dataCount);
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task collectIdGene()
        {
            var query = await (from row in _context.GeneID
                               select row).ToArrayAsync();
            idgene = new Dictionary<int, string>();
            lock (idgene)
                foreach (var item in query)
                    idgene[item.ID] = item.Symbol;
            _cache.Set("idgene", idgene);
        }

        private async Task collectGeneID()
        {
            var query = await (from row in _context.Synon
                               select row).ToArrayAsync();
            geneid = new Dictionary<string, int>();
            lock (geneid)
                foreach (var item in query)
                    geneid[item.Symbol] = item.ID;
            _cache.Set("geneid", geneid);
        }

        private async Task collectConf()
        {
            GeneRelation = new Dictionary<Tuple<int, int>, int>();
            CacheUp = new Dictionary<int, List<int>>();
            CacheDown = new Dictionary<int, List<int>>();
            var queryReactomes = await (from row in _context.Reactomes
                                        orderby row.Confidence descending
                                        select row).ToArrayAsync();
            lock (GeneRelation)
                foreach (var item in queryReactomes)
                {
                    int id1 = item.Gene1, id2 = item.Gene2;
                    GeneRelation[new Tuple<int, int>(id1, id2)] = item.Confidence;
                    if (!CacheDown.ContainsKey(id1))
                        CacheDown[id1] = new List<int>();
                    CacheDown[id1].Add(id2);
                    if (!CacheUp.ContainsKey(id2))
                        CacheUp[id2] = new List<int>();
                    CacheUp[id2].Add(id1);
                }
            _cache.Set("GeneRelation", GeneRelation);
            _cache.Set("CacheUp", CacheUp);
            _cache.Set("CacheDown", CacheDown);
        }

        private JsonResult _SearchSingle(string query, uint updepth, uint ddepth, uint width, int instanceID, CancellationToken ct, string logStr)
        {
            bool process = true;
            query = query.Trim().ToUpper();
            if (!geneid.ContainsKey(query))
                return FailedQuery(1);
            int queryID = geneid[query];
            bool found = false;
            var queryNode = new Node(null, queryID, false);
            List<List<Node>> levels = new List<List<Node>> { new List<Node> { queryNode } };
            int xmax = 1;
            //BFS to search for all upstream genes
            int newx = DbQuery(ref process, queryID, updepth, levels, 1, width, CacheUp, false, ct);
            if (!process)
                return FailedQuery(2);
            if (newx > 0)
            {
                found = true;
                if (newx > xmax)
                    xmax = newx;
            }
            while (levels[levels.Count - 1].Count == 0)
                levels.RemoveAt(levels.Count - 1);
            levels.Reverse();
            int upperCount = levels.Count;
            if (ct.IsCancellationRequested)
                ct.ThrowIfCancellationRequested();
            //BFS to search for all downstream genes
            newx = DbQuery(ref process, queryID, ddepth, levels, (uint)upperCount, width, CacheDown, true, ct);
            if (!process)
                return FailedQuery(2);
            if (newx > 0)
            {
                found = true;
                if (newx > xmax)
                    xmax = newx;
            }
            if (!found)
                return FailedQuery(1);
            if (ct.IsCancellationRequested)
                ct.ThrowIfCancellationRequested();
            while (levels[levels.Count - 1].Count == 0)
                levels.RemoveAt(levels.Count - 1);
            //assign coordinates to nodes 
            int totalCount = 0;
            Dictionary<Node, Tuple<double, int>> NodeCoord = new Dictionary<Node, Tuple<double, int>>();
            for (int y = 0; y < levels.Count; y++)
            {
                double interval = (double)xmax / (levels[y].Count + 1);
                for (int x = 0; x < levels[y].Count; x++)
                {
                    double xpos = (x + 1) * interval;
                    NodeCoord[levels[y][x]] = new Tuple<double, int>(xpos, y + 1);
                    totalCount++;
                }
            }
            if (ct.IsCancellationRequested)
                ct.ThrowIfCancellationRequested();
            List<Node> idNode = new List<Node>();
            Dictionary<Node, int> nodeId = new Dictionary<Node, int>();
            HashSet<Tuple<int, int>> edges = new HashSet<Tuple<int, int>>();
            for (int y = 0; y < levels.Count; y++)
                for (int x = 0; x < levels[y].Count; x++)
                {
                    int id1;
                    if (nodeId.ContainsKey(levels[y][x]))
                        id1 = nodeId[levels[y][x]];
                    else
                    {
                        idNode.Add(levels[y][x]);
                        id1 = idNode.Count - 1;
                        nodeId[levels[y][x]] = id1;
                    }
                    if (levels[y][x].next != null)
                    {
                        int id2;
                        if (nodeId.ContainsKey(levels[y][x].next))
                            id2 = nodeId[levels[y][x].next];
                        else
                        {
                            idNode.Add(levels[y][x].next);
                            id2 = idNode.Count - 1;
                            nodeId[levels[y][x].next] = id2;
                        }
                        edges.Add(new Tuple<int, int>(id1, id2));
                    }
                }

            GraphModel data = new GraphModel()
            {
                status = 0,
                xmax = xmax - 1,
                ymax = levels.Count + 1
            };
            data.query.Add(nodeId[queryNode]);
            for (int i = 0; i < idNode.Count; i++)
            {
                data.vertex.Add(new string[3] {
                    idgene[idNode[i].geneID],
                     NodeCoord[idNode[i]].Item1.ToString("F4"),
                     NodeCoord[idNode[i]].Item2.ToString("F4")
                });
            }

            foreach (var e in edges)
            {
                if (idNode[e.Item1].reverse)
                {
                    data.edge.Add(new int[3]
                    {
                        e.Item2,
                        e.Item1,
                        GeneRelation[new Tuple<int, int>(idNode[e.Item2].geneID, idNode[e.Item1].geneID)]
                    });
                }
                else
                {
                    data.edge.Add(new int[3]
                    {
                        e.Item1,
                        e.Item2,
                        GeneRelation[new Tuple<int, int>(idNode[e.Item1].geneID, idNode[e.Item2].geneID)]
                    });
                }
            }

            string refKey = logStr + ", type: ref";
            if (!_cache.TryGetValue(refKey, out List<Tuple<int, int>> target))
            {
                target = new List<Tuple<int, int>>();
                for (int y = 0; y < levels.Count; y++)
                    for (int x = 0; x < levels[y].Count; x++)
                        if (levels[y][x].next != null)
                        {
                            if (levels[y][x].reverse)
                                target.Add(new Tuple<int, int>(levels[y][x].next.geneID, levels[y][x].geneID));
                            else
                                target.Add(new Tuple<int, int>(levels[y][x].geneID, levels[y][x].next.geneID));
                        }
                _cache.Set(refKey, target, new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromHours(1)));
            }
            data.refKey = refKey;
            return Json(data);
        }

        [HttpPost]
        public async Task<JsonResult> SearchSingle(string query, uint updepth, uint ddepth, uint width, int instanceID)
        {
            if (tskDB != null)
                await tskDB.ConfigureAwait(false);

            string logStr = "query: " + query + ", upstream depth: " + updepth.ToString() + ", downstream depth: " + ddepth.ToString() + ", width: " + width.ToString();
            Task logTask = writeLog(logStr, instanceID);

#if DEBUG
            var res = getSingle(query, updepth, ddepth, width, instanceID, logStr);
#else
            if (!_cache.TryGetValue(logStr, out JsonResult res))
            {
                res = getSingle(query, updepth, ddepth, width, instanceID, logStr);
                _cache.Set(logStr, res, new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromHours(1)));
            }
#endif
            return res;
        }

        private JsonResult getSingle(string query, uint updepth, uint ddepth, uint width, int instanceID, string logStr)
        {
            var ts = new CancellationTokenSource();
            CancellationToken ct = ts.Token;
            tasks[instanceID] = ts;
            var searchTask = Task.Factory.StartNew(() => _SearchSingle(query, updepth, ddepth, width, instanceID, ct, logStr));
            JsonResult res;
            try
            {
                res = searchTask.Result;
            }
            catch (OperationCanceledException)
            {
                res = null;
            }

            tasks.Remove(instanceID, out CancellationTokenSource temp);
            temp.Dispose();

            return res;
        }

        private async Task<JsonResult> _SearchMulti(HashSet<int> queryId, int instanceID, CancellationToken ct, string logStr)
        {
            //Call C++ library to build graph
            int[] genelist = queryId.ToArray();
            int n = queryId.Count;
            int vcount = 0, ecount = 0;
            IntPtr pvertex = IntPtr.Zero;
            IntPtr pcoordinates = IntPtr.Zero;
            IntPtr pedge = IntPtr.Zero;
            Task task = Task.CompletedTask;
            float[] coordinates = Array.Empty<float>();
            int[] vertex = Array.Empty<int>();
            int[] edge = Array.Empty<int>();
            try
            {
                using (var reg = ct.Register(() => terminnateProc(instanceID)))
                    enrichGenes(
                        instanceID,
                        out pvertex, out pcoordinates, out vcount, out pedge, out ecount,
                        genelist, n);
                vertex = new int[vcount];
                coordinates = new float[vcount * 2];
                edge = new int[ecount * 2];
                Marshal.Copy(pvertex, vertex, 0, vcount);
                Marshal.Copy(pcoordinates, coordinates, 0, vcount * 2);
                Marshal.Copy(pedge, edge, 0, ecount * 2);
            }
            finally
            {
                freeIntArr(pvertex);
                freeFloatArr(pcoordinates);
                freeIntArr(pedge);
            }
            //Done C++ call
            if (vcount == 0 || ecount == 0)
                return FailedQuery(1);
            double xmax = 0, ymax = 0;
            for (int i = 0; i < vcount; i++)
            {
                coordinates[i * 2] *= 1.4f;
                coordinates[i * 2 + 1] *= 1.4f;
                if (coordinates[i * 2] > xmax)
                    xmax = coordinates[i * 2];
                if (coordinates[i * 2 + 1] > ymax)
                    ymax = coordinates[i * 2 + 1];
            }
            xmax++;
            ymax++;
            Dictionary<int, int> vertexCount = new Dictionary<int, int>();
            for (int i = 0; i < vcount; i++)
                vertexCount[vertex[i]] = i;
            if (ct.IsCancellationRequested)
                return null;
            //Build Json
            StringBuilder jsonStr = new StringBuilder();
            StringWriter sw = new StringWriter(jsonStr);
            GraphModel data = new GraphModel()
            {
                status = 0,
                xmax = (float)xmax,
                ymax = (float)ymax
            };
            for (int i = 0; i < vcount; i++)                       //vertices: [symbol, xpos, y]
            {
                data.vertex.Add(new string[3]
                {
                      idgene[vertex[i]],
                      coordinates[i * 2].ToString("F4"),
                      coordinates[i * 2 + 1].ToString("F4")
                });
            }
            for (int i = 0; i < n; i++)
            {
                int vtx = genelist[i];
                if (vertexCount.ContainsKey(vtx))
                    data.query.Add(vertexCount[vtx]);
            }
            for (int i = 0; i < ecount; i++)
            {
                int v1 = edge[i * 2], v2 = edge[i * 2 + 1];
                int count1 = vertexCount[v1], count2 = vertexCount[v2];
                data.edge.Add(new int[3]{
                    count1,
                    count2,
                    GeneRelation[new Tuple<int, int>(v1, v2)]
                });
            }

            string refKey = logStr + ", type: ref";
            if (!_cache.TryGetValue(refKey, out List<Tuple<int, int>> target))
            {
                //Prepare cache        
                target = new List<Tuple<int, int>>();
                for (int i = 0; i < ecount; i++)
                    target.Add(new Tuple<int, int>(edge[i * 2], edge[i * 2 + 1]));
                _cache.Set(refKey, target, new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromHours(1)));
            }
            data.refKey = refKey;
            return Json(data);
        }

        [HttpPost]
        public async Task<JsonResult> SearchMulti(string query, int instanceID)
        {
            if (tskDB != null)
                await tskDB.ConfigureAwait(false);

            HashSet<int> queryId = new HashSet<int>();
            List<string> queryLst = new List<string>();
            int i = 0;
            while (i < query.Length)
            {
                StringBuilder buffer = new StringBuilder();
                while (i < query.Length && Char.IsLetterOrDigit(query[i]))
                {
                    buffer.Append(Char.ToUpper(query[i]));
                    i++;
                }
                if (buffer.Length > 0)
                {
                    string symbol = buffer.ToString();
                    if (geneid.ContainsKey(symbol))
                    {
                        queryId.Add(geneid[symbol]);
                        queryLst.Add(symbol);
                    }
                }
                i++;
            }
            if (queryId.Count == 0)
                return FailedQuery(1);

            string queryLog = string.Join(", ", queryLst);
            Task logTask = writeLog(queryLog, instanceID);

#if DEBUG
            var res = await getMulti(queryId, instanceID, queryLog).ConfigureAwait(false);
#else
            if (!_cache.TryGetValue(queryLog, out JsonResult res))
            {
                res = await getMulti(queryId, instanceID, queryLog).ConfigureAwait(false);
                _cache.Set(queryLog, res, new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromHours(1)));
            }
#endif
            return res;
        }

        private async Task<JsonResult> getMulti(HashSet<int> queryId, int instanceID, string logStr)
        {
            var tokenSource = new CancellationTokenSource();
            CancellationToken ct = tokenSource.Token;
            var searchTask = _SearchMulti(queryId, instanceID, ct, logStr);
            tasks[instanceID] = tokenSource;
            JsonResult res;
            try
            {
                res = await searchTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                res = null;
            }
            tasks.Remove(instanceID, out CancellationTokenSource temp);
            temp.Dispose();

            return res;
        }

        [HttpGet]
        public void CancelTask(int id)
        {
            if (tasks.ContainsKey(id) && tasks[id] != null)
                tasks[id].Cancel();
        }

        private int DbQuery(ref bool process, int query, uint depth, List<List<Node>> levels, uint initial, uint width, Dictionary<int, List<int>> db, bool reverse, CancellationToken ct)
        {
            int xmax = 0;
            HashSet<int> used = new HashSet<int> { query };
            for (int i = (int)initial; i < depth + initial && levels[i - 1].Count > 0; i++)
            {
                if (ct.IsCancellationRequested)
                    return 0;
                levels.Add(new List<Node>());
                foreach (var node in levels[i - 1])
                {
                    var gene = node.geneID;
                    if (db.ContainsKey(gene))
                    {
                        List<Tuple<int, int>> selection = new List<Tuple<int, int>>();
                        int count = 0;
                        foreach (var reGene in db[gene])
                        {
                            if (count == width)
                                break;
                            if (!used.Contains(reGene))
                            {
                                used.Add(reGene);
                                var newNode = new Node(node, reGene, reverse);
                                levels[i].Add(newNode);
                                count++;
                            }
                        }
                    }
                }
                if (levels[i].Count > xmax)
                    xmax = levels[i].Count;
                if (xmax > 1024 || levels.Count > 1024)
                {
                    process = false;
                    return -1;
                }
            }
            return xmax;
        }

        //status 1: query target not found. 2: query too large to display
        private JsonResult FailedQuery(int status)
        {
            if (failQueries.ContainsKey(status))
                return failQueries[status];
            StringBuilder jsonStr = new StringBuilder();
            StringWriter sw = new StringWriter(jsonStr);
            using (JsonWriter writer = new JsonTextWriter(sw))
            {
                writer.Formatting = Formatting.Indented;
                writer.WriteStartObject();
                writer.WritePropertyName("status");
                writer.WriteValue(status);
                writer.WriteEndObject();
            }
            JsonResult res = new JsonResult(jsonStr.ToString());
            failQueries[status] = res;
            return res;
        }

        [HttpPost]
        public async Task<JsonResult> GetReference(string refKey)
        {
            string referenceModelKey = refKey + ", stage: model";
            if (!_cache.TryGetValue(referenceModelKey, out List<ReferenceModel> model))
            {
                if (!_cache.TryGetValue(refKey, out List<Tuple<int, int>> interactions))
                    return Json("");
                model = await obtainRef(interactions);
                _cache.Set(referenceModelKey, model, new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromHours(1)));
            }
            return Json(model);
        }

        private async Task<List<ReferenceModel>> obtainRef(List<Tuple<int, int>> interactions)
        {
            Dictionary<Tuple<int, int>, List<Tuple<int, string>>> queryItems = new Dictionary<Tuple<int, int>, List<Tuple<int, string>>>();
            IQueryable<RefQuery> query = null;
            foreach (var item in interactions)
                if (query == null)
                    query = from row in _context.Ref
                            where row.Gene1 == item.Item1 && row.Gene2 == item.Item2
                            select new RefQuery(item.Item1, item.Item2, row.RefID, row.Author);
                else
                    query = query.Union(from row in _context.Ref
                                        where row.Gene1 == item.Item1 && row.Gene2 == item.Item2
                                        select new RefQuery(item.Item1, item.Item2, row.RefID, row.Author));
            var qArr = await query.ToArrayAsync();
            foreach (var entry in qArr)
            {
                var pair = new Tuple<int, int>(entry.gene1, entry.gene2);
                if (!queryItems.ContainsKey(pair))
                    queryItems[pair] = new List<Tuple<int, string>>();
                queryItems[pair].Add(new Tuple<int, string>(entry.refID, entry.author));
            }

            List<ReferenceModel> data = new List<ReferenceModel>();
            foreach (var key in queryItems)
            {
                ReferenceModel model = new ReferenceModel()
                {
                    gene1 = key.Key.Item1,
                    gene2 = key.Key.Item2,
                    geneName1 = idgene[key.Key.Item1],
                    geneName2 = idgene[key.Key.Item2]
                };
                List<PaperModel> papers = new List<PaperModel>();
                foreach (var item in key.Value)
                {
                    PaperModel pmdel = new PaperModel()
                    {
                        refID = item.Item1,
                        author = item.Item2
                    };
                    papers.Add(pmdel);
                }
                model.references = papers;
                data.Add(model);
            }
            return data;
        }

        private async Task writeLog(string log, int instanceID)
        {
            try
            {
                await _context.Database.ExecuteSqlCommandAsync(@"
                INSERT INTO QueryLogs (query, instanceID) values (@query, @instanceID)",
                    new SqlParameter("query", log),
                    new SqlParameter("instanceID", instanceID)
                    );
            }
            catch { }   //suppress exceptions to avoid crash due to unhandled exception
        }
    }
}