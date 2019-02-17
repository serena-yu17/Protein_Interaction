using DBLib;
using Livingstone.Library;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Protein_Interaction.Models;
using ProtoBuf;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Protein_Interaction.Operations
{
    public class GeneGraphOperations
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

        private static Dictionary<GenePair, int> GeneRelation = null;
        private static Dictionary<string, int> geneid = null;
        private static Dictionary<int, string> idgene = null;
        private static Dictionary<int, List<int>> CacheUp = null;
        private static Dictionary<int, List<int>> CacheDown = null;

        private IMemoryCache _cache;
        private ILogger<GeneGraphOperations> logger;

        private static int taskCount = 0;

        private static Task dbTsk = null;
        private static object dbLock = new object();
        private static HashAlgorithm md5 = MD5.Create();

        private static ConcurrentDictionary<int, CancellationTokenSource> cancellation = new ConcurrentDictionary<int, CancellationTokenSource>();

        public GeneGraphOperations(IMemoryCache cache, ILoggerFactory factory)
        {
            _cache = cache;
            logger = factory.CreateLogger<GeneGraphOperations>();
        }

        public int getTaskID()
        {
            return Interlocked.Increment(ref taskCount);
        }

        public void loadDB()
        {
            if ((dbTsk == null || dbTsk.IsCompleted) && isDatasetValid())
                lock (dbLock)
                    if ((dbTsk == null || dbTsk.IsCompleted) && isDatasetValid())
                        dbTsk = _loacDBAsync();
        }

        public void clearCancellationTokenSource(int instanceId)
        {
            if (cancellation.TryRemove(instanceId, out var source))
                source.Dispose();
        }

        public SearchResultModel searchSingle(SingleQueryModel model)
        {
            var logTsk = writeLog(model, model.instanceID);
            if (!isDatasetValid() && dbTsk != null && !dbTsk.IsCompleted)
                dbTsk.Wait();
            model.query = model.query.ToUpper().Trim();
            string refKey = null;
            using (MemoryStream ms = new MemoryStream())
            {
                Serializer.Serialize(ms, model);
                refKey = getHashString(ms);
            }
            if (_cache.TryGetValue(refKey, out SearchResultModel val) && val != null && val.graph != null)
            {
                return val;
            }

            CancellationTokenSource cs = new CancellationTokenSource();
            cancellation[model.instanceID] = cs;
            var ct = cs.Token;

            var data = _searchSingle(model, ct);
            if (!ct.IsCancellationRequested && data != null && data.graph != null)
            {
                data.graph.refKey = refKey;
                _cache.Set(refKey, data);
            }
            clearCancellationTokenSource(model.instanceID);
            return data;
        }

        public SearchResultModel searchMulti(MultiQueryModel model)
        {
            var logTsk = writeLog(model, model.instanceID);
            if (!isDatasetValid() && dbTsk != null && !dbTsk.IsCompleted)
                dbTsk.Wait();

            Array.Sort(model.queries);

            string refKey = null;
            using (MemoryStream ms = new MemoryStream())
            {
                Serializer.Serialize(ms, model);
                refKey = getHashString(ms);
            }
            if (_cache.TryGetValue(refKey, out SearchResultModel val) && val != null && val.graph != null)
            {
                return val;
            }

            CancellationTokenSource cs = new CancellationTokenSource();
            cancellation[model.instanceID] = cs;
            var ct = cs.Token;

            var data = _searchMulti(model, ct);
            if (!ct.IsCancellationRequested && data != null && data.graph != null)
            {
                data.graph.refKey = refKey;
                _cache.Set(refKey, data);
            }
            clearCancellationTokenSource(model.instanceID);
            return data;
        }

        public async Task<ReferenceModel[]> getRef(string searchRef)
        {
            GenePair[] interactions = null;

            if (!_cache.TryGetValue(searchRef, out SearchResultModel resultData) || 
                resultData == null || resultData.referencePairs == null)
                return null;

            interactions = resultData.referencePairs;

            string refKey = null;
            using (MemoryStream ms = new MemoryStream())
            {
                Serializer.Serialize(ms, interactions);
                refKey = getHashString(ms);
            }
            if (_cache.TryGetValue(refKey, out ReferenceModel[] data) && data != null && data.Length != 0)
                return data;

            var references = await _getRef(interactions).ConfigureAwait(false);
            if (references != null && references.Length != 0)
                _cache.Set(refKey, references);
            return references;
        }

        private async Task<ReferenceModel[]> _getRef(GenePair[] interactions)
        {
            #region query
            const string query = @"
            if OBJECT_ID('tempdb..#tempGenes') is not null
	            drop table #tempGenes;

            create table #tempGenes(
	            gene1 int,
	            gene2 int
            );

            insert into #tempGenes(gene1, gene2)
            values @insertVal;

            select r.Gene1, r.Gene2, r.RefID, r.Author
            from Ref r
	            join #tempGenes t on r.Gene1 = t.gene1 and r.Gene2 = t.gene2;";
            #endregion

            //synthesize the query
            Dictionary<string, object> param = new Dictionary<string, object>();
            StringBuilder insertVal = new StringBuilder();
            for (int i = 0; i < interactions.Length; i++)
            {
                if (i != 0)
                    insertVal.Append(",");
                insertVal.Append("(");

                string firstParam = "@f" + (i * 2).ToString();
                param[firstParam] = interactions[i].Gene1;
                insertVal.Append(firstParam);

                insertVal.Append(",");

                string secondParam = "@s" + (i * 2 + 1).ToString();
                param[secondParam] = interactions[i].Gene2;
                insertVal.Append(secondParam);

                insertVal.Append(")");
            }

            var completeQuery = query.Replace("@insertVal", insertVal.ToString());
            Dictionary<GenePair, List<(int, string)>> queryItems = new Dictionary<GenePair, List<(int, string)>>();
            List<List<object>> rawData = new List<List<object>>();

            await DBHandler.getDataListAsync(null, rawData, null, completeQuery, DBHandler.Servers.azureProtein).ConfigureAwait(false);

            if (rawData.Count != 0)
                foreach (var row in rawData)
                {
                    var pair = new GenePair((int)row[0], (int)row[1]);
                    if (!queryItems.ContainsKey(pair))
                        queryItems[pair] = new List<(int, string)>();
                    queryItems[pair].Add(((int)row[2], row[3] as string));
                }

            List<ReferenceModel> data = new List<ReferenceModel>();
            foreach (var kp in queryItems)
            {
                ReferenceModel model = new ReferenceModel()
                {
                    gene1 = kp.Key.Gene1,
                    gene2 = kp.Key.Gene2,
                    geneName1 = idgene[kp.Key.Gene1],
                    geneName2 = idgene[kp.Key.Gene2]
                };
                List<PaperModel> papers = new List<PaperModel>();
                foreach (var item in kp.Value)
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
            return data.ToArray();
        }

        private bool isDatasetValid()
        {
            return GeneRelation != null && geneid != null && idgene != null && CacheUp != null && CacheDown != null && GeneRelation.Count > 200000;
        }

        private SearchResultModel _searchSingle(SingleQueryModel model, CancellationToken ct)
        {
            bool process = true;
            var query = model.query.Trim().ToUpper();
            if (!geneid.ContainsKey(query))
                return new SearchResultModel(null, new Failure(1), null);
            int queryID = geneid[query];
            bool found = false;
            var queryNode = new Node(null, queryID, false);
            List<List<Node>> levels = new List<List<Node>> { new List<Node> { queryNode } };
            int xmax = 1;
            //BFS to search for all upstream genes
            int newx = DbQuery(ref process, queryID, model.updepth, levels, 1, model.width, CacheUp, false, ct);
            if (!process)
                return new SearchResultModel(null, new Failure(2), null);
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
                return null;
            //BFS to search for all downstream genes
            newx = DbQuery(ref process, queryID, model.ddepth, levels, (uint)upperCount, model.width, CacheDown, true, ct);
            if (!process)
                return new SearchResultModel(null, new Failure(2), null);
            if (newx > 0)
            {
                found = true;
                if (newx > xmax)
                    xmax = newx;
            }
            if (!found)
                return new SearchResultModel(null, new Failure(1), null);
            if (ct.IsCancellationRequested)
                return null;
            while (levels[levels.Count - 1].Count == 0)
                levels.RemoveAt(levels.Count - 1);
            //assign coordinates to nodes 
            int totalCount = 0;
            Dictionary<Node, (double, int)> NodeCoord = new Dictionary<Node, (double, int)>();
            for (int y = 0; y < levels.Count; y++)
            {
                double interval = (double)xmax / (levels[y].Count + 1);
                for (int x = 0; x < levels[y].Count; x++)
                {
                    double xpos = (x + 1) * interval;
                    NodeCoord[levels[y][x]] = (xpos, y + 1);
                    totalCount++;
                }
            }
            if (ct.IsCancellationRequested)
                return null;
            List<Node> idNode = new List<Node>();
            Dictionary<Node, int> nodeId = new Dictionary<Node, int>();
            HashSet<GenePair> edges = new HashSet<GenePair>();
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
                        edges.Add(new GenePair(id1, id2));
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
                if (idNode[e.Gene1].reverse)
                {
                    data.edge.Add(new int[3]
                    {
                        e.Gene2,
                        e.Gene1,
                        GeneRelation[new GenePair(idNode[e.Gene2].geneID, idNode[e.Gene1].geneID)]
                    });
                }
                else
                {
                    data.edge.Add(new int[3]
                    {
                        e.Gene1,
                        e.Gene2,
                        GeneRelation[new GenePair(idNode[e.Gene1].geneID, idNode[e.Gene2].geneID)]
                    });
                }
            }

            var reference = new List<GenePair>();
            for (int y = 0; y < levels.Count; y++)
                for (int x = 0; x < levels[y].Count; x++)
                    if (levels[y][x].next != null)
                    {
                        if (levels[y][x].reverse)
                            reference.Add(new GenePair(levels[y][x].next.geneID, levels[y][x].geneID));
                        else
                            reference.Add(new GenePair(levels[y][x].geneID, levels[y][x].next.geneID));
                    }
            var sortedRef = reference.Distinct().OrderBy(x => x.Gene1).ThenBy(x => x.Gene2).ToArray();
            return new SearchResultModel(data, null, sortedRef);
        }

        private SearchResultModel _searchMulti(MultiQueryModel model, CancellationToken ct)
        {
            //Call C++ library to build graph
            int[] genelist = model.queries;
            int n = genelist.Length;
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
                using (var reg = ct.Register(() => terminnateProc(model.instanceID)))
                    enrichGenes(
                        model.instanceID,
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
                return new SearchResultModel(null, new Failure(1), null);

            if (ct.IsCancellationRequested)
                return null;

            //Build graph structure
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
                    GeneRelation[new GenePair(v1, v2)]
                });
            }

            var reference = new List<GenePair>();
            for (int i = 0; i < ecount; i++)
                reference.Add(new GenePair(edge[i * 2], edge[i * 2 + 1]));
            var sortedRef = reference.Distinct().OrderBy(x => x.Gene1).ThenBy(x => x.Gene2).ToArray();
            return new SearchResultModel(data, null, sortedRef);
        }

        private int DbQuery(ref bool isProcessed, int query, uint depth,
            List<List<Node>> levels, uint initial, uint width,
            Dictionary<int, List<int>> db, bool downstream, CancellationToken ct)
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
                        List<GenePair> selection = new List<GenePair>();
                        int count = 0;
                        foreach (var reGene in db[gene])
                        {
                            if (count == width)
                                break;
                            if (!used.Contains(reGene))
                            {
                                used.Add(reGene);
                                var newNode = new Node(node, reGene, downstream);
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
                    isProcessed = false;
                    return -1;
                }
            }
            return xmax;
        }

        private async Task _loacDBAsync()
        {
            try
            {
                var tskSymbol = loadSymbols();
                var tskConf = loadConfidence();
                await Task.WhenAll(tskSymbol, tskConf).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogCritical(ErrorHandler.getInfoStringTrace(ex));
            }
        }

        private async Task loadSymbols()
        {
            const string query = @"select ID, Symbol from GeneID;";
            List<List<object>> data = new List<List<object>>();
            await DBHandler.getDataListAsync(null, data, null, query, DBHandler.Servers.azureProtein).ConfigureAwait(false);
            Dictionary<int, string> newIDGene = new Dictionary<int, string>();
            Dictionary<string, int> newGeneID = new Dictionary<string, int>();
            if (data.Count != 0)
                foreach (var row in data)
                {
                    int id = (int)row[0];
                    string symbol = row[1] as string;
                    newIDGene[id] = symbol;
                    newGeneID[symbol] = id;
                }
            Interlocked.Exchange(ref idgene, newIDGene);
            Interlocked.Exchange(ref geneid, newGeneID);
        }

        private async Task loadConfidence()
        {
            const string query = @"select Gene1, Gene2, Confidence from Reactomes;";

            var newGeneRelation = new Dictionary<GenePair, int>();
            var newCacheUp = new Dictionary<int, List<int>>();
            var newCacheDown = new Dictionary<int, List<int>>();

            List<List<object>> data = new List<List<object>>();
            await DBHandler.getDataListAsync(null, data, null, query, DBHandler.Servers.azureProtein).ConfigureAwait(false);

            if (data.Count != 0)
                foreach (var item in data)
                {
                    int id1 = (int)item[0];
                    int id2 = (int)item[1];
                    int conf = (int)item[3];
                    newGeneRelation[new GenePair(id1, id2)] = conf;
                    newCacheDown[id1].Add(id2);
                    newCacheUp[id2].Add(id1);
                }
            Interlocked.Exchange(ref GeneRelation, newGeneRelation);
            Interlocked.Exchange(ref CacheDown, newCacheDown);
            Interlocked.Exchange(ref CacheUp, newCacheUp);
        }

        private async Task writeLog<T>(T logModel, int instanceID)
        {
            if (logModel == null)
                return;
            try
            {
                const string query = @"
                INSERT INTO QueryLogs (query, instanceID) values (@query, @instanceID);";

                var logText = JsonConvert.SerializeObject(logModel);

                Dictionary<string, object> param = new Dictionary<string, object>()
                {
                    { "@query", logText },
                    { "@instanceID", instanceID }
                };
                await DBHandler.ExecuteNonQueryAsync(query, DBHandler.Servers.azureProtein, param).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ErrorHandler.getInfoStringTrace(ex));
            }
        }

        private static byte[] getHash(string input)
        {
            return md5.ComputeHash(Encoding.ASCII.GetBytes(input));
        }

        private static string getHashString(string input)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in md5.ComputeHash(Encoding.ASCII.GetBytes(input)))
                sb.Append(b.ToString("X2"));
            return sb.ToString();
        }

        private static string getHashString(byte[] input)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in md5.ComputeHash(input))
                sb.Append(b.ToString("X2"));
            return sb.ToString();
        }

        private static string getHashString(MemoryStream input)
        {
            StringBuilder sb = new StringBuilder();
            input.Position = 0;
            foreach (byte b in md5.ComputeHash(input))
                sb.Append(b.ToString("X2"));
            return sb.ToString();
        }
    }
}
