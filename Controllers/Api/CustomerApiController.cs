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
        const string sql = "SELECT CUSTOMER_ID, NAME, EMAIL, PHONE, ADDRESS, CITY, STATE, POSTAL_CODE, COUNTRY, RECORD_TYP, CUSTOMER_STATUS, UPDATED_BY, UPDATED_DATE, CREATED_BY, CREATED_DATE, CREATED_LOC FROM MM_CUSTOMER WHERE STATUS = 1;";
        var customer = await connection.QueryAsync<Customer>(sql);
        return Ok(customer);
    }

    // GET: api/customer/{id}
    [HttpGet("{id}")]
    public async Task<ActionResult<Customer>> GetCustomer(int id)
    {
        if (id == 0) return BadRequest();

        await using var connection = await _context.CreateOpenConnectionAsync();
        const string sql = "SELECT CUSTOMER_ID, NAME, EMAIL, PHONE, ADDRESS, CITY, STATE, POSTAL_CODE, COUNTRY, RECORD_TYP, CUSTOMER_STATUS, UPDATED_BY, UPDATED_DATE, CREATED_BY, CREATED_DATE, CREATED_LOC FROM MM_CUSTOMER WHERE CUSTOMER_ID = @Id AND STATUS = 1;";
        var customer = await connection.QuerySingleOrDefaultAsync<Customer>(sql, new { Id = id });

        if (customer == null) return NotFound();

        return Ok(customer);
    }

    // POST: api/customer
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<Customer>> Create(Customer customer)
    {
        customer.Created_By ??= "API";
        if (customer.Created_Date == default)
        {
            customer.Created_Date = DateTime.UtcNow;
        }

        await using var connection = await _context.CreateOpenConnectionAsync();
        const string sql = "INSERT INTO MM_CUSTOMER (NAME, EMAIL, PHONE, ADDRESS, CITY, STATE, POSTAL_CODE, COUNTRY, RECORD_TYP, CUSTOMER_STATUS, CREATED_BY, CREATED_DATE, CREATED_LOC, UPDATED_BY, UPDATED_DATE, UPDATED_LOC) VALUES (@Name, @Email, @Phone, @Address, @City, @State, @Postal_Code, @Country, @Record_Typ, @Customer_Status, @Created_By, @Created_Date, @Created_Loc, @Updated_By, @Updated_Date, @Updated_Loc) RETURNING CUSTOMER_ID;";
        var id = await connection.ExecuteScalarAsync<int>(sql, customer);

        if (id == 0) return BadRequest();
        customer.Customer_Id = id;

        return CreatedAtAction(nameof(GetCustomer), new { id = customer.Customer_Id }, customer);
    }

    // PUT: api/customer/{id}
    [HttpPut("{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(int id, Customer customer)
    {
        if (id != customer.Customer_Id) return BadRequest();

        customer.Updated_By ??= "SYSTEM";
        customer.Updated_Date = DateTime.UtcNow;

        await using var connection = await _context.CreateOpenConnectionAsync();
        const string sql = "UPDATE MM_CUSTOMER SET NAME = @Name, EMAIL = @Email, PHONE = @Phone, ADDRESS = @Address, CITY = @City, STATE = @State, POSTAL_CODE = @Postal_Code, COUNTRY = @Country, RECORD_TYP = @Record_Typ, CUSTOMER_STATUS = @Customer_Status, UPDATED_BY = @Updated_By, UPDATED_DATE = @Updated_Date, UPDATED_LOC = @Updated_Loc WHERE CUSTOMER_ID = @Customer_Id; AND STATUS = 1";
        var result = await connection.ExecuteAsync(sql, customer);

        if (result == 0) return BadRequest();

        return NoContent();
    }

    // DELETE: api/customer/{id}
    [HttpDelete("{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (id == 0) return BadRequest();

        await using var connection = await _context.CreateOpenConnectionAsync();
        const string sql = @"
            UPDATE MM_CUSTOMER
            SET STATUS = 0,
                UPDATED_BY = 'SYSTEM',
                UPDATED_DATE = CURRENT_TIMESTAMP,
                UPDATED_LOC = '127.0.0.1'
            WHERE CUSTOMER_ID = @Id AND STATUS = 1;";
        var result = await connection.ExecuteAsync(sql, new { Id = id });

        if (result == 0) return NotFound();

        return NoContent();
    }
}
