# Digital purchase fulfillment status authority

Scope: **backend-only** Figma Digital Purchase order state integrity.

- AUTO_ID item fulfillment is provider-managed. The paid order dispatches its provider attempt; if the item fails, CMS retries through `POST /api/v1/cms/fulfillment/orders/{orderId}/items/{orderItemId}/execute`. CMS `PUT /api/v1/cms/orders/{orderId}/items/{orderItemId}/fulfillment` cannot set any AUTO_ID status, including Completed.
- Game Voucher code fulfillment is system-managed from the encrypted inventory and already disallows generic status mutations.
- Merchandise item fulfillment is shipment-managed: shipping operator must progress Pending → ReadyToShip → Shipped → Delivered through `PUT /api/v1/cms/orders/{id}/shipment`, which invokes `SyncMerchandiseFromShipmentAsync`. Generic CMS item status cannot mark merchandise Completed before delivery.
- MANUAL / MANUAL_LOGIN digital product fulfillment remains editable using the authenticated CMS item endpoint, only **after Paid**, subject to state transitions and immutable Completed/Cancelled.
- Paid state itself is controlled strictly by signed provider webhook or provider status reconciliation, not admin status changes.

## Regression
- `scripts/smoke-auto-id-fulfillment.sh`: failed provider fulfillment rejects CMS override with 409 without changing attempt count, then correctly retries using provider execution endpoint.
- `scripts/smoke-shipment-fulfillment.sh`: merchandise cannot be marked completed from generic CMS before delivery; once Delivered, item and order correctly complete.
- `scripts/smoke-manual-login-credentials.sh`: pre-existing test ensures legitimate manual fulfillment of a paid MANUAL_LOGIN item still succeeds.

No customer/admin FE changes, payment-provider integration or schema migration.
