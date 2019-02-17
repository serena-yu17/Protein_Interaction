using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Protein_Interaction.Models;
using Protein_Interaction.Operations;
using System.Diagnostics;

namespace Protein_Interaction.Controllers
{
    public class HomeController : Controller
    {
        HomeOperations homeOperations;

        public HomeController(ILoggerFactory factory)
        {
            homeOperations = new HomeOperations(factory);
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult About()
        {            
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

        public JsonResult Count()
        {
            var res = homeOperations.getCount();
            return Json(res);
        }
    }
}
