using Livingstone.Library;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Protein_Interaction.Models;
using Protein_Interaction.Operations;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Protein_Interaction.Controllers
{
    public class SearchController : Controller
    {
        private readonly GeneGraphOperations graphOperations;
        private readonly ILogger<SearchController> logger;

        public SearchController(IMemoryCache cache, ILoggerFactory factory)
        {
            graphOperations = new GeneGraphOperations(cache, factory);
            logger = factory.CreateLogger<SearchController>();
            graphOperations.loadDB();
        }

        [HttpGet]
        public IActionResult Multi()
        {
            ViewBag.taskID = graphOperations.getTaskID();
            return View();
        }

        [HttpGet]
        public IActionResult Single()
        {
            ViewBag.taskID = graphOperations.getTaskID();
            return View();
        }

        [HttpGet]
        public IActionResult SearchSingle(SingleQueryModel model)
        {
            try
            {
                var res = graphOperations.searchSingle(model);
                if (res.failure != null)
                {
                    Response.StatusCode = 500;
                    return Json(res.failure);
                }
                return Json(res.graph);
            }
            catch (Exception ex)
            {
                logger.LogError(ErrorHandler.getInfoStringTrace(ex));
                return new StatusCodeResult(500);
            }
        }

        [HttpGet]
        public IActionResult SearchMulti(string query, int instanceID)
        {            
            try
            {
                var model = graphOperations.collectQueryGenes(query, instanceID);
                var res = graphOperations.searchMulti(model);
                if (res.failure != null)
                {
                    Response.StatusCode = 500;
                    return Json(res.failure);
                }
                return Json(res.graph);
            }
            catch (Exception ex)
            {
                logger.LogError(ErrorHandler.getInfoStringTrace(ex));
                return new StatusCodeResult(500);
            }
        }

        [HttpGet]
        public IActionResult CancelTask(int id)
        {
            try
            {
                graphOperations.clearCancellationTokenSource(id);
                return new StatusCodeResult(204);
            }
            catch (Exception ex)
            {
                logger.LogError(ErrorHandler.getInfoStringTrace(ex));
                return new StatusCodeResult(500);
            }
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

        [HttpGet]
        public async Task<IActionResult> GetReference(string refKey)
        {
            try
            {
                var res = await graphOperations.getRef(refKey).ConfigureAwait(false);
                return Json(res);
            }
            catch (Exception ex)
            {
                logger.LogError(ErrorHandler.getInfoStringTrace(ex));
                return new StatusCodeResult(500);
            }
        }
    }
}