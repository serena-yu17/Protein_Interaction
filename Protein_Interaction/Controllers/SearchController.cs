using ErrorMessage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Protein_Interaction.Models;
using Protein_Interaction.Operations;
using System;
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
                var msg = ErrorHandler.getInfoStringTrace(ex);
                logger.LogError(msg);
                Response.StatusCode = 500;
                return Content(msg);
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
                var msg = ErrorHandler.getInfoStringTrace(ex);
                logger.LogError(msg);
                Response.StatusCode = 500;
                return Content(msg);
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
                var msg = ErrorHandler.getInfoStringTrace(ex);
                logger.LogError(msg);
                Response.StatusCode = 500;
                return Content(msg);
            }
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
                var msg = ErrorHandler.getInfoStringTrace(ex);
                logger.LogError(msg);
                Response.StatusCode = 500;
                return Content(msg);
            }
        }
    }
}