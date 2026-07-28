using CMS.Data;
using CMS.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace CMS.Controllers;

[ApiController]
[Route("api/customer")]
public class CustomerApiController : ControllerBase
{
    private readonly DapperContext _context;

    public CustomerApiController(DapperContext context)
    {
        _context = context;
    }

    // GET: api/customer
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Customer>>> GetAll()
    {
        await using var connection = await _context.CreateOpenConnectionAsync();
        var sql = "SELECT customer_id, name, email, phone, address, city, state, postal_code, country, record_typ, customer_status, updated_by, updated_date FROM customer";
        var customer = await connection.QueryAsync<Customer>(sql);
        return Ok(customer);
    }

    // GET: api/customer/rows
    // [HttpGet("/rows")]
    // public async Task<IActionResult> GetCustomerRows()
    // {
    //     await using var connection = await _context.CreateOpenConnectionAsync();

    //     var sql = "SELECT customer_id, name, email, phone, address, city, state, postal_code, country, record_typ, customer_status, updated_by, updated_date FROM customer";
    //     var customers = await connection.QueryAsync<Customer>(sql);

    //     return PartialView("_CustomerRows", customers);
    // }

    // GET: api/customer/{id}
    [HttpGet("{id}")]
    public async Task<ActionResult<Customer>> GetCustomer(int id)
    {
        if (id == 0) return BadRequest();

        await using var connection = await _context.CreateOpenConnectionAsync();
        var sql = "SELECT customer_id, name, email, phone, address, city, state, postal_code, country, record_typ, customer_status, updated_by, updated_date FROM customer WHERE customer_id = @Id";
        var customer = await connection.QuerySingleOrDefaultAsync<Customer>(sql, new { Id = id });

        if (customer == null) return NotFound();

        return Ok(customer);
    }

    // POST: api/customer
    [HttpPost]
    public async Task<ActionResult<Customer>> Create(Customer customer)
    {
        await using var connection = await _context.CreateOpenConnectionAsync();
        var sql = "INSERT INTO customer (name, email, phone, address, city, state, postal_code, country, record_typ, customer_status, updated_by, updated_date) VALUES (@Name, @Email, @Phone, @Address, @City, @State, @Postal_Code, @Country, @Record_Typ, @Customer_Status, @Updated_By, @Updated_Date) RETURNING customer_id";
        var id = await connection.ExecuteScalarAsync<int>(sql, customer);

        if (id == 0) return BadRequest();
        customer.Customer_Id = id;

        return CreatedAtAction(nameof(GetCustomer), new { id = customer.Customer_Id }, customer);
    }

    // PUT: api/customer/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, Customer customer)
    {
        if (id != customer.Customer_Id) return BadRequest();

        await using var connection = await _context.CreateOpenConnectionAsync();
        var sql = "UPDATE customer SET name = @Name, email = @Email, phone = @Phone, address = @Address, city = @City, state = @State, postal_code = @Postal_Code, country = @Country, record_typ = @Record_Typ, customer_status = @Customer_Status, updated_by = @Updated_By, updated_date = @Updated_Date WHERE customer_id = @Customer_Id";
        var result = await connection.ExecuteAsync(sql, customer);

        if (result == 0) return BadRequest();

        return NoContent();
    }

    // DELETE: api/customer/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (id == 0) return BadRequest();

        await using var connection = await _context.CreateOpenConnectionAsync();
        var sql = "DELETE FROM customer WHERE customer_id = @Id";
        var result = await connection.ExecuteAsync(sql, new { Id = id });

        if (result == 0) return BadRequest();

        return NoContent();
    }
}
