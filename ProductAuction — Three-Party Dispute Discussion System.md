# ProductAuction — Three-Party Dispute Discussion System

## Backend Feasibility Study & Implementation Specification

## 1. Feature Objective

Implement a dispute-specific communication system in the ProductAuction backend.

When a dispute is raised, the buyer, seller, and administrator should be able to participate in the same discussion.

The purpose is to allow all three parties to:

- Understand the reported issue.
- Submit objections and explanations.
- Ask questions and provide clarification.
- Exchange messages.
- Review supporting information.
- Reach a mutual understanding where possible.

The administrator remains responsible for reviewing the dispute and making the final resolution decision.

**This is a three-party dispute discussion system, not a general-purpose messaging platform.**

---

## 2. Three-Party Communication Model

Each dispute should have one shared conversation involving:

1. Buyer
2. Seller
3. Administrator

All three parties should be able to participate in the same conversation.

### Example scenario

A buyer purchases a product but reports that the seller did not deliver it.

The workflow:

1. Buyer raises a dispute.
2. Seller receives a notification.
3. Buyer, seller, and administrator can access the dispute conversation.
4. Seller explains their position.
5. Buyer responds with clarification.
6. Administrator asks questions and reviews both sides.
7. Both parties can continue discussing the issue.
8. Administrator makes the final decision.

### Communication permissions

| Participant | View messages | Send messages | Final resolution |
| ----------- | ------------- | ------------- | ---------------- |
| Buyer       | Yes           | Yes           | No               |
| Seller      | Yes           | Yes           | No               |
| Admin       | Yes           | Yes           | Yes              |

Only the buyer and seller associated with the relevant dispute should participate. Other buyers and sellers must not have access.

Administrators must have appropriate authorization.

---

## 3. Proposed Discussion Workflow

### Step 1 — Dispute creation

The buyer or seller creates a dispute using the existing dispute functionality.

The existing dispute record should identify:

- Buyer
- Seller
- Related sale/order or transaction
- Dispute creator
- Dispute reason
- Description
- Creation date
- Current status

Reuse existing fields wherever possible.

### Step 2 — Conversation initialization

When a dispute is created:

- Associate a discussion with the dispute.
- Make the conversation accessible to the buyer and seller involved.
- Make it accessible to authorized administrators.
- Notify the opposing party and relevant administrators through the existing notification system, if available.

A separate conversation table is not required if the existing Dispute record can directly serve as the conversation identifier.

### Step 3 — Buyer and seller discussion

Both parties can:

- Send messages.
- Read previous messages.
- Explain their positions.
- Respond to questions.
- Clarify misunderstandings.

All messages should be associated with the same DisputeId.

### Step 4 — Administrator participation

The administrator should be able to:

- Read the entire conversation.
- Send messages to both parties.
- Ask for clarification.
- Request additional information.
- Explain applicable marketplace policies.
- Review the conversation before making a decision.

Admin messages should appear in the same chronological conversation.

### Step 5 — Final resolution

The administrator reviews:

- Original dispute details.
- Buyer's messages.
- Seller's messages.
- Administrator's own messages.
- Existing dispute evidence.
- Relevant sale and payment information.

The administrator then uses the existing dispute-resolution functionality.

Do not replace the existing resolution mechanism with the messaging system.

---

## 4. Database Design

Inspect the existing database and models before implementing these changes.

### 4.1 Proposed DisputeMessage model

Create a `DisputeMessage` entity for messages associated with a dispute.

| Property  | Suggested type | Purpose                        |
| --------- | -------------- | ------------------------------ |
| Id        | int            | Primary key                    |
| DisputeId | int            | Associated dispute             |
| SenderId  | string         | Authenticated user's ID        |
| Message   | string         | Message content                |
| CreatedAt | DateTime       | Message timestamp              |
| IsRead    | bool           | Basic read status, if required |

Use the actual key types from the existing project.

### Important design rules

- Every message must belong to one dispute.
- Every message must have one authenticated sender.
- Buyer, seller, and administrator messages share the same message table.
- Do not create separate BuyerMessage, SellerMessage, and AdminMessage tables.
- Sender identity must be obtained from the authenticated user.
- Message history must be preserved.

### 4.2 Relationships

Expected relationships:

- One Dispute has many DisputeMessages.
- Each DisputeMessage belongs to one Dispute.
- Each DisputeMessage belongs to one authenticated user.

Use the existing Identity configuration and user relationships.

### 4.3 Optional fields for later

The following are not required for the initial implementation:

- EditedAt
- IsEdited
- AttachmentUrl
- MessageType
- ReadAt
- ParentMessageId

Do not add them unless the existing requirements justify them.

---

## 5. API Design

Inspect the existing dispute routes and avoid creating duplicate functionality.

The following endpoints are proposed.

### 5.1 Retrieve conversation

`GET /api/disputes/{id}/messages`

Purpose:

- Retrieve messages for a specific dispute.
- Return messages in chronological order.
- Include sender information.
- Verify that the requester is an authorized participant or administrator.

Suggested response information:

- Message ID
- Dispute ID
- Sender ID
- Sender name
- Sender role or participant type
- Message content
- CreatedAt

### 5.2 Send message

`POST /api/disputes/{id}/messages`

Purpose:

- Send a message to the shared dispute conversation.

Example request:

```json
{
  "message": "I would like to clarify the delivery situation."
}
```

The backend must:

1. Validate the dispute.
2. Verify that the dispute allows discussion.
3. Identify the authenticated sender.
4. Verify the sender's participation or administrator permission.
5. Validate the message content.
6. Save the message.
7. Trigger notifications where applicable.

Do not accept SenderId as a trusted request value.

### 5.3 Submit formal objection

`POST /api/disputes/{id}/response`

Purpose:

Allow the opposing party to submit an official objection or response to the original complaint.

This should be distinguishable from ordinary conversation messages.

The response should be stored as part of the dispute record or through an appropriate response entity, depending on the existing model design.

Do not force the system to interpret every chat message as a formal objection.

### 5.4 Mark messages as read

`PUT /api/disputes/{id}/messages/read`

Purpose:

Mark messages as read by the current user.

This can be deferred if individual read tracking is not needed initially.

If read tracking is implemented, avoid using one shared `IsRead` flag to represent read status for all three participants. Each participant needs their own read state.

---

## 6. Authorization and Security

This is a critical part of the feature.

### Buyer permissions

The buyer can:

- View the conversation for their own dispute.
- Send messages.
- Submit their explanation.
- Read seller and administrator messages.

### Seller permissions

The seller can:

- View the conversation for their own dispute.
- Send messages.
- Submit their objection.
- Read buyer and administrator messages.

### Administrator permissions

The administrator can:

- View any dispute conversation they are authorized to manage.
- Send messages.
- Review all participants' messages.
- Resolve the dispute using existing authorization rules.

### Security requirements

1. Do not trust a SenderId received from the frontend.
2. Use the authenticated user's ID.
3. Verify buyer and seller ownership against the actual dispute.
4. Verify administrator permissions using the existing authorization approach.
5. Prevent unrelated users from accessing messages.
6. Validate all message content.
7. Prevent messages from being submitted to nonexistent disputes.
8. Prevent ordinary messages after a dispute is finalized, unless an authorized administrator reopens it.
9. Do not expose internal user information unnecessarily.

Never rely only on frontend role checks.

---

## 7. Dispute Lifecycle Integration

The discussion system must work with the existing dispute statuses and resolution process.

Inspect the existing `Dispute` model and `DisputeController.cs` before introducing any new status.

### Proposed behavior

| Dispute situation         | Discussion behavior                                            |
| ------------------------- | -------------------------------------------------------------- |
| Newly created             | Conversation available                                         |
| UnderReview               | All three parties can participate                              |
| Waiting for response      | All three parties can participate                              |
| Waiting for clarification | All three parties can participate                              |
| Resolved                  | Conversation becomes read-only                                 |
| Reopened                  | Conversation becomes available again if reopening is supported |

These are behavioral descriptions, not instructions to add new enum values.

### Important rule

The administrator must still be able to resolve a dispute even if:

- The seller never responds.
- The buyer stops participating.
- One party disagrees with the discussion.
- The parties cannot reach an agreement.

The discussion system must not block the existing administrative resolution process.

### Existing resolution endpoint

Preserve the existing:

`PUT /api/dispute/{id}/resolve`

Inspect its current behavior and authorization.

Integrate the discussion history into the administrator's review process without unnecessarily changing the endpoint.

---

## 8. Real-Time Communication: REST API vs SignalR

ASP.NET Core provides SignalR for real-time communication.

Two implementation approaches are possible.

| Approach           | Description                                           | Complexity |
| ------------------ | ----------------------------------------------------- | ---------- |
| REST API + polling | Frontend periodically retrieves new messages          | Lower      |
| SignalR            | Messages are delivered through persistent connections | Higher     |

### Recommendation

Implement REST API messaging first.

The initial system should support:

- Retrieving conversation history.
- Sending messages.
- Refreshing messages.
- Notifications for new messages.

SignalR can be added later if real-time communication is required.

Do not introduce SignalR simply because the feature is called chat.

### Future SignalR implementation

If SignalR is introduced later:

- Use one group per dispute.
- Add only authorized participants to the group.
- Broadcast messages only to the relevant dispute group.
- Verify access before allowing group membership.
- Persist messages in the database before broadcasting them.

The database remains the permanent source of conversation history.

---

## 9. Notifications

Inspect the existing notification implementation before adding anything new.

The following events may generate notifications:

| Event                      | Notification recipient            |
| -------------------------- | --------------------------------- |
| New dispute created        | Opposing party and relevant admin |
| Formal objection submitted | Other party and relevant admin    |
| New buyer message          | Seller and relevant admin         |
| New seller message         | Buyer and relevant admin          |
| New admin message          | Buyer and seller                  |
| Dispute resolved           | Buyer and seller                  |

Avoid notifying the sender about their own message.

Reuse existing notification services and notification storage.

Do not build a second notification system.

---

## 10. Attachments and Evidence

Supporting evidence can be useful in dispute discussions.

Examples include:

- Delivery confirmation.
- Product condition photographs.
- Relevant documents.
- Screenshots of transaction-related information.

However, attachments should be treated as a separate implementation concern.

### Initial version

Start with text messages.

Reuse the existing dispute evidence upload functionality if it already exists.

### Future version

If attachments are introduced:

- Validate file types and sizes.
- Restrict uploads to authorized participants.
- Associate each attachment with a dispute and uploader.
- Store files using the existing file-storage approach.
- Do not expose private files through unrestricted public URLs.

---

## 11. Suggested Implementation Order

Implement the feature in batches.

### Batch 1 — Existing backend analysis

Inspect:

- `Dispute` model
- `DisputeController.cs`
- Existing dispute DTOs
- Existing resolution functionality
- `FaultType` and dispute status handling
- Identity user relationships
- Existing notification services
- Existing evidence upload functionality

Deliverable:

A short report describing what already exists, what can be reused, and what needs to be added.

### Batch 2 — Database and model

- Add `DisputeMessage`.
- Configure its relationships.
- Configure message validation.
- Add the necessary DbSet.
- Generate a migration.
- Verify existing dispute records remain unaffected.

Deliverable:

Updated model, DbContext configuration, and migration.

### Batch 3 — Messaging API

- Implement message retrieval.
- Implement message submission.
- Return sender information.
- Enforce chronological ordering.
- Add appropriate DTOs.
- Add pagination if necessary for long conversations.

Deliverable:

Working three-party REST messaging functionality.

### Batch 4 — Formal objection

- Implement the opposing party's official response.
- Prevent unrelated users from submitting objections.
- Preserve the original complaint.
- Ensure the response is visible to the buyer, seller, and administrator.

Deliverable:

Formal objection functionality separate from ordinary messages.

### Batch 5 — Authorization

- Verify buyer access.
- Verify seller access.
- Verify admin access.
- Prevent unrelated users from reading or sending messages.
- Prevent unauthorized messages after resolution.

Deliverable:

Verified three-party access restrictions.

### Batch 6 — Notification integration

- Notify participants about new disputes.
- Notify relevant parties about objections.
- Notify other participants about new messages.
- Notify participants when a dispute is resolved.

Deliverable:

Integrated notifications using the existing notification system.

### Batch 7 — Existing resolution integration

- Make conversation history available during admin review.
- Preserve existing resolution rules.
- Make resolved conversations read-only.
- Respect existing reopening functionality, if present.

Deliverable:

Complete dispute discussion and resolution workflow.

---

## 12. Required Test Scenarios

| #   | Test                                     | Expected result                                       |
| --- | ---------------------------------------- | ----------------------------------------------------- |
| 1   | Buyer creates a dispute                  | Dispute conversation becomes accessible               |
| 2   | Seller accesses the dispute              | Seller can view the conversation                      |
| 3   | Admin accesses the dispute               | Admin can view the conversation                       |
| 4   | Buyer sends a message                    | Message is saved and visible to all three parties     |
| 5   | Seller sends a message                   | Message is saved and visible to all three parties     |
| 6   | Admin sends a message                    | Message is saved and visible to both parties          |
| 7   | Unrelated buyer attempts access          | Access denied                                         |
| 8   | Unrelated seller attempts access         | Access denied                                         |
| 9   | User submits another user's SenderId     | Backend ignores/rejects the supplied identity         |
| 10  | Opposing party submits an objection      | Official response is saved                            |
| 11  | Unauthorized user sends a message        | Request rejected                                      |
| 12  | Message submitted to nonexistent dispute | Request rejected                                      |
| 13  | Message submitted after resolution       | Request rejected                                      |
| 14  | Admin resolves without seller response   | Resolution still succeeds                             |
| 15  | Admin resolves without buyer response    | Resolution still succeeds                             |
| 16  | Conversation retrieved                   | Messages appear in chronological order                |
| 17  | Notification generated                   | Correct participants receive notifications            |
| 18  | Dispute is resolved                      | Previous conversation remains accessible as read-only |

---

## 13. Out of Scope for Initial Implementation

Do not implement these unless there is an existing requirement:

- General-purpose private messaging.
- User-to-user chat outside disputes.
- Group chat unrelated to disputes.
- Online/offline presence.
- Typing indicators.
- Voice or video calls.
- Message reactions.
- Message editing.
- Message deletion.
- Complex chat room management.
- SignalR, unless required for the initial release.
- A separate messaging microservice.

Keep the initial implementation focused on dispute communication.

---

## 14. Final Acceptance Criteria

The feature is complete when:

- Every dispute has an accessible shared conversation.
- Buyer, seller, and administrator can all participate.
- All messages are associated with the correct dispute.
- Messages identify their actual authenticated sender.
- All three parties can see the same conversation history.
- Formal objections are distinguishable from normal messages.
- Unauthorized users cannot access conversations.
- Administrators can review discussions and resolve disputes.
- Resolved conversations remain available as read-only history.
- Notifications use the existing notification infrastructure.
- Existing dispute resolution and wallet workflows remain functional.
- All required tests pass.

---

## 15. Instructions to the Coding Agent

Work directly with the existing ProductAuction backend.

Do not assume that the proposed models, properties, routes, or status values already exist.

### First, analyze the existing implementation

Report:

1. Current dispute database structure.
2. Existing dispute creation and resolution workflow.
3. How buyer and seller identities are associated with disputes.
4. Existing notification implementation.
5. Existing evidence upload functionality.
6. Which parts can be reused.
7. Which files require modification.
8. Whether a database migration is required.
9. Whether REST API messaging is sufficient for the current architecture.

### Then implement in batches

Follow the implementation order in this document.

Additional instructions:

- Keep the architecture consistent with existing controllers, DTOs, services, and repositories.
- Do not introduce unnecessary abstractions.
- Do not duplicate notification or authorization systems.
- Do not modify unrelated auction, wallet, or sale functionality.
- Preserve all existing dispute records.
- Do not change existing business rules silently.
- Explain any conflicts between this specification and the current code before making significant changes.
- Provide a list of modified files and explain each change.
- Provide migration commands and database verification instructions.

**Primary objective: A secure, shared three-party conversation for every dispute, with the buyer, seller, and administrator participating while preserving the existing administrator-controlled dispute-resolution process.**
