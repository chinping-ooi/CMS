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
            var customers = await connection.QueryAsync<Customer>("SELECT * FROM MM_CUSTOMER;");
            return View("~/Views/Home/Customer.cshtml", customers);
        }

        try
        {
            const string sql = @"
                INSERT INTO MM_CUSTOMER (NAME, EMAIL, PHONE, ADDRESS, CITY, STATE, POSTAL_CODE, COUNTRY, RECORD_TYP, CUSTOMER_STATUS, CREATED_BY, CREATED_DATE, CREATED_LOC, UPDATED_BY, UPDATED_DATE, UPDATED_LOC)
                VALUES (@Name, @Email, @Phone, @Address, @City, @State, @Postal_Code, @Country, @Record_Typ, @Customer_Status, @Created_By, @Created_Date, @Created_Loc, @Updated_By, @Updated_Date, @Updated_Loc)
                RETURNING CUSTOMER_ID;";

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
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(customer));
        if (!ModelState.IsValid)
        {
            foreach (var error in ModelState)
            {
                foreach (var message in error.Value.Errors)
                {
                    Console.WriteLine($"{error.Key}: {message.ErrorMessage}");
                }
            }

            TempData["ToastMessage"] = "Please correct the form errors before updating.";
            TempData["ToastType"] = "danger";

            return RedirectToAction("Customer", "Home");
        }

        try
        {
            const string sql = @"
                UPDATE MM_CUSTOMER SET
                    NAME = @Name,
                    EMAIL = @Email,
                    PHONE = @Phone,
                    ADDRESS = @Address,
                    CITY = @City,
                    STATE = @State,
                    POSTAL_CODE = @Postal_Code,
                    COUNTRY = @Country,
                    RECORD_TYP = @Record_Typ,
                    CUSTOMER_STATUS = @Customer_Status,
                    UPDATED_BY = @Updated_By,
                    UPDATED_DATE = @Updated_Date,
                    UPDATED_LOC = @Updated_Loc
                WHERE CUSTOMER_ID = @Customer_Id;";

            await using var connection = await _context.CreateOpenConnectionAsync();
            var rows = await connection.ExecuteAsync(sql, customer);

            if (rows == 0)
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
