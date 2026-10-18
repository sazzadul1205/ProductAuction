using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.DTOs.Wallet
{
    public class CreateDepositRequestDto
    {
        [Required]
        public string Method { get; set; } = string.Empty;

        [Range(1, 1000000)]
        public decimal Amount { get; set; }

        [Required]
        public string TransactionReference { get; set; } = string.Empty;

        [Required]
        public IFormFile ReceiptImage { get; set; } = null!;
    }
}