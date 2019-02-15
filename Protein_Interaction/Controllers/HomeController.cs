using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Protein_Interaction.Data;
using Protein_Interaction.Models;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Protein_Interaction.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        private static int queryID = 0;
        private static int querySyn = 0;
        private static int queryRef = 0;
        private static Thread thread = null;

        public IActionResult About()
        {
            if (thread == null)
            {
                thread = new Thread(GetDB);
                thread.Start();
            }
            return View();
        }

        public IActionResult Contact()
        {
            return View();
        }

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        private readonly DataContext _context;

        public HomeController(DataContext context)
        {
            _context = context;
        }

        public JsonResult GetCount()
        {
            if (thread != null)
            {
                thread.Join();
                thread = null;
            }
            StringBuilder jsonStr = new StringBuilder();
            StringWriter sw = new StringWriter(jsonStr);
            using (JsonWriter writer = new JsonTextWriter(sw))
            {
                writer.Formatting = Formatting.Indented;
                writer.WriteStartObject();
                writer.WritePropertyName("queryID");
                writer.WriteValue(queryID);
                writer.WritePropertyName("querySyn");
                writer.WriteValue(querySyn);
                writer.WritePropertyName("queryRef");
                writer.WriteValue(queryRef);
                writer.WriteEndObject();
            }
            return new JsonResult(jsonStr.ToString());
        }

        private void GetDB()
        {
            if (queryID == 0 || queryRef == 0 || querySyn == 0)
            {
                var TqueryID = (from row in _context.GeneID
                                select row).CountAsync();
                var TquerySyn = (from row in _context.Synon
                                 select row).CountAsync();
                var TqueryRef = (from row in _context.Ref
                                 select row).CountAsync();
                Task<int>[] tasks = new Task<int>[] { TqueryID, TquerySyn, TqueryRef };
                Task.WaitAll(tasks);
                queryID = tasks[0].Result;
                querySyn = tasks[1].Result;
                queryRef = tasks[2].Result;
            }
        }
    }
}
