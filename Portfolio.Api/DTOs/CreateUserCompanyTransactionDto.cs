namespace Portfolio.Api.DTOs
{
    /// <summary>
    /// Request DTO for recording a buy/sell transaction.
    /// Uses Symbol instead of TickerId.
    /// </summary>
    public class CreateUserCompanyTransactionDto
    {
        public string Symbol { get; set; } = string.Empty;
        public int Shares { get; set; }
        public decimal? Price { get; set; }
        public string? Notes { get; set; }
    }
}
