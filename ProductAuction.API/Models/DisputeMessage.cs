using System.ComponentModel.DataAnnotations;

namespace ProductAuction.API.Models
{
  public class DisputeMessage
  {
    public int Id { get; set; }

    public int DisputeId { get; set; }
    public Dispute Dispute { get; set; } = null!;
    public string SenderId { get; set; } = string.Empty;
    [MaxLength(50)]
    public string SenderRole { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Body { get; set; } = string.Empty;

    public string? AttachmentUrl { get; set; }

    public DateTime CreatedAt { get; set; }
  }
}