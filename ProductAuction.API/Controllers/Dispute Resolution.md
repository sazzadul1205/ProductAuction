# Dispute Resolution – Options Explained

**Endpoint:** `PUT /api/dispute/{id}/resolve`  
**Access:** Admin only  
**Controller:** `DisputeController.cs`

---

## 1. Common Preconditions

The following validations run before any resolution option is executed. If any validation fails, the request stops and returns the corresponding error.

1. **Admin Identity:** The caller must have a valid Admin ID in the authentication token. Otherwise, return `401 Unauthorized`.
2. **Valid FaultType:** Must be exactly `Buyer`, `Seller`, or `System`. Otherwise, return `400 Bad Request`.
3. **AdminDecision Required:** The written decision text cannot be empty. Otherwise, return `400 Bad Request`.
4. **System Fault and Penalty Restriction:** If `FaultType = System` and `ApplyPenalty = true`, reject the request with `400 Bad Request`. System faults cannot carry penalties.
5. **Dispute Exists:** The requested dispute ID must exist. Otherwise, return `404 Not Found`.
6. **Dispute Status:** The dispute must have `Status = UnderReview`. If its status is `Open` or `Resolved`, return `400 Bad Request`.
7. **Sale Exists:** The linked sale must exist. Otherwise, return `404 Not Found`.
8. **Product Exists:** The linked product must exist. Otherwise, return `404 Not Found`.
9. **Duplicate Penalty Prevention:** If a `Penalty` record already exists for this dispute, reject the request with `400 Bad Request`.
10. **Escrow Consistency:** If payment has been submitted (`PaymentSubmittedAt` has a value):
    - An active `SaleEscrow` reservation must exist.
    - The escrow amount must equal `sale.FinalAmount`.
    - If either condition fails, return `400 Bad Request`.
11. **Buyer Penalty and Active Escrow Restriction:** If `FaultType = Buyer`, `ApplyPenalty = true`, and escrow still exists, reject the request with `400 Bad Request`. This combination requires manual admin investigation.

### Admin Decision Assignment

After the common validations pass, the decision information is assigned:

- `dispute.AdminDecision = dto.AdminDecision`
- `dispute.DecidedBy = adminId`

### Penalty Calculation

The following calculation is used by Options 4 and 5:

- **Total Penalty:** `StartingPrice × 0.25` (rounded to 2 decimal places).
- **Opposite Party Share:** `Total Penalty × 0.80`.
- **Platform Share:** `Total Penalty − Opposite Party Share`.

---

## 2. Option 1 – System Fault (No Penalty)

**Conditions:**

- `FaultType = System`
- `ApplyPenalty = false`

**Process:**

1. If an escrow exists, credit the escrow amount back to the buyer's wallet (`Balance += escrow.Amount`).
2. Mark the escrow status as `Released`.
3. Record a `Credit` wallet transaction with the description:
   - `System-fault refund for dispute #{id}`
4. Do not create a penalty record.
5. Do not forfeit any deposits.

**Result:** The buyer receives the escrow refund. No party is penalized, and nobody loses their deposit.

---

## 3. Option 2 – Buyer Fault (No Penalty)

**Conditions:**

- `FaultType = Buyer`
- `ApplyPenalty = false`

**Process:**

1. If an escrow exists, credit the escrow amount back to the buyer's wallet.
2. Mark the escrow status as `Released`.
3. Record a `Credit` wallet transaction with the description:
   - `No-penalty refund for dispute #{id}`
4. Do not create a penalty record.
5. Do not forfeit the buyer's reserved deposit.

**Result:** The buyer receives the refund but is found at fault in the admin's written decision. No money is deducted as a penalty.

---

## 4. Option 3 – Seller Fault (No Penalty)

**Conditions:**

- `FaultType = Seller`
- `ApplyPenalty = false`

**Process:**

1. If an escrow exists, credit the escrow amount back to the buyer's wallet.
2. Mark the escrow status as `Released`.
3. Record a `Credit` wallet transaction with the description:
   - `Seller-fault refund for dispute #{id}`
4. Do not create a penalty record.
5. Do not forfeit the seller's reserved deposit.

**Result:** The buyer receives the refund. The seller is found at fault but keeps their reserved deposit.

---

## 5. Option 4 – Buyer Fault (Penalty Applied)

**Conditions:**

- `FaultType = Buyer`
- `ApplyPenalty = true`

**Important:** This option is only reachable when no escrow exists. The common precondition blocks buyer penalties when an escrow exists.

**Process:**

### Step 1: Load Wallets

Load the following wallets:

- Buyer's wallet.
- Seller's wallet.
- Platform wallet.

If any wallet is missing, return `400 Bad Request`.

### Step 2: Validate Buyer's Reservation

Find the buyer's `BuyerFunds` reservation for the auction.

The following conditions must be satisfied:

- The reservation must exist.
- Its amount must equal `totalPenalty`.
- The buyer's wallet must have sufficient `ReservedBalance`.
- The buyer's wallet must have sufficient `Balance`.

If any condition fails, return `400 Bad Request`.

### Step 3: Charge the Buyer

Apply the following updates:

- `buyerWallet.Balance -= totalPenalty`
- `buyerWallet.ReservedBalance -= buyerDeposit.Amount`
- `buyerDeposit.Status = Forfeited`

### Step 4: Distribute the Penalty

- Seller receives 80%:
  - `sellerWallet.Balance += oppositePartyAmount`
- Platform receives 20%:
  - `platformWallet.Balance += platformAmount`

### Step 5: Record Wallet Transactions

Create three wallet transactions:

1. Buyer:
   - Type: `Penalty`
   - Description: `Buyer penalty for dispute #{id}`
2. Seller:
   - Type: `Credit`
   - Description: `Dispute compensation #{id}`
3. Platform:
   - Type: `Penalty`
   - Description: `Platform penalty share for dispute #{id}`

### Step 6: Create Penalty Record

Create a `Penalty` record with:

- `AgainstUserId = buyerId`
- `Status = Applied`

**Result:** The buyer loses 25% of the product's starting price. The seller receives 80% of the penalty, and the platform receives the remaining 20%.

---

## 6. Option 5 – Seller Fault (Penalty Applied)

**Conditions:**

- `FaultType = Seller`
- `ApplyPenalty = true`

**Process:**

### Step 1: Refund Escrow (If Available)

If an escrow exists:

1. Credit `escrow.Amount` back to the buyer's wallet.
2. Mark the escrow status as `Released`.
3. Record a `Credit` wallet transaction with the description:
   - `Seller-fault refund for dispute #{id}`

### Step 2: Load Seller and Platform Wallets

Load:

- Seller's wallet.
- Platform wallet.

If either wallet is missing, return `400 Bad Request`.

### Step 3: Validate Seller's Reservation

Find the seller's `SellerDeposit` reservation for the product.

The following conditions must be satisfied:

- The reservation must exist.
- Its amount must equal `totalPenalty`.
- The seller's wallet must have sufficient `ReservedBalance`.
- The seller's wallet must have sufficient `Balance`.

If any condition fails, return `400 Bad Request`.

### Step 4: Load Buyer's Wallet

Load the buyer's wallet for the compensation credit.

If the wallet is missing, return `400 Bad Request`.

### Step 5: Charge the Seller

Apply the following updates:

- `sellerWallet.Balance -= totalPenalty`
- `sellerWallet.ReservedBalance -= sellerDeposit.Amount`
- `sellerDeposit.Status = Forfeited`

### Step 6: Distribute the Penalty

- Buyer receives 80%:
  - `buyerWallet.Balance += oppositePartyAmount`
- Platform receives 20%:
  - `platformWallet.Balance += platformAmount`

### Step 7: Record Wallet Transactions

Create three wallet transactions:

1. Seller:
   - Type: `Penalty`
   - Description: `Seller penalty for dispute #{id}`
2. Buyer:
   - Type: `Credit`
   - Description: `Dispute compensation #{id}`
3. Platform:
   - Type: `Penalty`
   - Description: `Platform penalty share for dispute #{id}`

### Step 8: Create Penalty Record

Create a `Penalty` record with:

- `AgainstUserId = sellerId`
- `Status = Applied`

**Result:** The buyer receives the escrow refund (if applicable) and 80% of the penalty. The seller loses 25% of the starting price, and the platform receives 20%.

---

## 7. Common Cleanup (After Every Option)

The following operations run after the selected resolution option.

### Step 1: Release Seller Deposit if Not Forfeited

Find the seller's `SellerDeposit` reservation for the product that is still marked `Reserved`.

If found:

- Subtract the reservation amount from `ReservedBalance`.
- Change its status to `Released`.

If it was already forfeited in Option 5, no further action is taken.

### Step 2: Release Buyer's Deposit if Not Forfeited

Find the buyer's `BuyerFunds` reservation for the auction that is still marked `Reserved`.

If found:

- Subtract the reservation amount from `ReservedBalance`.
- Change its status to `Released`.

If it was already forfeited in Option 4, no further action is taken.

### Step 3: Release All Other Buyers' Auction Deposits

Find all remaining `BuyerFunds` reservations for the auction that:

- Have `Status = Reserved`.
- Do not belong to the current buyer's wallet.

For each reservation:

1. Load the corresponding wallet.
2. Subtract the reservation amount from `ReservedBalance`.
3. Change the reservation status to `Released`.
4. Record a `Release` wallet transaction with the description:
   - `Auction deposit released after dispute #{id}`

If any required wallet is missing, return `400 Bad Request`.

### Step 4: Update Final Statuses

Update the following records:

- `dispute.Status = Resolved`
- `product.Status = Unavailable`
- `sale.Status = Refunded`

### Step 5: Commit Database Transaction

Execute:

1. `SaveChangesAsync()`
2. `CommitAsync()`

All database changes are committed together. If an operation fails before the commit, the transaction is rolled back.

### Step 6: Return Response

Return `200 OK` with a message based on whether a penalty was applied.

**When a penalty is applied:**

`Dispute resolved and penalty applied successfully.`

**When no penalty is applied:**

`Dispute resolved without a penalty.`

The response contains:

- `FaultType`
- `AdminDecision`
- `DecidedBy`
- `ProductStatus`
- `SaleStatus`
- `PenaltyApplied`
- Penalty breakdown (or `null` if no penalty was applied)

---

## 8. Quick Summary Table

| Option | FaultType | ApplyPenalty | Escrow Refunded?               | Buyer Charged? | Seller Charged? | 80/20 Split? |
| ------ | --------- | ------------ | ------------------------------ | -------------- | --------------- | ------------ |
| 1      | System    | false        | Yes                            | No             | No              | No           |
| 2      | Buyer     | false        | Yes                            | No             | No              | No           |
| 3      | Seller    | false        | Yes                            | No             | No              | No           |
| 4      | Buyer     | true         | N/A (blocked if escrow exists) | Yes (25%)      | No              | Yes          |
| 5      | Seller    | true         | Yes                            | No             | Yes (25%)       | Yes          |

---

## 9. Final Outcome (All Options)

Regardless of the selected option:

- Dispute status becomes `Resolved`.
- Product status becomes `Unavailable`.
- Sale status becomes `Refunded`.
- All remaining buyer deposits for the auction are released.
- Seller deposit is released unless forfeited in Option 5.
- Buyer deposit is released unless forfeited in Option 4.
