# Customer authentication — frontend contract

Scope: customer login, registration + verification, and password reset.
Customer auth is completely separate from CMS admin auth. Never use a CMS JWT
for the storefront or send a customer JWT to CMS APIs.

## Endpoints (base: `/api/v1/auth`)

| Method | Route | Request | Success |
| --- | --- | --- | --- |
| POST | `/register` | `{ "name", "email", "password" }` | 202: verification email sent + registrationToken |
| POST | `/change-registration-email` | `{ "registrationToken", "newEmail" }` | 202: new verification email + replacement registrationToken |
| POST | `/login` | `{ "email", "password" }` | 200: customer JWT + profile |
| POST | `/resend-verification` | `{ "email" }` | 202: generic response |
| POST | `/verify-email` | `{ "token" }` | 200: customer JWT + profile, auto-login |
| POST | `/forgot-password` | `{ "email" }` | 202: generic response |
| POST | `/reset-password` | `{ "token", "newPassword", "confirmPassword" }` | 200: password updated |
| GET | `/me` | `Authorization: Bearer <customer JWT>` | 200: profile |

Login and verify success response:
```json
{
  "accessToken": "<jwt>",
  "expiresAt": "<ISO-8601>",
  "customer": {
    "id": "<guid>", "name": "...", "email": "...", "emailVerified": true
  }
}
```
GET /me returns the `customer` object only. The client must send the customer
JWT for protected customer endpoints; admin JWTs are never interchangeable.
The client clears its JWT on sign-out. Resetting a password revokes prior JWTs.

## Figma mapping

- Register (Name, Email, Password) -> POST register -> Verify your email.
  Keep `registrationToken` in memory only while verification is pending.
- Edit email address -> POST change-registration-email with that token; replace the
  stored registrationToken with the new one. Previous verification links and
  previous edit token stop working; send the new link to the corrected address.
- Resend uses POST resend-verification; enforce UI countdown from
  `resendAfterSeconds` (45 by default) and display the generic response.
- Verification link opens `/auth/verify-email?token=...`; call POST verify-email.
  Success carries a JWT: store session and show Email verified, then Continue to Zetruv.
- Invalid/expired verification link: `VERIFICATION_LINK_INVALID` or
  `VERIFICATION_LINK_EXPIRED`. Request a fresh link with resend-verification.
  The most recently issued link is the only unconsumed valid link.
- Forgot password -> POST forgot-password -> Check your email, masked email
  client-side. Do not distinguish existing and unknown accounts.
- Reset link opens `/auth/reset-password?token=...`; call POST reset-password.
  On success show Password updated -> Back to login (no auto-login on reset).
- Password: 8–256 characters, >=1 uppercase ASCII letter, >=1 digit;
  confirm must match, and last five used passwords cannot be reused.
- Link lifetime: verification 24 hours, reset 15 minutes.
- Show returned `code` for invalid/expired links, password mismatch/reuse,
  invalid credentials, and `EMAIL_NOT_VERIFIED` (403 on valid credentials).
- A 429 is the IP/endpoint anti-abuse limit (10 requests per minute).
  Wait before retrying; do not treat it as a successful email send.
- Errors from email delivery use 503 `EMAIL_DELIVERY_UNAVAILABLE`.
  Do not pretend a verification/reset message was delivered.

## Email delivery config

`CUSTOMER_EMAIL_PROVIDER=resend` with
`CUSTOMER_EMAIL_FROM=no-reply@<verified-sending-domain>` and
`CUSTOMER_EMAIL_RESEND_API_KEY=<secret>`. Alternatively,
`CUSTOMER_EMAIL_PROVIDER=smtp` with SMTP host/port/credentials, and from.
The key must stay in the per-environment `.env`, never in git or frontend.
The from domain must be verified by the provider before claiming email works.
The application fails closed (503) when email is not configured or the
configured callback origin is invalid/insecure outside Development.

For integration tests only, `CustomerEmail__Provider=capture` writes
messages to a private temporary directory and is rejected outside Development.

## Required FE wiring (not part of this backend PR)

The existing `AuthModal.jsx` currently advances state without calling the API.
Replace dummy transitions with actual requests, handle link URLs on page load,
store the customer JWT, call /me on reload, and do not navigate to reset form
from the Open email button without a valid link token. Open email should open
the mailbox; only the email link supplies a reset/verification token.
Figma verified state calls for auto-login using the verification response.
