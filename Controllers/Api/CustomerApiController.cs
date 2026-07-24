using CMS.Data;
using CMS.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace CMS.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomerApiController : ControllerBase
{
    private readonly DapperContext _context;

    public CustomerApiController(DapperContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Customer>>> GetAll()
    {
        await using var connection = await _context.CreateOpenConnectionAsync();
        var customers = await connection.QueryAsync<Customer>("SELECT * FROM customer");
        return Ok(customers);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Customer>> Get(int id)
    {
        await using var connection = await _context.CreateOpenConnectionAsync();
        var customer = await connection.QuerySingleOrDefaultAsync<Customer>(
            "SELECT * FROM customer WHERE customer_id = @Id", new { Id = id });

        if (customer == null)
        {
            return NotFound();
        }

        return Ok(customer);
    }

    [HttpPost]
    public async Task<ActionResult<Customer>> Create(Customer customer)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        const string sql = @"
            INSERT INTO customer (name, email, phone, address, city, state, postal_code, country, record_typ, customer_status, updated_by, updated_date)
            VALUES (@Name, @Email, @Phone, @Address, @City, @State, @Postal_Code, @Country, @Record_Typ, @Customer_Status, @Updated_By, @Updated_Date)
            RETURNING customer_id";

        await using var connection = await _context.CreateOpenConnectionAsync();
        var id = await connection.ExecuteScalarAsync<int>(sql, customer);
        customer.Customer_Id = id;

        return CreatedAtAction(nameof(Get), new { id = customer.Customer_Id }, customer);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, Customer customer)
    {
        if (id != customer.Customer_Id)
        {
            return BadRequest();
        }

        const string sql = @"
            UPDATE customer SET
                name = @Name,
                email = @Email,
                phone = @Phone,
                address = @Address,
                city = @City,
                state = @State,
                postal_code = @Postal_Code,
                country = @Country,
                record_typ = @Record_Typ,
                customer_status = @Customer_Status,
                updated_by = @Updated_By,
                updated_date = @Updated_Date
            WHERE customer_id = @Customer_Id";

        await using var connection = await _context.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync(sql, customer);

        if (rows == 0)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        await using var connection = await _context.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync(
            "DELETE FROM customer WHERE customer_id = @Id", new { Id = id });

        if (rows == 0)
        {
            return NotFound();
        }

        return NoContent();
    }
}
