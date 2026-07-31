using System.ComponentModel.DataAnnotations;
using CMS.Enum;

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
        public Status Status { get; set; } = Status.Active;
        public Record_Type Record_Typ { get; set; }
        public string Created_By { get; set; }
        public DateTime Created_Date { get; set; }
        public string? Created_Loc { get; set; }
        public string? Updated_By { get; set; }
        public DateTime? Updated_Date { get; set; }
        public string? Updated_Loc { get; set; }
    }
}
