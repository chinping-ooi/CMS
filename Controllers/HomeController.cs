using System.Diagnostics;
using CMS.Data;
using CMS.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMS.Controllers;

public class HomeController : Controller
{
    private readonly DapperContext _context;

    public HomeController(DapperContext context)
    {
        _context = context;
    }

    [Authorize]
    [HttpGet("/")]
    public IActionResult Index()
    {
        return View();
    }

    [AllowAnonymous]
    [HttpGet("/login")]
    public IActionResult Login()
    {
        ViewData["Title"] = "Login";
        return View();
    }

    [Authorize]
    [HttpGet("/customer", Name = "customer")]
    public async Task<IActionResult> Customer()
    {
        return View();
    }

    [AllowAnonymous]
    [HttpGet("/not-found")]
    public IActionResult NotFoundPage()
    {
        return View();
    }

    [AllowAnonymous]    
    [HttpGet("/error")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
