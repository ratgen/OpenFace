using Microsoft.AspNetCore.Mvc;

namespace AuApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ActionUnitController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost(Name = "PostVideo")]
        public int PostVideo()
        {
            return 1;
        }

    }
}
