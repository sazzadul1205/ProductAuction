
using Microsoft.EntityFrameworkCore;

namespace ProductAuction.API.Models
{
    public class Wallet
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        [Precision(18, 2)]
        public decimal Balance { get; set; } = 0;
        [Precision(18, 2)]
        public decimal ReservedBalance { get; set; } = 0;
    }
}
