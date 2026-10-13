using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [Route("api/v1/[controller]")]
    [Authorize]
    public class HomeController : ControllerBase
    {

        private readonly IMapper _mapper;
        public HomeController(
           IMapper mapper)
        {
            _mapper = mapper;
        }


    }
}