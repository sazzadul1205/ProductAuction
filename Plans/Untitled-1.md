## 1. Missing endpoints

Based on the repository comparison, these are the main endpoints I didn't find implemented:

| Area            | Missing endpoint or feature                                                                   |
| --------------- | --------------------------------------------------------------------------------------------- |
| Auth & users    | `GET /auth/me` with current-user details                                                      |
| Admin users     | List users, get one user's details, lock/unlock a user                                        |
| Profile         | Seller verification status endpoint                                                           |
| Categories      | Update an existing category                                                                   |
| Products        | Seller gets details of their own product, including non-public status and deposit information |
| Auctions        | Featured auctions; admin auction list                                                         |
| Seller auctions | List bids for a seller's auction; seller and admin auction-result lists                       |
| Wallet          | User's own deposit-request history; details of one deposit request                            |
| Sales           | Buyer lists won auctions; delivery details; report non-delivery                               |
| Disputes        | Start dispute review; separate reject-dispute endpoint                                        |
| Penalties       | List penalties; waive a penalty                                                               |
| Dashboards      | Admin, seller, and buyer dashboards                                                           |
| Buyer activity  | Buyer-specific list of bids and bid history                                                   |

Some of these might be unnecessary for your first working version. The list reflects the Notion documentation, not a requirement to build every feature immediately.

## 2. Existing features that need improvement

These are not missing endpoints; the functionality already exists but differs from the documentation.

| Feature                    | What needs attention                                                                                                                    |
| -------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| Registration               | Return the documented `userId`, email, and role response.                                                                               |
| Login                      | Return the documented token, expiry, user ID, full name, and roles.                                                                     |
| Product submission         | Your Draft → PendingApproval workflow differs from the documentation. Either document your current workflow or change it deliberately.  |
| Auction details            | Include the documented seller information, product images, category, and winner/result details.                                         |
| Auction listing            | Add filtering, sorting, and pagination if required by the documented contract.                                                          |
| Deposit approval           | Prevent an already-reviewed deposit request from being approved and credited again.                                                     |
| Auction background service | Handle auctions whose entire scheduled window passes while the application is offline.                                                  |
| Payment workflow           | Decide whether wallet payment immediately sets `Paid` or requires admin confirmation. Your code and Notion document currently disagree. |
| API responses              | Align response DTOs, error formats, and HTTP status codes with the documentation.                                                       |

## 3. What I recommend implementing first

For your current testing stage, use this order:

1. Fix duplicate deposit approval.
2. Test the existing bidding, buyout, wallet payment, delivery, and receipt-confirmation workflows.
3. Add seller product details and deposit-request history.
4. Add buyer won-auctions and delivery-details endpoints.
5. Add the missing admin and seller endpoints that your planned UI actually needs.
6. Update Notion so its route names and payment workflow match the final implementation.

Important: You don't need to implement every dashboard, penalty endpoint, or administrative listing before testing the core auction flow. Get one complete flow working first:

`Register → Create product → Submit for approval → Approve product → Configure auction → Bid → Win → Pay → Dispatch → Confirm receipt`

That will give you a much better foundation for completing the remaining endpoints.
