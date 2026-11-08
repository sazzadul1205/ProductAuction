# Re-Auction Test Plan

1. Ensure SQL Server DB accessible via SSMS.
2. Run API: dotnet run.
3. Run frontend: npm run dev.
4. Test scenarios per Kilo prompt Batch 5 table.
## Visual Walkthrough
1. Seller MyProducts -> click "Reset for Re-Auction" on approved product.
2. Product moves to Draft; edit and submit.
3. Admin approves -> new auction created.
4. View previous auctions via product auction history.