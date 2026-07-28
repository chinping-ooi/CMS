using System.ComponentModel.DataAnnotations;

namespace CMS.Models
{
    public class Customer
    {
        public int Customer_Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Postal_Code { get; set; }
        public string? Country { get; set; }
        public int Record_Typ { get; set; } = 1;
        public string Customer_Status { get; set; }
        public string Created_By { get; set; }
        public DateTime Created_Date { get; set; }
        public string? Created_Loc { get; set; }
        public string? Updated_By { get; set; }
        public DateTime? Updated_Date { get; set; }
        public string? Updated_Loc { get; set; }
    }
}
