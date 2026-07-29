using CMS.Data;
using CMS.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;
// using Microsoft.AspNetCore.Authorization;

namespace CMS.Controllers;

public class CustomerController : Controller
{
    private readonly DapperContext _context;

    public CustomerController(DapperContext context)
    {
        _context = context;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Customer customer)
    {
        if (!ModelState.IsValid)
        {
            TempData["ToastMessage"] = "Please correct the form errors before saving.";
            TempData["ToastType"] = "danger";
            await using var connection = await _context.CreateOpenConnectionAsync();
            var customers = await connection.QueryAsync<Customer>("SELECT * FROM customer");
            return View("~/Views/Home/Customer.cshtml", customers);
        }

        try
        {
            const string sql = @"
                INSERT INTO customer (name, email, phone, address, city, state, postal_code, country, record_typ, customer_status, created_by, created_date, created_loc, updated_by, updated_date, updated_loc)
                VALUES (@Name, @Email, @Phone, @Address, @City, @State, @Postal_Code, @Country, @Record_Typ, @Customer_Status, @Created_By, @Created_Date, @Created_Loc, @Updated_By, @Updated_Date, @Updated_Loc)
                RETURNING customer_id";

            await using var connection = await _context.CreateOpenConnectionAsync();
            var id = await connection.ExecuteScalarAsync<int>(sql, customer);
            customer.Customer_Id = id;

            TempData["ToastMessage"] = "Customer added successfully.";
            TempData["ToastType"] = "success";
        }
        catch (Exception ex)
        {
            TempData["ToastMessage"] = $"Could not add customer: {ex.Message}";
            TempData["ToastType"] = "danger";
        }

        return RedirectToAction("Customer", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Customer customer)
    {
        if (!ModelState.IsValid)
        {
            TempData["ToastMessage"] = "Please correct the form errors before updating.";
            TempData["ToastType"] = "danger";
            return RedirectToAction("Customer", "Home");
        }

        try
        {
            await using var connection = await _context.CreateOpenConnectionAsync();
            
            var sql = "UPDATE customer SET name = @Name, email = @Email, phone = @Phone, address = @Address, city = @City, state = @State, postal_code = @Postal_Code, country = @Country, record_typ = @Record_Typ, customer_status = @Customer_Status, updated_by = @Updated_By, updated_date = @Updated_Date, updated_loc = @Updated_Loc WHERE customer_id = @Customer_Id";
            var result = await connection.ExecuteAsync(sql, customer);

            if (result == 0)
            {
                TempData["ToastMessage"] = "No matching customer was updated.";
                TempData["ToastType"] = "danger";
                return RedirectToAction("Customer", "Home");
            }

            TempData["ToastMessage"] = "Customer updated successfully.";
            TempData["ToastType"] = "success";
        }
        catch (Exception ex)
        {
            TempData["ToastMessage"] = $"Could not update customer: {ex.Message}";
            TempData["ToastType"] = "danger";
        }

        return RedirectToAction("Customer", "Home");
    }
}
