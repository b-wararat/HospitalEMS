﻿using Microsoft.AspNetCore.Mvc;
using ServerLibrary.Repositoies.Contracts;

namespace Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GenericController<T> : Controller where T : class
    {
        private readonly IGenericRepository<T> genericRepository;

        public GenericController(IGenericRepository<T> genericRepository)
        {
            this.genericRepository = genericRepository;
        }

        [HttpGet("all")]
        public async Task<IActionResult> GetAll() => Ok(await genericRepository.GetAll());

        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> Delete([FromRoute] int id) {
            if (id <= 0) return BadRequest("Invalid request sent");
            return Ok(await genericRepository.DeleteById(id));
        }

        [HttpGet("get/{id}")]
        public async Task<IActionResult> GetById([FromRoute] int id)
        {
            if (id <= 0) return BadRequest("Invalid request sent");
            return Ok(await genericRepository.GetById(id));
        }

        [HttpPost("add")]
        public async Task<IActionResult> Add(T model) {
            if (model is null) return BadRequest("Bad requst made");
            return Ok(await genericRepository.Insert(model));
        }

        [HttpPut("update")]
        public async Task<IActionResult> Update(T model)
        {
            if (model is null) return BadRequest("Bad requst made");
            return Ok(await genericRepository.Update(model));
        }
    }
}
