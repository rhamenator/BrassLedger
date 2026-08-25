# Security email, invitations, and account recovery

BrassLedger uses security email for operator invitations, email verification, and password recovery. Configure it before inviting operators or relying on self-service recovery.

## Required configuration

Set these values through environment variables, a deployment secret store, or another protected configuration provider. Do not commit SMTP credentials to an `appsettings` file.

| Configuration key | Environment variable | Purpose |
| --- | --- | --- |
| `AccountEmail:Enabled` | `AccountEmail__Enabled` | Must be `true` to enable delivery. |
| `AccountEmail:PublicBaseUrl` | `AccountEmail__PublicBaseUrl` | Public HTTPS origin used to construct links, such as `https://ledger.example.com`. Request `Host` headers are never trusted for security links. |
| `AccountEmail:Host` | `AccountEmail__Host` | SMTP server name. |
| `AccountEmail:Port` | `AccountEmail__Port` | SMTP port, normally `587` for StartTLS or `465` for TLS-on-connect. |
| `AccountEmail:Security` | `AccountEmail__Security` | `StartTls`, `Ssl`, or `SslOnConnect`. Cleartext and opportunistic downgrade modes are rejected. |
| `AccountEmail:UserName` | `AccountEmail__UserName` | SMTP account when authentication is required. |
| `AccountEmail:Password` | `AccountEmail__Password` | SMTP credential; store this as a secret. |
| `AccountEmail:FromAddress` | `AccountEmail__FromAddress` | Valid sender mailbox. |
| `AccountEmail:FromName` | `AccountEmail__FromName` | Display name; defaults to `BrassLedger Security`. |

Optional lifetime and retry controls are `InvitationLifetimeHours` (default 24, bounded to 1–168), `EmailVerificationLifetimeHours` (default 24, bounded to 1–168), `PasswordResetLifetimeMinutes` (default 30, bounded to 10–120), `MaximumDeliveryAttempts` (default 8, bounded to 1–20), and `DeliveryTimeoutSeconds` (default 30, bounded to 5–120).

Example shell configuration:

```bash
export AccountEmail__Enabled=true
export AccountEmail__PublicBaseUrl=https://ledger.example.com
export AccountEmail__Host=smtp.example.com
export AccountEmail__Port=587
export AccountEmail__Security=StartTls
export AccountEmail__UserName=ledger-mailer
export AccountEmail__Password='load-this-from-your-secret-manager'
export AccountEmail__FromAddress=security@example.com
```

Restart BrassLedger after changing configuration. The Administration page reports whether delivery is configured.

## Security and delivery behavior

- Invitations create an inactive operator and inactive company membership. The recipient verifies the address and chooses the password through an expiring, one-use link; administrators never choose or receive the password.
- Existing operators verify their address from **Account security** before password recovery becomes eligible.
- An operator may replace the account address only after password reauthentication. The change rotates the security stamp, signs out existing sessions, makes recovery ineligible, requires verification at the replacement address, and sends a security notice to the prior verified address.
- Password-reset requests deliberately return the same response for eligible and unknown identifiers. Requests are network-rate-limited, and repeated requests for the same account are suppressed for five minutes.
- Tokens contain 256 random bits. Only their SHA-256 hashes are stored. Tokens are bound to the account security stamp and are invalid after a password, MFA, role-security, or session-revocation change.
- Redemption is transactional and concurrency-safe. Exactly one request can claim a token, and the account update must still match the security stamp observed when the token was issued.
- Email addresses and queued message bodies are protected at rest. A normalized SHA-256 lookup value enforces email uniqueness without making encrypted addresses queryable.
- Successful password recovery rotates the security stamp and invalidates existing sessions. A separate notification is queued after the change.
- The action token is removed from the browser address bar and retained only in a short-lived, HTTP-only, same-site cookie. Action pages and API responses use `no-store` caching headers.

## Monitoring and retry

Administration shows the latest company-scoped delivery attempts, a masked recipient, attempt count, bounded error category, and the next retry time. Failed delivery uses exponential retry and becomes permanently failed after the configured attempt ceiling. An authorized user manager can explicitly reset a failed record for another bounded attempt.

The status **Accepted by SMTP** means the configured SMTP server accepted the message. It does not prove that a downstream provider delivered it to the recipient's inbox. Investigate provider logs, suppression lists, SPF/DKIM/DMARC alignment, bounces, and recipient filtering when an accepted message does not arrive.

After SMTP acceptance BrassLedger clears the protected message body. It retains delivery metadata and the provider message identifier for audit and support. Error records intentionally omit provider text that could contain addresses, message content, or tokens.

## Operational verification

Before production use:

1. Use a non-production operator and mailbox to complete invitation, verification, reset, replay rejection, and reset-notification tests.
2. Confirm the public URL is HTTPS and resolves to the intended deployment through the normal reverse proxy.
3. Confirm SMTP certificate validation and authentication succeed without allowing a TLS downgrade.
4. Confirm provider logs correlate with the message identifier and that failures appear in Administration without exposing the link.
5. Document who may retry delivery and how mailbox ownership is verified before an administrator changes an operator's address.

SMTP acceptance and software tests do not replace validation of the organization's mail provider, DNS authentication, retention policy, or incident-response procedure.

The workflow design follows Microsoft's [ASP.NET Core account confirmation and password recovery guidance](https://learn.microsoft.com/aspnet/core/security/authentication/accconfirm?view=aspnetcore-8.0), the [OWASP Forgot Password Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html), and the [OWASP email validation and verification guidance](https://cheatsheetseries.owasp.org/cheatsheets/Email_Validation_and_Verification_Cheat_Sheet.html). SMTP transport uses [MailKit](https://www.nuget.org/packages/MailKit/4.17.0) because Microsoft's `System.Net.Mail.SmtpClient` documentation does not recommend that API for new development.
