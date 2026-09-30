# Customer checkout idempotency contract

Figma Digital Purchase Flow (node 3064:8): retrying Create Order must not make a second order or consume a second discount voucher claim.

Request: POST /api/v1/checkout/orders with Authorization: Bearer <verified-customer-token> and Idempotency-Key: <crypto.randomUUID() UUID v4>. The customer frontend must reuse the SAME key AND identical request body for network retries of the same checkout attempt; generate a NEW key for an intentionally new checkout.

Behavior:
- First accepted request returns HTTP 201, including the regular signed OrderAccessToken and actual payment amount.
- Replay of same key and normalized body returns HTTP 200 with the original order ID/number and current order/payment status, and a fresh signed order access token. Does not create a new order, revalidate expiring destination, or claim voucher twice.
- Reuse of same key with a different request returns HTTP 409. Invalid/non-random UUID returns 400; guest key usage returns 401. Legacy guest checkout without a key still works for backward compatibility, but has no idempotency guarantee.
- Key scoped to verified customer account, HMAC request fingerprint keyed by the raw random UUID, hashed scoped key in DB only. Raw key and login credentials are never persisted for idempotency.
- Transaction-wide PostgreSQL session advisory lock serializes concurrent attempts for the same key; a unique DB index is the final duplicate guard. Lock always released; checkout service uses its existing transaction on that open connection.
- Requires DB migration AddCustomerCheckoutIdempotency.

Limitations: the customer FE is not modified in this PR. It must send the header at real checkout; the API cannot automatically deduplicate unkeyed guest orders.
