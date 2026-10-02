# Digital Purchase — invoice data contract

Figma Digital Purchase Payment Pending and Payment Success states contain "Unduh Invoice".

Backend projection: **GET `/api/v1/me/orders/{orderId}/invoice`**.

- Requires signed-in Customer Bearer JWT; order owner is enforced in the database. No auth or CMS auth returns 401; another customer receives 404. Response is `private, no-store`.
- Available **immediately after order creation**, including Pending, Failed, and Paid payment statuses. Does not mark payment Paid or trigger fulfillment.
- Contains immutable invoice number (= persisted order number), issued date (= created date), owner name/email, persisted item names/SKUs, unit price, quantity, total, subtotal, discount, shipping, grand total, currency, voucher code, payment status and paid date.
- Does not expose manual-login credentials, sensitive account target fields, provider webhook details, tokens, or privileged CMS reconciliation data.
- `scripts/smoke-customer-auth.sh` verifies guest/admin/other customer restrictions, two identical-SKU line items remain distinct, pending invoice values, paid date updates, and no destination/credential leak.

**Not a PDF endpoint:** returns JSON for the customer FE to display or generate a downloadable invoice. Actual PDF generation and customer FE "Unduh Invoice" wiring are **still outstanding**; don't mark full UI/download parity as done. No tax-invoice or PPN claim is implied by this order summary.

No Xendit, migration, or FE changes.
