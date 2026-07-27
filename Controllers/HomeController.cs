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

    [HttpGet("/customer")]
    public async Task<IActionResult> Customer()
    {
        try
        {
            await using var connection = await _context.CreateOpenConnectionAsync();
            var customers = await connection.QueryAsync<Customer>("SELECT * FROM customer");
            return View(customers);
        }
        catch (Exception ex)
        {
            ViewBag.DbError = ex.Message;
            return View(Enumerable.Empty<Customer>());
        }
    }

    [AllowAnonymous]
    [HttpGet("/not-found")]
    public IActionResult NotFoundPage()
    {
        ViewData["Title"] = "Page Not Found";
        return View();
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()    {
        return View(
            new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier }
        );
    }
}
