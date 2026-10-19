namespace ProductAuction.API.DTOs.Admin
{
    public class AdminUserLockDto
    {
        public bool IsLocked { get; set; }
        public int? DurationMinutes { get; set; }
        public bool IsPermanent { get; set; }
    }
}
